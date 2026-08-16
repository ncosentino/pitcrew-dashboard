using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Dashboard.Kernel.DisplayNames;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class CreateSupportEnrollmentUnitOfWork(
    SupportDashboardAccessService _accessContextService,
    ISupportStore _supportStore,
    SupportSecretService _secretService,
    DashboardSupportKeyService _keyService,
    SupportRelayManagementClient _relayClient,
    IOptions<SupportPlaneOptions> _options,
    TimeProvider _timeProvider) : ICreateSupportEnrollmentUnitOfWork
{
  public async Task<CreatedSupportEnrollment?> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      CreateSupportEnrollmentInput input,
      CancellationToken cancellationToken)
  {
    var actor = await _accessContextService.GetOrNullAsync(principal, cancellationToken);
    if (actor is null)
    {
      return null;
    }
    if (!await _accessContextService.IsTenantAdministratorAsync(
        actor,
        tenantId,
        cancellationToken))
    {
      return null;
    }
    var displayName = OperatorDisplayName.NormalizeOrNull(input.DisplayName);
    if (displayName is null ||
        !IsPublicKeyValid(input.NodeSigningPublicKeySpki, ecdsa: true) ||
        !IsPublicKeyValid(input.NodeEncryptionPublicKeySpki, ecdsa: false))
    {
      return null;
    }
    var now = _timeProvider.GetUtcNow();
    var enrollmentCode = _secretService.CreateEnrollmentCode();
    var transportCredential = _secretService.CreateTransportCredential();
    var identity = new SupportIdentity(
        tenantId,
        Guid.NewGuid(),
        displayName,
        input.NodeSigningPublicKeySpki,
        input.NodeEncryptionPublicKeySpki,
        actor.User.GitHubUserId,
        now,
        null,
        null,
        null,
        null,
        1);
    var expiresAt = now.AddSeconds(_options.Value.EnrollmentLifetimeSeconds);
    var write = new SupportIdentityWrite(
        identity,
        _secretService.Hash(transportCredential),
        _secretService.Hash(enrollmentCode),
        expiresAt);
    var status = await _supportStore.CreateIdentityAsync(
        write,
        cancellationToken);
    if (status != SupportMutationStatus.Succeeded)
    {
      return null;
    }
    var relayStatus = await _relayClient.RegisterNodeAsync(
        write,
        cancellationToken);
    if (relayStatus == SupportRelayManagementStatus.Failed)
    {
      await _supportStore.RevokeIdentityAsync(
          tenantId,
          identity.NodeId,
          actor.User.GitHubUserId,
          now,
          cancellationToken);
      return null;
    }
    return new CreatedSupportEnrollment(
            identity,
            enrollmentCode,
            transportCredential,
            expiresAt,
            _options.Value.RelayUrl,
            _keyService.AuthorizationSigningPublicKeySpki,
            _keyService.ResultEncryptionPublicKeySpki);
  }

  private static bool IsPublicKeyValid(string value, bool ecdsa)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 4096)
    {
      return false;
    }
    try
    {
      if (ecdsa)
      {
        using var key = SupportKeyFactory.ImportEcdsaPublicKey(value);
        return key.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7";
      }
      using var rsa = SupportKeyFactory.ImportRsaPublicKey(value);
      return rsa.KeySize >= 3072;
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
      return false;
    }
    catch (FormatException)
    {
      return false;
    }
  }
}
