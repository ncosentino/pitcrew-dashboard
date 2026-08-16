using System.Security.Cryptography;

namespace PitCrew.Support.Agent.App;

internal sealed class StoredSupportNodeIdentity(
    string _directoryPath,
    ISupportNodeKeyProvider _keyProvider,
    StoredSupportNodeIdentityManifest _manifest,
    string _transportCredential) : ISupportNodePrivateKeySource
{
  public string TenantId => _manifest.TenantId!;

  public Guid NodeId => _manifest.NodeId!.Value;

  public Uri DashboardUrl => new(_manifest.DashboardUrl!, UriKind.Absolute);

  public Uri RelayUrl => new(_manifest.RelayUrl!, UriKind.Absolute);

  public string TransportCredential => _transportCredential;

  public string DashboardAuthorizationSigningPublicKeySpki =>
      _manifest.DashboardAuthorizationSigningPublicKeySpki!;

  public string DashboardResultEncryptionPublicKeySpki =>
      _manifest.DashboardResultEncryptionPublicKeySpki!;

  public string ReplayRoot => _manifest.ReplayRoot;

  public string PipeName => _manifest.PipeName;

  public ECDsa OpenSigningKey() =>
      _keyProvider.OpenSigningKey(_directoryPath, _manifest.Keys);

  public RSA OpenEncryptionKey() =>
      _keyProvider.OpenEncryptionKey(_directoryPath, _manifest.Keys);
}
