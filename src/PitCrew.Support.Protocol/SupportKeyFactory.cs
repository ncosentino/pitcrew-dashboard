using System.Security.Cryptography;

namespace PitCrew.Support.Protocol;

/// <summary>
/// Creates and imports built-in .NET asymmetric support-plane keys.
/// </summary>
public static class SupportKeyFactory
{
  /// <summary>
  /// Creates node keys: ECDSA P-256 signing and RSA 3072 encryption.
  /// </summary>
  /// <returns>Exported node keys.</returns>
  public static SupportNodeKeySet CreateNodeKeys() =>
      new(CreateEcdsaKeyPair(), CreateRsa3072KeyPair());

  /// <summary>
  /// Creates Dashboard keys: ECDSA P-256 authorization signing and RSA 3072 result encryption.
  /// </summary>
  /// <returns>Exported Dashboard keys.</returns>
  public static SupportDashboardKeySet CreateDashboardKeys() =>
      new(CreateEcdsaKeyPair(), CreateRsa3072KeyPair());

  /// <summary>
  /// Imports an ECDSA private key encoded as PKCS#8 base64url.
  /// </summary>
  /// <param name="privateKeyBase64Url">Encoded private key.</param>
  /// <returns>Imported ECDSA instance owned by the caller.</returns>
  public static ECDsa ImportEcdsaPrivateKey(string privateKeyBase64Url)
  {
    var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(SupportBase64Url.Decode(privateKeyBase64Url), out _);
    return key;
  }

  /// <summary>
  /// Imports an ECDSA public key encoded as SubjectPublicKeyInfo base64url.
  /// </summary>
  /// <param name="publicKeyBase64Url">Encoded public key.</param>
  /// <returns>Imported ECDSA instance owned by the caller.</returns>
  public static ECDsa ImportEcdsaPublicKey(string publicKeyBase64Url)
  {
    var key = ECDsa.Create();
    key.ImportSubjectPublicKeyInfo(SupportBase64Url.Decode(publicKeyBase64Url), out _);
    return key;
  }

  /// <summary>
  /// Imports an RSA private key encoded as PKCS#8 base64url.
  /// </summary>
  /// <param name="privateKeyBase64Url">Encoded private key.</param>
  /// <returns>Imported RSA instance owned by the caller.</returns>
  public static RSA ImportRsaPrivateKey(string privateKeyBase64Url)
  {
    var key = RSA.Create();
    key.ImportPkcs8PrivateKey(SupportBase64Url.Decode(privateKeyBase64Url), out _);
    return key;
  }

  /// <summary>
  /// Imports an RSA public key encoded as SubjectPublicKeyInfo base64url.
  /// </summary>
  /// <param name="publicKeyBase64Url">Encoded public key.</param>
  /// <returns>Imported RSA instance owned by the caller.</returns>
  public static RSA ImportRsaPublicKey(string publicKeyBase64Url)
  {
    var key = RSA.Create();
    key.ImportSubjectPublicKeyInfo(SupportBase64Url.Decode(publicKeyBase64Url), out _);
    return key;
  }

  private static SupportKeyPair CreateEcdsaKeyPair()
  {
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    return new SupportKeyPair(
        SupportBase64Url.Encode(key.ExportSubjectPublicKeyInfo()),
        SupportBase64Url.Encode(key.ExportPkcs8PrivateKey()));
  }

  private static SupportKeyPair CreateRsa3072KeyPair()
  {
    using var key = RSA.Create(3072);
    return new SupportKeyPair(
        SupportBase64Url.Encode(key.ExportSubjectPublicKeyInfo()),
        SupportBase64Url.Encode(key.ExportPkcs8PrivateKey()));
  }
}
