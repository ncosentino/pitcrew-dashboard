using System.Security.Claims;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.DisplayNames;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Access;

internal sealed record CreateDiagnosticCredentialInput(
    string Label,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Guid> NodeIds,
    IReadOnlyList<string> ProfileIds);

internal sealed record DiagnosticCredentialCommandResult(
    DiagnosticCredentialMutationStatus Status,
    string? Error,
    DiagnosticCredential? Credential,
    string? RawCredential);

internal interface IDiagnosticCredentialUnitOfWork
{
  Task<DiagnosticCredentialCommandResult> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      CreateDiagnosticCredentialInput input,
      CancellationToken cancellationToken);

  Task<IReadOnlyList<DiagnosticCredential>> GetAllAsync(
      string tenantId,
      CancellationToken cancellationToken);

  Task<DiagnosticCredentialMutationStatus> RevokeAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid credentialId,
      CancellationToken cancellationToken);

  Task<DiagnosticCredentialCommandResult> RotateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid credentialId,
      CancellationToken cancellationToken);
}

internal sealed class DiagnosticCredentialUnitOfWork(
    AccessContextService _accessContextService,
    IDiagnosticCredentialStore _credentialStore,
    TimeProvider _timeProvider) :
    IDiagnosticCredentialUnitOfWork
{
  private const int MaximumRestrictions = 64;

  public async Task<DiagnosticCredentialCommandResult> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      CreateDiagnosticCredentialInput input,
      CancellationToken cancellationToken)
  {
    var actor = await _accessContextService.GetOrNullAsync(
        principal,
        cancellationToken);
    if (actor is null)
    {
      return NotFound();
    }
    var now = _timeProvider.GetUtcNow();
    var validation = Validate(input, now);
    if (validation.Error is not null)
    {
      return new DiagnosticCredentialCommandResult(
          DiagnosticCredentialMutationStatus.Conflict,
          validation.Error,
          null,
          null);
    }
    var token = DiagnosticCredentialToken.Create();
    var credential = new DiagnosticCredential(
        token.CredentialId,
        tenantId,
        validation.Label!,
        actor.User.GitHubUserId,
        now,
        input.ExpiresAt,
        null,
        null,
        null,
        null,
        0,
        validation.NodeIds!,
        validation.ProfileIds!);
    var status = await _credentialStore.CreateAsync(
        new DiagnosticCredentialWrite(
            credential,
            token.Hash),
        cancellationToken);
    return status == DiagnosticCredentialMutationStatus.Succeeded
        ? new DiagnosticCredentialCommandResult(
            status,
            null,
            credential,
            token.Raw)
        : new DiagnosticCredentialCommandResult(
            status,
            status == DiagnosticCredentialMutationStatus.InvalidNode
                ? "Every node restriction must belong to the tenant."
                : null,
            null,
            null);
  }

  public Task<IReadOnlyList<DiagnosticCredential>> GetAllAsync(
      string tenantId,
      CancellationToken cancellationToken) =>
      _credentialStore.GetAllAsync(
          tenantId,
          cancellationToken);

  public async Task<DiagnosticCredentialMutationStatus> RevokeAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid credentialId,
      CancellationToken cancellationToken)
  {
    var actor = await _accessContextService.GetOrNullAsync(
        principal,
        cancellationToken);
    return actor is null
        ? DiagnosticCredentialMutationStatus.NotFound
        : await _credentialStore.RevokeAsync(
            tenantId,
            credentialId,
            actor.User.GitHubUserId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
  }

  public async Task<DiagnosticCredentialCommandResult> RotateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid credentialId,
      CancellationToken cancellationToken)
  {
    var actor = await _accessContextService.GetOrNullAsync(
        principal,
        cancellationToken);
    if (actor is null)
    {
      return NotFound();
    }
    var token = DiagnosticCredentialToken.Create();
    var mutation = await _credentialStore.RotateAsync(
        tenantId,
        credentialId,
        token.CredentialId,
        token.Hash,
        actor.User.GitHubUserId,
        _timeProvider.GetUtcNow(),
        cancellationToken);
    return mutation.Status == DiagnosticCredentialMutationStatus.Succeeded
        ? new DiagnosticCredentialCommandResult(
            mutation.Status,
            null,
            mutation.Credential,
            token.Raw)
        : new DiagnosticCredentialCommandResult(
            mutation.Status,
            null,
            null,
            null);
  }

  private static (
      string? Label,
      IReadOnlyList<Guid>? NodeIds,
      IReadOnlyList<string>? ProfileIds,
      string? Error) Validate(
          CreateDiagnosticCredentialInput input,
          DateTimeOffset now)
  {
    var label = OperatorDisplayName.NormalizeOrNull(input.Label);
    if (label is null)
    {
      return (
          null,
          null,
          null,
          "Credential label must contain between 1 and 128 characters.");
    }
    if (input.ExpiresAt < now.AddMinutes(5) ||
        input.ExpiresAt > now.AddDays(365))
    {
      return (
          null,
          null,
          null,
          "Credential expiry must be between 5 minutes and 365 days in the future.");
    }
    var nodeIds = input.NodeIds
        .Where(nodeId => nodeId != Guid.Empty)
        .Distinct()
        .Order()
        .ToArray();
    if (nodeIds.Length != input.NodeIds.Count ||
        nodeIds.Length > MaximumRestrictions)
    {
      return (
          null,
          null,
          null,
          $"Node restrictions must contain at most {MaximumRestrictions} unique non-empty identifiers.");
    }
    if (input.ProfileIds.Any(string.IsNullOrWhiteSpace))
    {
      return (
          null,
          null,
          null,
          $"Profile restrictions must contain at most {MaximumRestrictions} unique PitCrew profile identifiers.");
    }
    var profileIds = input.ProfileIds
        .Select(profileId => profileId.Trim())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    if (profileIds.Length != input.ProfileIds.Count ||
        profileIds.Length > MaximumRestrictions ||
        profileIds.Any(profileId => !PitCrewProfileId.IsValid(profileId)))
    {
      return (
          null,
          null,
          null,
          $"Profile restrictions must contain at most {MaximumRestrictions} unique PitCrew profile identifiers.");
    }
    return (label, nodeIds, profileIds, null);
  }

  private static DiagnosticCredentialCommandResult NotFound() =>
      new(
          DiagnosticCredentialMutationStatus.NotFound,
          null,
          null,
          null);
}
