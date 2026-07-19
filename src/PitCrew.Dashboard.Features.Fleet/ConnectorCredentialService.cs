using System.Security.Cryptography;
using System.Text;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class ConnectorCredentialService
{
  public string CreateNodeCredential() =>
      CreateSecret("pc_node_");

  public string CreateEnrollmentCode() =>
      CreateSecret("pc_enroll_");

  public string Hash(string credential) =>
      Convert.ToHexString(
          SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

  private static string CreateSecret(string prefix)
  {
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return prefix + Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
  }
}
