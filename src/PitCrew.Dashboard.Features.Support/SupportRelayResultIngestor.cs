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
    if (payload is null ||
        !string.Equals(payload.TenantId, session.TenantId, StringComparison.Ordinal) ||
        payload.NodeId != session.NodeId ||
        payload.SessionId != session.SessionId ||
        !string.Equals(payload.Capability, session.Capability, StringComparison.Ordinal) ||
        !string.Equals(payload.RequestDigest, session.RequestDigest, StringComparison.Ordinal) ||
        payload.ExpiresAt != session.ExpiresAt ||
        !IsReportValid(payload.Report, session))
    {
      return session;
    }
    var reportJson = payload.Report.GetRawText();
    var publicKeyBytes = SupportBase64Url.Decode(identity.NodeSigningPublicKeySpki);
    var attestation = new SupportResultAttestation(
        Convert.ToBase64String(publicKeyBytes),
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

  private static bool IsReportValid(
      JsonElement report,
      SupportDiagnosticSession session)
  {
    if (!ReadInt32(report, "schemaVersion", out var schemaVersion) ||
        schemaVersion != 1 ||
        !ReadString(report, "collectionScope", out var collectionScope) ||
        !string.Equals(collectionScope, "file-only", StringComparison.Ordinal) ||
        !ReadString(report, "diagnosticMode", out var diagnosticMode) ||
        !string.Equals(diagnosticMode, session.DiagnosticMode, StringComparison.Ordinal) ||
        !ReadString(report, "pitcrewRoot", out var pitCrewRoot) ||
        !string.Equals(pitCrewRoot, "<pitcrew-root>", StringComparison.Ordinal) ||
        !ReadString(report, "packageId", out var packageId) ||
        !string.Equals(packageId, session.PackageId, StringComparison.Ordinal) ||
        !ReadString(report, "collectorSha256", out var collectorSha256) ||
        !IsLowercaseSha256(collectorSha256))
    {
      return false;
    }
    if (session.ProfileId is null)
    {
      return true;
    }
    return ReadString(report, "profile", out var profile) &&
        string.Equals(profile, session.ProfileId, StringComparison.Ordinal);
  }

  private static bool ReadString(
      JsonElement value,
      string propertyName,
      out string result)
  {
    result = string.Empty;
    if (!value.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.String)
    {
      return false;
    }
    result = property.GetString() ?? string.Empty;
    return !string.IsNullOrWhiteSpace(result);
  }

  private static bool ReadInt32(
      JsonElement value,
      string propertyName,
      out int result)
  {
    result = 0;
    return value.TryGetProperty(propertyName, out var property) &&
        property.TryGetInt32(out result);
  }

  private static bool IsLowercaseSha256(string value) =>
      value.Length == 64 &&
      value.All(character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
