using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal static class SupportPublicKeyValidator
{
  public static bool AreValid(string signingPublicKeySpki, string encryptionPublicKeySpki) =>
      IsPublicKeyValid(signingPublicKeySpki, ecdsa: true) &&
      IsPublicKeyValid(encryptionPublicKeySpki, ecdsa: false);

  private static bool IsPublicKeyValid(string value, bool ecdsa)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 4096)
    {
      return false;
    }
    try
    {
      if (ecdsa)
      {
        using var key = SupportKeyFactory.ImportEcdsaPublicKey(value);
        return key.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7";
      }
      using var rsa = SupportKeyFactory.ImportRsaPublicKey(value);
      return rsa.KeySize == 3072;
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
      return false;
    }
    catch (FormatException)
    {
      return false;
    }
  }
}
