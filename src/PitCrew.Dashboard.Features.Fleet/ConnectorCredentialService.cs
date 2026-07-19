using System.Security.Cryptography;
using System.Text;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class ConnectorCredentialService
{
  public string CreateCredential()
  {
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
  }

  public bool Matches(
      string expected,
      string candidate)
  {
    var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
    return CryptographicOperations.FixedTimeEquals(expectedHash, candidateHash);
  }

  public string Hash(string credential) =>
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
}
