using System.Security.Cryptography;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class LegacySupportNodePrivateKeySource(
    string _signingPrivateKeyPkcs8,
    string _encryptionPrivateKeyPkcs8) : ISupportNodePrivateKeySource
{
  public ECDsa OpenSigningKey() =>
      SupportKeyFactory.ImportEcdsaPrivateKey(_signingPrivateKeyPkcs8);

  public RSA OpenEncryptionKey() =>
      SupportKeyFactory.ImportRsaPrivateKey(_encryptionPrivateKeyPkcs8);
}
