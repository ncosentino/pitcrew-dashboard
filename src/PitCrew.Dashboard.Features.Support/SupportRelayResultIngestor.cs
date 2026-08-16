using System.Security.Cryptography;
using System.Text.Json;

using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportRelayResultIngestor(
    ISupportStore _supportStore,
    SupportRelayManagementClient _relayClient,
    DashboardSupportKeyService _keyService,
    TimeProvider _timeProvider)
{
  private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

  public async Task<SupportDiagnosticSession> IngestOrCurrentAsync(
      SupportDiagnosticSession session,
      SupportIdentity identity,
      CancellationToken cancellationToken)
  {
    if (session.Status == SupportDiagnosticSessionStatus.Completed)
    {
      return session;
    }
    var resultJson = await _relayClient.FetchResultOrNullAsync(
        session.SessionId,
        cancellationToken);
    if (string.IsNullOrWhiteSpace(resultJson))
    {
      return session;
    }
    if (resultJson.Length > 4_194_304)
    {
      return session;
    }
    SupportResultPayload payload;
    SupportResultAttestation attestation;
    string reportJson;
    try
    {
      var resultEnvelope = JsonSerializer.Deserialize<SupportEnvelope>(resultJson, _jsonOptions);
      if (resultEnvelope is null)
      {
        return session;
      }
      var publicKeyBytes = SupportBase64Url.Decode(identity.NodeSigningPublicKeySpki);
      var publicKeyFingerprint = Convert.ToHexString(
              SHA256.HashData(publicKeyBytes))
          .ToLowerInvariant();
      if (!string.Equals(
              publicKeyFingerprint,
              session.NodeSigningKeyFingerprint,
              StringComparison.Ordinal))
      {
        return session;
      }
      using var nodeSigning = SupportKeyFactory.ImportEcdsaPublicKey(
          identity.NodeSigningPublicKeySpki);
      var packageBytes = SupportEnvelopeCryptography.OpenOrNull(
          resultEnvelope,
          nodeSigning,
          _keyService.ResultDecryptionKey);
      if (packageBytes is null || packageBytes.Length > 3_145_728)
      {
        return session;
      }
      var package = JsonSerializer.Deserialize<SupportSignedResultPackage>(
          packageBytes,
          _jsonOptions);
      if (package is null ||
          string.IsNullOrWhiteSpace(package.PayloadBase64Url) ||
          string.IsNullOrWhiteSpace(package.SignatureBase64Url) ||
          package.PayloadBase64Url.Length > 4_194_304 ||
          package.SignatureBase64Url.Length > 128)
      {
        return session;
      }
      var payloadBytes = SupportBase64Url.Decode(package.PayloadBase64Url);
      var signatureBytes = SupportBase64Url.Decode(package.SignatureBase64Url);
      if (payloadBytes.Length > 3_145_728 ||
          signatureBytes.Length != 64 ||
          !nodeSigning.VerifyData(
              payloadBytes,
              signatureBytes,
              HashAlgorithmName.SHA256,
              DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
      {
        return session;
      }
      payload = JsonSerializer.Deserialize<SupportResultPayload>(
          payloadBytes,
          _jsonOptions)!;
      if (payload is null ||
          !SupportDiagnosticReportValidator.IsSafeMarkdown(
              payload.Markdown) ||
          !string.Equals(payload.TenantId, session.TenantId, StringComparison.Ordinal) ||
          payload.NodeId != session.NodeId ||
          payload.SessionId != session.SessionId ||
          !string.Equals(payload.Capability, session.Capability, StringComparison.Ordinal) ||
          !string.Equals(payload.RequestDigest, session.RequestDigest, StringComparison.Ordinal) ||
          payload.ExpiresAt != session.ExpiresAt ||
          !SupportDiagnosticReportValidator.IsValid(
              payload.Report,
              session.DiagnosticMode,
              session.ProfileId,
              session.PackageId))
      {
        return session;
      }
      reportJson = payload.Report.GetRawText();
      if (reportJson.Length > 2_097_152)
      {
        return session;
      }
      attestation = new SupportResultAttestation(
          Convert.ToBase64String(publicKeyBytes),
          package.PayloadBase64Url,
          package.SignatureBase64Url,
          SupportEnvelopeCryptography.SignatureAlgorithm);
    }
    catch (JsonException)
    {
      return session;
    }
    catch (FormatException)
    {
      return session;
    }
    catch (CryptographicException)
    {
      return session;
    }
    catch (ArgumentException)
    {
      return session;
    }
    _ = await _supportStore.CompleteSessionAsync(
        session.TenantId,
        session.SessionId,
        resultJson,
        reportJson,
        payload.Markdown,
        JsonSerializer.Serialize(attestation, _jsonOptions),
        _timeProvider.GetUtcNow(),
        cancellationToken);
    return await _supportStore.GetSessionOrNullAsync(
        session.TenantId,
        session.SessionId,
        cancellationToken) ?? session;
  }

}
