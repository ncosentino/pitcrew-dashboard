using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCngSupportNodeKeyProvider : ISupportNodeKeyProvider
{
  private const string CredentialFileName = "transport-credential.cng";

  public string Name => "windows-cng";

  public void SecureDirectory(string directoryPath)
  {
    var identity = CurrentIdentity();
    var security = new DirectorySecurity();
    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    security.SetOwner(identity);
    security.AddAccessRule(new FileSystemAccessRule(
        identity,
        FileSystemRights.FullControl,
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
        PropagationFlags.None,
        AccessControlType.Allow));
    new DirectoryInfo(directoryPath).SetAccessControl(security);
  }

  public void SecureFile(string filePath)
  {
    var identity = CurrentIdentity();
    var security = new FileSecurity();
    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    security.SetOwner(identity);
    security.AddAccessRule(new FileSystemAccessRule(
        identity,
        FileSystemRights.FullControl,
        AccessControlType.Allow));
    new FileInfo(filePath).SetAccessControl(security);
  }

  public bool IsFileSecure(string filePath) =>
      File.Exists(filePath) &&
      IsAccessControlSecure(
          new FileInfo(filePath).GetAccessControl(
              AccessControlSections.Access | AccessControlSections.Owner));

  public SupportNodeKeyDescriptor Generate(string directoryPath, string keySetId)
  {
    var signingName = SigningName(keySetId);
    var encryptionName = EncryptionName(keySetId);
    DeleteKey(signingName);
    DeleteKey(encryptionName);
    try
    {
      using var signingKey = CngKey.Create(
          CngAlgorithm.ECDsaP256,
          signingName,
          new CngKeyCreationParameters
          {
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
          });
      var encryptionParameters = new CngKeyCreationParameters
      {
        ExportPolicy = CngExportPolicies.None,
        KeyUsage = CngKeyUsages.Decryption,
        Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
      };
      encryptionParameters.Parameters.Add(new CngProperty(
          "Length",
          BitConverter.GetBytes(3072),
          CngPropertyOptions.None));
      using var encryptionKey = CngKey.Create(
          CngAlgorithm.Rsa,
          encryptionName,
          encryptionParameters);
      using var signing = new ECDsaCng(signingKey);
      using var encryption = new RSACng(encryptionKey);
      return new SupportNodeKeyDescriptor(
          Name,
          keySetId,
          signingName,
          encryptionName,
          SupportBase64Url.Encode(signing.ExportSubjectPublicKeyInfo()),
          SupportBase64Url.Encode(encryption.ExportSubjectPublicKeyInfo()));
    }
    catch (CryptographicException)
    {
      DeleteKey(signingName);
      DeleteKey(encryptionName);
      throw;
    }
  }

  public bool Validate(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor)
  {
    if (!string.Equals(descriptor.Provider, Name, StringComparison.Ordinal) ||
        !string.Equals(
            descriptor.SigningKeyReference,
            SigningName(descriptor.KeySetId),
            StringComparison.Ordinal) ||
        !string.Equals(
            descriptor.EncryptionKeyReference,
            EncryptionName(descriptor.KeySetId),
            StringComparison.Ordinal) ||
        !IsDirectorySecure(directoryPath))
    {
      return false;
    }
    try
    {
      using var signing = OpenSigningKey(directoryPath, descriptor);
      using var encryption = OpenEncryptionKey(directoryPath, descriptor);
      return signing.KeySize == 256 &&
          encryption.KeySize == 3072 &&
          string.Equals(
              SupportBase64Url.Encode(signing.ExportSubjectPublicKeyInfo()),
              descriptor.SigningPublicKeySpki,
              StringComparison.Ordinal) &&
          string.Equals(
              SupportBase64Url.Encode(encryption.ExportSubjectPublicKeyInfo()),
              descriptor.EncryptionPublicKeySpki,
              StringComparison.Ordinal);
    }
    catch (CryptographicException)
    {
      return false;
    }
  }

  public ECDsa OpenSigningKey(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor)
  {
    using var key = CngKey.Open(
          descriptor.SigningKeyReference,
          CngProvider.MicrosoftSoftwareKeyStorageProvider);
    return new ECDsaCng(key);
  }

  public RSA OpenEncryptionKey(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor)
  {
    using var key = CngKey.Open(
          descriptor.EncryptionKeyReference,
          CngProvider.MicrosoftSoftwareKeyStorageProvider);
    return new RSACng(key);
  }

  public void WriteCredential(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor,
      string credential)
  {
    using var encryption = OpenEncryptionKey(directoryPath, descriptor);
    var plaintext = Encoding.UTF8.GetBytes(credential);
    byte[]? ciphertext = null;
    try
    {
      ciphertext = encryption.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256);
      var path = Path.Combine(directoryPath, CredentialFileName);
      var temporaryPath = $"{path}.{Guid.NewGuid():N}.new";
      File.WriteAllBytes(temporaryPath, ciphertext);
      SecureFile(temporaryPath);
      File.Move(temporaryPath, path, overwrite: true);
      SecureFile(path);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(plaintext);
      if (ciphertext is not null)
      {
        CryptographicOperations.ZeroMemory(ciphertext);
      }
    }
  }

  public string? ReadCredential(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor)
  {
    var path = Path.Combine(directoryPath, CredentialFileName);
    if (!File.Exists(path))
    {
      return null;
    }
    using var encryption = OpenEncryptionKey(directoryPath, descriptor);
    var ciphertext = File.ReadAllBytes(path);
    byte[]? plaintext = null;
    try
    {
      plaintext = encryption.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA256);
      return Encoding.UTF8.GetString(plaintext);
    }
    catch (CryptographicException)
    {
      return null;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(ciphertext);
      if (plaintext is not null)
      {
        CryptographicOperations.ZeroMemory(plaintext);
      }
    }
  }

  public void DeleteKeySet(string directoryPath, string keySetId)
  {
    DeleteKey(SigningName(keySetId));
    DeleteKey(EncryptionName(keySetId));
    DeleteCredential(directoryPath);
  }

  public void DeleteCredential(string directoryPath)
  {
    var credentialPath = Path.Combine(directoryPath, CredentialFileName);
    if (File.Exists(credentialPath))
    {
      File.Delete(credentialPath);
    }
  }

  private static string SigningName(string keySetId) =>
      $"PitCrew.Support.{keySetId}.signing";

  private static string EncryptionName(string keySetId) =>
      $"PitCrew.Support.{keySetId}.encryption";

  private static bool IsDirectorySecure(string directoryPath) =>
      Directory.Exists(directoryPath) &&
      IsAccessControlSecure(
          new DirectoryInfo(directoryPath).GetAccessControl(
              AccessControlSections.Access | AccessControlSections.Owner));

  private static bool IsAccessControlSecure(
      FileSystemSecurity security)
  {
    var identity = CurrentIdentity();
    if (!security.AreAccessRulesProtected ||
        !identity.Equals(security.GetOwner(typeof(SecurityIdentifier))))
    {
      return false;
    }
    var rules = security.GetAccessRules(
        includeExplicit: true,
        includeInherited: true,
        typeof(SecurityIdentifier));
    foreach (FileSystemAccessRule rule in rules)
    {
      if (rule.AccessControlType == AccessControlType.Allow &&
          !identity.Equals(rule.IdentityReference))
      {
        return false;
      }
    }
    return true;
  }

  private static SecurityIdentifier CurrentIdentity() =>
      WindowsIdentity.GetCurrent().User ??
      throw new InvalidOperationException(
          "The support-agent service identity has no Windows security identifier.");

  private static void DeleteKey(string name)
  {
    if (!CngKey.Exists(
        name,
        CngProvider.MicrosoftSoftwareKeyStorageProvider))
    {
      return;
    }
    using var key = CngKey.Open(
        name,
        CngProvider.MicrosoftSoftwareKeyStorageProvider);
    key.Delete();
  }
}
