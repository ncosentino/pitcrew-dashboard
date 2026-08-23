using System.Security.Cryptography;

namespace PitCrew.Support.Protocol;

/// <summary>
/// Seals, signs, verifies, and decrypts support-plane v1 envelopes with built-in .NET cryptography.
/// </summary>
public static class SupportEnvelopeCryptography
{
  /// <summary>Envelope format version used by this implementation.</summary>
  public const string EnvelopeVersion = "support-envelope-v1";

  /// <summary>Content encryption algorithm used by this implementation.</summary>
  public const string ContentEncryptionAlgorithm = "A256GCM";

  /// <summary>Key-wrap algorithm used by this implementation.</summary>
  public const string KeyWrapAlgorithm = "RSA-OAEP-SHA256";

  /// <summary>Signature algorithm used by this implementation.</summary>
  public const string SignatureAlgorithm = "ES256-P1363";

  private const int AesKeyBytes = 32;
  private const int AesNonceBytes = 12;
  private const int AesTagBytes = 16;

  /// <summary>
  /// Encrypts payload bytes for the recipient and signs the resulting envelope.
  /// </summary>
  /// <param name="payload">Canonical payload bytes.</param>
  /// <param name="recipientEncryptionKey">Recipient RSA public key.</param>
  /// <param name="senderSigningKey">Sender ECDSA private key.</param>
  /// <param name="senderKeyId">Bounded sender key identifier.</param>
  /// <param name="recipientKeyId">Bounded recipient key identifier.</param>
  /// <returns>Signed opaque envelope.</returns>
  public static SupportEnvelope Seal(
      ReadOnlySpan<byte> payload,
      RSA recipientEncryptionKey,
      ECDsa senderSigningKey,
      string senderKeyId,
      string recipientKeyId)
  {
    Span<byte> aesKey = stackalloc byte[AesKeyBytes];
    Span<byte> nonce = stackalloc byte[AesNonceBytes];
    RandomNumberGenerator.Fill(aesKey);
    RandomNumberGenerator.Fill(nonce);
    var ciphertext = new byte[payload.Length];
    Span<byte> tag = stackalloc byte[AesTagBytes];
    using (var aes = new AesGcm(aesKey, AesTagBytes))
    {
      aes.Encrypt(nonce, payload, ciphertext, tag);
    }
    var wrappedKey = recipientEncryptionKey.Encrypt(
        aesKey.ToArray(),
        RSAEncryptionPadding.OaepSHA256);
    CryptographicOperations.ZeroMemory(aesKey);
    var unsigned = new SupportEnvelope(
        EnvelopeVersion,
        ContentEncryptionAlgorithm,
        KeyWrapAlgorithm,
        SignatureAlgorithm,
        senderKeyId,
        recipientKeyId,
        SupportBase64Url.Encode(wrappedKey),
        SupportBase64Url.Encode(nonce),
        SupportBase64Url.Encode(ciphertext),
        SupportBase64Url.Encode(tag),
        string.Empty);
    var signature = senderSigningKey.SignData(
        SupportCanonicalJson.SerializeUnsignedEnvelope(unsigned),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    return unsigned with
    {
      SignatureBase64Url = SupportBase64Url.Encode(signature),
    };
  }

  /// <summary>
  /// Verifies the envelope signature and decrypts its payload.
  /// </summary>
  /// <param name="envelope">Envelope received from the relay.</param>
  /// <param name="senderSigningKey">Sender ECDSA public key.</param>
  /// <param name="recipientEncryptionKey">Recipient RSA private key.</param>
  /// <returns>Decrypted payload bytes, or <see langword="null" /> when verification or decryption fails.</returns>
  public static byte[]? OpenOrNull(
      SupportEnvelope envelope,
      ECDsa senderSigningKey,
      RSA recipientEncryptionKey)
  {
    var status = OpenWithStatus(
        envelope,
        senderSigningKey,
        recipientEncryptionKey,
        out var payload);
    return status == SupportEnvelopeOpenStatus.Succeeded
        ? payload
        : null;
  }

  /// <summary>
  /// Verifies and decrypts an envelope while preserving a bounded failure stage.
  /// </summary>
  /// <param name="envelope">Envelope received from the relay.</param>
  /// <param name="senderSigningKey">Sender ECDSA public key.</param>
  /// <param name="recipientEncryptionKey">Recipient RSA private key.</param>
  /// <param name="payload">
  /// Decrypted payload bytes when the result is
  /// <see cref="SupportEnvelopeOpenStatus.Succeeded"/>.
  /// </param>
  /// <returns>A bounded verification or decryption outcome.</returns>
  public static SupportEnvelopeOpenStatus OpenWithStatus(
      SupportEnvelope envelope,
      ECDsa senderSigningKey,
      RSA recipientEncryptionKey,
      out byte[]? payload)
  {
    payload = null;
    if (!IsSupported(envelope))
    {
      return SupportEnvelopeOpenStatus.Unsupported;
    }
    var unsigned = envelope with
    {
      SignatureBase64Url = string.Empty,
    };
    try
    {
      var signature = SupportBase64Url.Decode(envelope.SignatureBase64Url);
      if (!senderSigningKey.VerifyData(
          SupportCanonicalJson.SerializeUnsignedEnvelope(unsigned),
          signature,
          HashAlgorithmName.SHA256,
          DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
      {
        return SupportEnvelopeOpenStatus.SignatureRejected;
      }
    }
    catch (CryptographicException)
    {
      return SupportEnvelopeOpenStatus.SignatureRejected;
    }
    catch (FormatException)
    {
      return SupportEnvelopeOpenStatus.SignatureRejected;
    }

    try
    {
      var wrappedKey = SupportBase64Url.Decode(envelope.WrappedKeyBase64Url);
      var key = recipientEncryptionKey.Decrypt(
          wrappedKey,
          RSAEncryptionPadding.OaepSHA256);
      try
      {
        var nonce = SupportBase64Url.Decode(envelope.NonceBase64Url);
        var ciphertext = SupportBase64Url.Decode(envelope.CiphertextBase64Url);
        var tag = SupportBase64Url.Decode(envelope.TagBase64Url);
        payload = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, AesTagBytes);
        aes.Decrypt(nonce, ciphertext, tag, payload);
        return SupportEnvelopeOpenStatus.Succeeded;
      }
      finally
      {
        CryptographicOperations.ZeroMemory(key);
      }
    }
    catch (CryptographicException)
    {
      if (payload is not null)
      {
        CryptographicOperations.ZeroMemory(payload);
      }
      payload = null;
      return SupportEnvelopeOpenStatus.PayloadRejected;
    }
    catch (FormatException)
    {
      if (payload is not null)
      {
        CryptographicOperations.ZeroMemory(payload);
      }
      payload = null;
      return SupportEnvelopeOpenStatus.PayloadRejected;
    }
  }

  /// <summary>
  /// Verifies a detached support-result attestation payload.
  /// </summary>
  /// <param name="attestation">Attestation returned by Dashboard.</param>
  /// <returns><see langword="true" /> when the node signature is valid.</returns>
  public static bool VerifyAttestation(SupportResultAttestation attestation)
  {
    if (!string.Equals(
        attestation.SignatureAlgorithm,
        SignatureAlgorithm,
        StringComparison.Ordinal))
    {
      return false;
    }
    using var key = ECDsa.Create();
    key.ImportSubjectPublicKeyInfo(
        Convert.FromBase64String(attestation.NodeSigningPublicKeySpki),
        out _);
    return key.VerifyData(
        SupportBase64Url.Decode(attestation.PayloadBase64Url),
        SupportBase64Url.Decode(attestation.SignatureBase64Url),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
  }

  private static bool IsSupported(SupportEnvelope envelope) =>
      string.Equals(envelope.EnvelopeVersion, EnvelopeVersion, StringComparison.Ordinal) &&
      string.Equals(envelope.ContentEncryptionAlgorithm, ContentEncryptionAlgorithm, StringComparison.Ordinal) &&
      string.Equals(envelope.KeyWrapAlgorithm, KeyWrapAlgorithm, StringComparison.Ordinal) &&
      string.Equals(envelope.SignatureAlgorithm, SignatureAlgorithm, StringComparison.Ordinal);
}

/// <summary>
/// Bounded stages returned while opening an opaque support envelope.
/// </summary>
public enum SupportEnvelopeOpenStatus
{
  /// <summary>The envelope was verified and decrypted.</summary>
  Succeeded,

  /// <summary>The envelope names unsupported protocol algorithms.</summary>
  Unsupported,

  /// <summary>The envelope signature or signature encoding was rejected.</summary>
  SignatureRejected,

  /// <summary>The wrapped key or authenticated payload was rejected.</summary>
  PayloadRejected,
}
