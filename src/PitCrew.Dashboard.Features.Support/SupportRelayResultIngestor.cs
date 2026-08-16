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
    var resultEnvelope = JsonSerializer.Deserialize<SupportEnvelope>(resultJson, _jsonOptions);
    if (resultEnvelope is null)
    {
      return session;
    }
    using var nodeSigning = SupportKeyFactory.ImportEcdsaPublicKey(
        identity.NodeSigningPublicKeySpki);
    var packageBytes = SupportEnvelopeCryptography.OpenOrNull(
        resultEnvelope,
        nodeSigning,
        _keyService.ResultDecryptionKey);
    if (packageBytes is null)
    {
      return session;
    }
    var package = JsonSerializer.Deserialize<SupportSignedResultPackage>(packageBytes, _jsonOptions);
    if (package is null)
    {
      return session;
    }
    var payloadBytes = SupportBase64Url.Decode(package.PayloadBase64Url);
    if (!nodeSigning.VerifyData(
        payloadBytes,
        SupportBase64Url.Decode(package.SignatureBase64Url),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
    {
      return session;
    }
    var payload = JsonSerializer.Deserialize<SupportResultPayload>(payloadBytes, _jsonOptions);
    if (payload is null || payload.SessionId != session.SessionId)
    {
      return session;
    }
    var reportJson = payload.Report.GetRawText();
    var attestation = new SupportResultAttestation(
        identity.NodeSigningPublicKeySpki,
        package.PayloadBase64Url,
        package.SignatureBase64Url,
        SupportEnvelopeCryptography.SignatureAlgorithm);
    var status = await _supportStore.CompleteSessionAsync(
        session.TenantId,
        session.SessionId,
        resultJson,
        reportJson,
        payload.Markdown,
        JsonSerializer.Serialize(attestation, _jsonOptions),
        _timeProvider.GetUtcNow(),
        cancellationToken);
    if (status != SupportMutationStatus.Succeeded)
    {
      return session;
    }
    return await _supportStore.GetSessionOrNullAsync(
        session.TenantId,
        session.SessionId,
        cancellationToken) ?? session;
  }
}

