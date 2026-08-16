using System.Security.Cryptography;
using System.Text;

namespace PitCrew.Support.Relay.App;

internal static class RelayCredentialHash
{
  public static string Hash(string credential) =>
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
}
