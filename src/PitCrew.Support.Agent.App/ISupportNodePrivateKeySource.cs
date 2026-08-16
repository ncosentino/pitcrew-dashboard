using System.Security.Cryptography;

namespace PitCrew.Support.Agent.App;

internal interface ISupportNodePrivateKeySource
{
  ECDsa OpenSigningKey();

  RSA OpenEncryptionKey();
}
