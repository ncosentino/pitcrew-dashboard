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

  public async Task<SupportAgentRequestProcessingResult> ProcessAsync(
      Guid expectedSessionId,
      SupportEnvelope envelope,
      CancellationToken cancellationToken)
  {
    using var dashboardSigning = SupportKeyFactory.ImportEcdsaPublicKey(
        _options.DashboardAuthorizationSigningPublicKeySpki);
    using var nodeEncryption = _options.PrivateKeys.OpenEncryptionKey();
    var payload = SupportEnvelopeCryptography.OpenOrNull(
        envelope,
        dashboardSigning,
        nodeEncryption);
    if (payload is null)
    {
      return new SupportAgentRequestProcessingResult(
          SupportAgentRequestProcessingStatus.EnvelopeRejected,
          null,
          null);
    }
    SupportDiagnosticRequest? request;
    try
    {
      request = JsonSerializer.Deserialize<SupportDiagnosticRequest>(
          payload,
          _jsonOptions);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(payload);
    }
    if (request is null)
    {
      return new SupportAgentRequestProcessingResult(
          SupportAgentRequestProcessingStatus.RequestMalformed,
          null,
          null);
    }
    if (request.SessionId != expectedSessionId)
    {
      return new SupportAgentRequestProcessingResult(
          SupportAgentRequestProcessingStatus.SessionMismatch,
          null,
          null);
    }
    var cached = _replayCache.GetResultOrNull(request.SessionId);
    if (cached is not null)
    {
      return new SupportAgentRequestProcessingResult(
          SupportAgentRequestProcessingStatus.Cached,
          cached,
          null);
    }
    var validation = SupportRequestValidator.Validate(
        request,
        _options.TenantId,
        _options.NodeId,
        _timeProvider.GetUtcNow(),
        _replayCache.HasNonce);
    if (validation != SupportRequestValidationStatus.Valid)
    {
      return new SupportAgentRequestProcessingResult(
          SupportAgentRequestProcessingStatus.ValidationRejected,
          null,
          validation);
    }
    if (!_replayCache.ClaimNonce(request.Nonce))
    {
      var raced = _replayCache.GetResultOrNull(
          request.SessionId);
      return new SupportAgentRequestProcessingResult(
          raced is null
              ? SupportAgentRequestProcessingStatus.ReplayPending
              : SupportAgentRequestProcessingStatus.Cached,
          raced,
          raced is null
              ? SupportRequestValidationStatus.Replay
              : null);
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
      return new SupportAgentRequestProcessingResult(
          SupportAgentRequestProcessingStatus.BrokerOutputRejected,
          null,
          null);
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
    using var nodeSigning = _options.PrivateKeys.OpenSigningKey();
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
    return new SupportAgentRequestProcessingResult(
        SupportAgentRequestProcessingStatus.Succeeded,
        resultEnvelope,
        null);
  }
}

internal sealed record SupportAgentRequestProcessingResult(
    SupportAgentRequestProcessingStatus Status,
    SupportEnvelope? ResultEnvelope,
    SupportRequestValidationStatus? ValidationStatus);

internal enum SupportAgentRequestProcessingStatus
{
  Succeeded,
  Cached,
  EnvelopeRejected,
  RequestMalformed,
  SessionMismatch,
  ValidationRejected,
  ReplayPending,
  BrokerOutputRejected,
}
