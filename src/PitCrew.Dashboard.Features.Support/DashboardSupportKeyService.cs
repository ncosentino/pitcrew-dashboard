using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class DashboardSupportKeyService : IDisposable
{
  private readonly ECDsa _authorizationSigningKey;
  private readonly RSA _resultDecryptionKey;
  private readonly string _authorizationSigningPublicKey;
  private readonly string _resultEncryptionPublicKey;

  public DashboardSupportKeyService(IOptions<SupportPlaneOptions> options)
  {
    if (string.IsNullOrWhiteSpace(options.Value.AuthorizationSigningPrivateKeyPkcs8) ||
        string.IsNullOrWhiteSpace(options.Value.ResultDecryptionPrivateKeyPkcs8))
    {
      var keys = SupportKeyFactory.CreateDashboardKeys();
      _authorizationSigningKey = SupportKeyFactory.ImportEcdsaPrivateKey(
          keys.AuthorizationSigning.PrivateKeyPkcs8Base64Url);
      _resultDecryptionKey = SupportKeyFactory.ImportRsaPrivateKey(
          keys.ResultEncryption.PrivateKeyPkcs8Base64Url);
      _authorizationSigningPublicKey = keys.AuthorizationSigning.PublicKeySubjectPublicKeyInfoBase64Url;
      _resultEncryptionPublicKey = keys.ResultEncryption.PublicKeySubjectPublicKeyInfoBase64Url;
      return;
    }

    _authorizationSigningKey = SupportKeyFactory.ImportEcdsaPrivateKey(
        options.Value.AuthorizationSigningPrivateKeyPkcs8);
    _resultDecryptionKey = SupportKeyFactory.ImportRsaPrivateKey(
        options.Value.ResultDecryptionPrivateKeyPkcs8);
    _authorizationSigningPublicKey = SupportBase64Url.Encode(
        _authorizationSigningKey.ExportSubjectPublicKeyInfo());
    _resultEncryptionPublicKey = SupportBase64Url.Encode(
        _resultDecryptionKey.ExportSubjectPublicKeyInfo());
  }

  public string AuthorizationSigningPublicKeySpki => _authorizationSigningPublicKey;

  public string ResultEncryptionPublicKeySpki => _resultEncryptionPublicKey;

  public ECDsa AuthorizationSigningKey => _authorizationSigningKey;

  public RSA ResultDecryptionKey => _resultDecryptionKey;

  public void Dispose()
  {
    _authorizationSigningKey.Dispose();
    _resultDecryptionKey.Dispose();
  }
}
