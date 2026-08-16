namespace PitCrew.Support.Protocol;

/// <summary>
/// Exported asymmetric support identity material.
/// </summary>
/// <param name="PublicKeySubjectPublicKeyInfoBase64Url">SubjectPublicKeyInfo-encoded public key.</param>
/// <param name="PrivateKeyPkcs8Base64Url">PKCS#8-encoded private key.</param>
public sealed record SupportKeyPair(
    string PublicKeySubjectPublicKeyInfoBase64Url,
    string PrivateKeyPkcs8Base64Url);

/// <summary>
/// Node-generated support keys used for request verification and result sealing.
/// </summary>
/// <param name="Signing">ECDSA P-256 key used to sign results.</param>
/// <param name="Encryption">RSA 3072 key used to decrypt requests.</param>
public sealed record SupportNodeKeySet(
    SupportKeyPair Signing,
    SupportKeyPair Encryption);

/// <summary>
/// Dashboard support keys kept outside the opaque relay.
/// </summary>
/// <param name="AuthorizationSigning">ECDSA P-256 key used to authorize requests.</param>
/// <param name="ResultEncryption">RSA 3072 key used to decrypt node results.</param>
public sealed record SupportDashboardKeySet(
    SupportKeyPair AuthorizationSigning,
    SupportKeyPair ResultEncryption);
