using System.Globalization;
using System.Text.Json;

namespace PitCrew.Support.Protocol;

/// <summary>
/// Writes the fixed-order UTF-8 JSON forms that are signed by support-plane v1.
/// </summary>
public static class SupportCanonicalJson
{
  private static readonly JsonWriterOptions _writerOptions = new()
  {
    Indented = false,
  };

  /// <summary>
  /// Serializes a diagnostic request with the v1 fixed property order.
  /// </summary>
  /// <param name="request">Request to serialize.</param>
  /// <returns>Canonical UTF-8 JSON bytes.</returns>
  public static byte[] SerializeRequest(SupportDiagnosticRequest request)
  {
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
    {
      writer.WriteStartObject();
      writer.WriteString("protocolVersion", request.ProtocolVersion);
      writer.WriteString("tenantId", request.TenantId);
      writer.WriteString("nodeId", request.NodeId.ToString("D"));
      writer.WriteString("sessionId", request.SessionId.ToString("D"));
      writer.WriteString("capabilityName", request.CapabilityName);
      writer.WriteNumber("capabilityVersion", request.CapabilityVersion);
      writer.WriteString("diagnosticMode", request.DiagnosticMode);
      if (request.ProfileId is null)
      {
        writer.WriteNull("profileId");
      }
      else
      {
        writer.WriteString("profileId", request.ProfileId);
      }
      writer.WriteString("packageId", request.PackageId);
      writer.WriteString("issuedAt", request.IssuedAt.ToString("O", CultureInfo.InvariantCulture));
      writer.WriteString("expiresAt", request.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
      writer.WriteString("nonce", request.Nonce);
      writer.WriteEndObject();
    }
    return buffer.ToArray();
  }

  /// <summary>
  /// Serializes a signed envelope without the signature property.
  /// </summary>
  /// <param name="envelope">Envelope to serialize.</param>
  /// <returns>Canonical UTF-8 JSON bytes signed by the sender.</returns>
  public static byte[] SerializeUnsignedEnvelope(SupportEnvelope envelope)
  {
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
    {
      writer.WriteStartObject();
      writer.WriteString("envelopeVersion", envelope.EnvelopeVersion);
      writer.WriteString("contentEncryptionAlgorithm", envelope.ContentEncryptionAlgorithm);
      writer.WriteString("keyWrapAlgorithm", envelope.KeyWrapAlgorithm);
      writer.WriteString("signatureAlgorithm", envelope.SignatureAlgorithm);
      writer.WriteString("senderKeyId", envelope.SenderKeyId);
      writer.WriteString("recipientKeyId", envelope.RecipientKeyId);
      writer.WriteString("wrappedKeyBase64Url", envelope.WrappedKeyBase64Url);
      writer.WriteString("nonceBase64Url", envelope.NonceBase64Url);
      writer.WriteString("ciphertextBase64Url", envelope.CiphertextBase64Url);
      writer.WriteString("tagBase64Url", envelope.TagBase64Url);
      writer.WriteEndObject();
    }
    return buffer.ToArray();
  }

  /// <summary>
  /// Serializes the payload returned to operator skills for independent node-signature verification.
  /// </summary>
  /// <param name="payload">Completed diagnostic result.</param>
  /// <returns>Canonical UTF-8 JSON bytes.</returns>
  public static byte[] SerializeResultAttestationPayload(SupportResultPayload payload)
  {
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
    {
      writer.WriteStartObject();
      writer.WriteString("sessionId", payload.SessionId.ToString("D"));
      writer.WritePropertyName("report");
      payload.Report.WriteTo(writer);
      writer.WriteString("markdown", payload.Markdown);
      writer.WriteEndObject();
    }
    return buffer.ToArray();
  }
}
