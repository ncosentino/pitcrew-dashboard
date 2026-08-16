using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class CompleteSupportEnrollmentUnitOfWork(
    ISupportStore _supportStore,
    SupportSecretService _secretService,
    DashboardSupportKeyService _keyService,
    SupportRelayManagementClient _relayClient,
    SupportRelayCleanupProcessor _relayCleanupProcessor,
    IOptions<SupportPlaneOptions> _options,
    TimeProvider _timeProvider) : ICompleteSupportEnrollmentUnitOfWork
{
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  public async Task<SupportEnrollmentCompletion> CompleteAsync(
      CompleteSupportEnrollmentInput input,
      CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(input.TenantId) ||
        input.TenantId.Length > 128 ||
        input.EnrollmentCode.Length is < 32 or > 256 ||
        input.CompletionId == Guid.Empty ||
        !SupportPublicKeyValidator.AreValid(
            input.NodeSigningPublicKeySpki,
            input.NodeEncryptionPublicKeySpki))
    {
      return new SupportEnrollmentCompletion(SupportMutationStatus.Invalid, null);
    }
    var now = _timeProvider.GetUtcNow();
    await _supportStore.PurgeExpiredEnrollmentsAsync(
        now,
        limit: 64,
        cancellationToken);
    var enrollmentCodeHash = _secretService.Hash(input.EnrollmentCode);
    var enrollment = await _supportStore.GetEnrollmentOrNullAsync(
        input.TenantId,
        enrollmentCodeHash,
        cancellationToken);
    if (enrollment is null)
    {
      return new SupportEnrollmentCompletion(SupportMutationStatus.Invalid, null);
    }
    if (enrollment.ConsumedAt is not null)
    {
      return await RecoverCompletedEnrollmentAsync(
          enrollment,
          input,
          cancellationToken);
    }
    if (enrollment.ExpiresAt < now)
    {
      return new SupportEnrollmentCompletion(SupportMutationStatus.Invalid, null);
    }

    var transportCredential = _secretService.CreateTransportCredential();
    var identity = new SupportIdentity(
        input.TenantId,
        Guid.NewGuid(),
        enrollment.DisplayName,
        input.NodeSigningPublicKeySpki,
        input.NodeEncryptionPublicKeySpki,
        enrollment.CreatedByGitHubUserId,
        now,
        null,
        null,
        null,
        null,
        1);
    var write = new SupportIdentityWrite(
        identity,
        _secretService.Hash(transportCredential),
        enrollmentCodeHash,
        enrollment.ExpiresAt);
    var credentialEnvelope = CreateCredentialEnvelope(
        identity.NodeId,
        input.CompletionId,
        transportCredential,
        input.NodeEncryptionPublicKeySpki);
    var relayCleanupLeaseId = Guid.NewGuid();
    await _supportStore.QueueRelayCleanupAsync(
        identity.NodeId,
        now,
        relayCleanupLeaseId,
        now.AddMinutes(1),
        cancellationToken);
    var relayStatus = await _relayClient.RegisterNodeAsync(write, cancellationToken);
    if (relayStatus == SupportRelayManagementStatus.Failed)
    {
      await _relayCleanupProcessor.ProcessOwnedAsync(
          identity.NodeId,
          relayCleanupLeaseId,
          cancellationToken);
      return new SupportEnrollmentCompletion(SupportMutationStatus.Conflict, null);
    }
    var status = await _supportStore.CompleteEnrollmentAsync(
        enrollment.EnrollmentId,
        input.CompletionId,
        write,
        credentialEnvelope,
        now,
        now.AddSeconds(
            _options.Value.EnrollmentRecoveryLifetimeSeconds),
        relayCleanupLeaseId,
        cancellationToken);
    if (status != SupportMutationStatus.Succeeded)
    {
      await _relayCleanupProcessor.ProcessOwnedAsync(
          identity.NodeId,
          relayCleanupLeaseId,
          cancellationToken);
      var completed = await _supportStore.GetEnrollmentOrNullAsync(
          input.TenantId,
          enrollmentCodeHash,
          cancellationToken);
      return completed is null
          ? new SupportEnrollmentCompletion(status, null)
          : await RecoverCompletedEnrollmentAsync(
              completed,
              input,
              cancellationToken);
    }
    return Succeeded(identity, transportCredential, credentialEnvelope);
  }

  private async Task<SupportEnrollmentCompletion> RecoverCompletedEnrollmentAsync(
      SupportEnrollment enrollment,
      CompleteSupportEnrollmentInput input,
      CancellationToken cancellationToken)
  {
    if (enrollment.CompletionId != input.CompletionId ||
        enrollment.CompletedNodeId is null ||
        enrollment.TransportCredentialEnvelope is null ||
        enrollment.RecoveryExpiresAt is null ||
        enrollment.RecoveryExpiresAt < _timeProvider.GetUtcNow())
    {
      return new SupportEnrollmentCompletion(SupportMutationStatus.Conflict, null);
    }
    var identity = await _supportStore.GetIdentityOrNullAsync(
        input.TenantId,
        enrollment.CompletedNodeId.Value,
        cancellationToken);
    if (identity is null)
    {
      return new SupportEnrollmentCompletion(SupportMutationStatus.Conflict, null);
    }
    if (identity.RevokedAt is not null)
    {
      return new SupportEnrollmentCompletion(SupportMutationStatus.Revoked, null);
    }
    if (!string.Equals(
            identity.NodeSigningPublicKeySpki,
            input.NodeSigningPublicKeySpki,
            StringComparison.Ordinal) ||
        !string.Equals(
            identity.NodeEncryptionPublicKeySpki,
            input.NodeEncryptionPublicKeySpki,
            StringComparison.Ordinal))
    {
      return new SupportEnrollmentCompletion(SupportMutationStatus.Conflict, null);
    }
    return Succeeded(
        identity,
        transportCredential: null,
        enrollment.TransportCredentialEnvelope);
  }

  private SupportEnrollmentCompletion Succeeded(
      SupportIdentity identity,
      string? transportCredential,
      SupportEnvelope transportCredentialEnvelope) =>
      new(
          SupportMutationStatus.Succeeded,
          new CompletedSupportEnrollment(
              identity,
              transportCredential,
              transportCredentialEnvelope,
              _options.Value.RelayUrl,
              _keyService.AuthorizationSigningPublicKeySpki,
              _keyService.ResultEncryptionPublicKeySpki));

  private SupportEnvelope CreateCredentialEnvelope(
      Guid nodeId,
      Guid completionId,
      string transportCredential,
      string nodeEncryptionPublicKeySpki)
  {
    var payload = JsonSerializer.SerializeToUtf8Bytes(
        new EnrollmentCredentialPayload(
            "support-enrollment-credential-v1",
            nodeId,
            completionId,
            transportCredential),
        _jsonOptions);
    try
    {
      using var encryptionKey =
          SupportKeyFactory.ImportRsaPublicKey(nodeEncryptionPublicKeySpki);
      return SupportEnvelopeCryptography.Seal(
          payload,
          encryptionKey,
          _keyService.AuthorizationSigningKey,
          "dashboard-support-auth-v1",
          nodeId.ToString("N"));
    }
    finally
    {
      CryptographicOperations.ZeroMemory(payload);
    }
  }

  private sealed record EnrollmentCredentialPayload(
      string Schema,
      Guid NodeId,
      Guid CompletionId,
      string TransportCredential);
}
