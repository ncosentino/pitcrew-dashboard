using System.Security.Cryptography;
using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportAgentRequestProcessor(
    SupportAgentOptions _options,
    ILocalDiagnosticsBroker _broker,
    AgentReplayCache _replayCache,
    TimeProvider _timeProvider)
{
  private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

  public async Task<SupportEnvelope?> ProcessAsync(
      Guid expectedSessionId,
      SupportEnvelope envelope,
      CancellationToken cancellationToken)
  {
    using var dashboardSigning = SupportKeyFactory.ImportEcdsaPublicKey(
        _options.DashboardAuthorizationSigningPublicKeySpki);
    using var nodeEncryption = SupportKeyFactory.ImportRsaPrivateKey(
        _options.NodeEncryptionPrivateKeyPkcs8);
    var payload = SupportEnvelopeCryptography.OpenOrNull(
        envelope,
        dashboardSigning,
        nodeEncryption);
    if (payload is null)
    {
      return null;
    }
    var request = JsonSerializer.Deserialize<SupportDiagnosticRequest>(payload, _jsonOptions);
    if (request is null || request.SessionId != expectedSessionId)
    {
      return null;
    }
    var cached = _replayCache.GetResultOrNull(request.SessionId);
    if (cached is not null)
    {
      return cached;
    }
    var validation = SupportRequestValidator.Validate(
        request,
        _options.TenantId,
        _options.NodeId,
        _timeProvider.GetUtcNow(),
        _replayCache.HasNonce);
    if (validation != SupportRequestValidationStatus.Valid)
    {
      return null;
    }
    if (!_replayCache.ClaimNonce(request.Nonce))
    {
      return _replayCache.GetResultOrNull(request.SessionId);
    }
    using var expiryCancellation = new CancellationTokenSource(
        request.ExpiresAt - _timeProvider.GetUtcNow(),
        _timeProvider);
    using var executionCancellation =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            expiryCancellation.Token);
    var diagnostics = await _broker.ExecuteAsync(
        new LocalDiagnosticsRequest(
            request.DiagnosticMode,
            request.ProfileId,
            request.PackageId),
        executionCancellation.Token);
    if (!SupportDiagnosticReportValidator.IsSafeMarkdown(
            diagnostics.Markdown) ||
        !SupportDiagnosticReportValidator.IsValid(
            diagnostics.Report,
            request.DiagnosticMode,
            request.ProfileId,
            request.PackageId))
    {
      return null;
    }
    var resultPayload = new SupportResultPayload(
        request.TenantId,
        request.NodeId,
        request.SessionId,
        request.CapabilityName,
        Convert.ToHexString(
                SHA256.HashData(SupportCanonicalJson.SerializeRequest(request)))
            .ToLowerInvariant(),
        request.ExpiresAt,
        diagnostics.Report,
        diagnostics.Markdown);
    var canonical = SupportCanonicalJson.SerializeResultAttestationPayload(resultPayload);
    using var dashboardEncryption = SupportKeyFactory.ImportRsaPublicKey(
        _options.DashboardResultEncryptionPublicKeySpki);
    using var nodeSigning = SupportKeyFactory.ImportEcdsaPrivateKey(
        _options.NodeSigningPrivateKeyPkcs8);
    var payloadSignature = nodeSigning.SignData(
        canonical,
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    var package = new SupportSignedResultPackage(
        SupportBase64Url.Encode(canonical),
        SupportBase64Url.Encode(payloadSignature));
    var resultEnvelope = SupportEnvelopeCryptography.Seal(
        JsonSerializer.SerializeToUtf8Bytes(package, _jsonOptions),
        dashboardEncryption,
        nodeSigning,
        _options.NodeId.ToString("N"),
        "dashboard-result-v1");
    _replayCache.StoreResult(request.SessionId, resultEnvelope);
    return resultEnvelope;
  }
}
