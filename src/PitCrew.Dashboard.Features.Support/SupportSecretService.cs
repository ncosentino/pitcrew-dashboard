using System.Security.Cryptography;
using System.Text;

using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportSecretService
{
  public string CreateEnrollmentCode() => CreateSecret("pcs_enroll_");

  public string CreateTransportCredential() => CreateSecret("pcs_node_");

  public string Hash(string secret) =>
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

  public string CreateNonce()
  {
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return SupportBase64Url.Encode(bytes);
  }

  private static string CreateSecret(string prefix)
  {
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return prefix + SupportBase64Url.Encode(bytes);
  }
}
