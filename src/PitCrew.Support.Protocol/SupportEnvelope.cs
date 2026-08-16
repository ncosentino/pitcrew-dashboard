namespace PitCrew.Support.Protocol;

/// <summary>
/// Signed hybrid-encryption envelope stored and transported by the untrusted relay.
/// </summary>
/// <param name="EnvelopeVersion">Envelope format version.</param>
/// <param name="ContentEncryptionAlgorithm">Content encryption algorithm.</param>
/// <param name="KeyWrapAlgorithm">Recipient key-wrap algorithm.</param>
/// <param name="SignatureAlgorithm">Signature algorithm over the unsigned envelope.</param>
/// <param name="SenderKeyId">Bounded identifier of the signing key.</param>
/// <param name="RecipientKeyId">Bounded identifier of the recipient encryption key.</param>
/// <param name="WrappedKeyBase64Url">RSA-OAEP-SHA256 wrapped AES content key.</param>
/// <param name="NonceBase64Url">AES-GCM nonce.</param>
/// <param name="CiphertextBase64Url">AES-GCM ciphertext.</param>
/// <param name="TagBase64Url">AES-GCM authentication tag.</param>
/// <param name="SignatureBase64Url">ECDSA P-256 IEEE-P1363 signature.</param>
public sealed record SupportEnvelope(
    string EnvelopeVersion,
    string ContentEncryptionAlgorithm,
    string KeyWrapAlgorithm,
    string SignatureAlgorithm,
    string SenderKeyId,
    string RecipientKeyId,
    string WrappedKeyBase64Url,
    string NonceBase64Url,
    string CiphertextBase64Url,
    string TagBase64Url,
    string SignatureBase64Url);
