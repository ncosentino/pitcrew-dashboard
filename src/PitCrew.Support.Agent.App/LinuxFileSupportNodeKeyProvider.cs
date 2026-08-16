using System.Security.Cryptography;
using System.Text;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class LinuxFileSupportNodeKeyProvider(
    IUnixFilePermissions _permissions) : ISupportNodeKeyProvider
{
  private const string SigningFileName = "signing-key.pk8";
  private const string EncryptionFileName = "encryption-key.pk8";
  private const string CredentialFileName = "transport-credential";
  private const UnixFileMode DirectoryMode =
      UnixFileMode.UserRead |
      UnixFileMode.UserWrite |
      UnixFileMode.UserExecute;
  private const UnixFileMode FileMode =
      UnixFileMode.UserRead |
      UnixFileMode.UserWrite;

  public string Name => "linux-pkcs8";

  public void SecureDirectory(string directoryPath) =>
      _permissions.Set(directoryPath, DirectoryMode);

  public void SecureFile(string filePath) =>
      _permissions.Set(filePath, FileMode);

  public bool IsFileSecure(string filePath) =>
      File.Exists(filePath) &&
      _permissions.Get(filePath) == FileMode &&
      _permissions.IsOwnedByCurrentUser(filePath);

  public SupportNodeKeyDescriptor Generate(string directoryPath, string keySetId)
  {
    using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using var encryption = RSA.Create(3072);
    WritePrivateKey(
        Path.Combine(directoryPath, SigningFileName),
        signing.ExportPkcs8PrivateKey());
    WritePrivateKey(
        Path.Combine(directoryPath, EncryptionFileName),
        encryption.ExportPkcs8PrivateKey());
    return new SupportNodeKeyDescriptor(
        Name,
        keySetId,
        SigningFileName,
        EncryptionFileName,
        SupportBase64Url.Encode(signing.ExportSubjectPublicKeyInfo()),
        SupportBase64Url.Encode(encryption.ExportSubjectPublicKeyInfo()));
  }

  public bool Validate(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor)
  {
    if (!string.Equals(descriptor.Provider, Name, StringComparison.Ordinal) ||
        !string.Equals(
            descriptor.SigningKeyReference,
            SigningFileName,
            StringComparison.Ordinal) ||
        !string.Equals(
            descriptor.EncryptionKeyReference,
            EncryptionFileName,
            StringComparison.Ordinal) ||
        _permissions.Get(directoryPath) != DirectoryMode ||
        !_permissions.IsOwnedByCurrentUser(directoryPath))
    {
      return false;
    }
    var signingPath = Path.Combine(directoryPath, SigningFileName);
    var encryptionPath = Path.Combine(directoryPath, EncryptionFileName);
    if (!File.Exists(signingPath) ||
        !File.Exists(encryptionPath) ||
        _permissions.Get(signingPath) != FileMode ||
        _permissions.Get(encryptionPath) != FileMode ||
        !_permissions.IsOwnedByCurrentUser(signingPath) ||
        !_permissions.IsOwnedByCurrentUser(encryptionPath))
    {
      return false;
    }
    try
    {
      using var signing = OpenSigningKey(directoryPath, descriptor);
      using var encryption = OpenEncryptionKey(directoryPath, descriptor);
      return string.Equals(
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
    catch (IOException)
    {
      return false;
    }
  }

  public ECDsa OpenSigningKey(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor)
  {
    var bytes = File.ReadAllBytes(Path.Combine(
        directoryPath,
        descriptor.SigningKeyReference));
    try
    {
      var key = ECDsa.Create();
      key.ImportPkcs8PrivateKey(bytes, out _);
      return key;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(bytes);
    }
  }

  public RSA OpenEncryptionKey(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor)
  {
    var bytes = File.ReadAllBytes(Path.Combine(
        directoryPath,
        descriptor.EncryptionKeyReference));
    try
    {
      var key = RSA.Create();
      key.ImportPkcs8PrivateKey(bytes, out _);
      return key;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(bytes);
    }
  }

  public void WriteCredential(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor,
      string credential)
  {
    var bytes = Encoding.UTF8.GetBytes(credential);
    try
    {
      WriteSecretFile(Path.Combine(directoryPath, CredentialFileName), bytes);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(bytes);
    }
  }

  public string? ReadCredential(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor)
  {
    var path = Path.Combine(directoryPath, CredentialFileName);
    if (!File.Exists(path) ||
        _permissions.Get(path) != FileMode ||
        !_permissions.IsOwnedByCurrentUser(path))
    {
      return null;
    }
    var bytes = File.ReadAllBytes(path);
    try
    {
      return Encoding.UTF8.GetString(bytes);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(bytes);
    }
  }

  public void DeleteKeySet(string directoryPath, string keySetId)
  {
    DeleteIfExists(Path.Combine(directoryPath, SigningFileName));
    DeleteIfExists(Path.Combine(directoryPath, EncryptionFileName));
    DeleteCredential(directoryPath);
  }

  public void DeleteCredential(string directoryPath) =>
      DeleteIfExists(Path.Combine(directoryPath, CredentialFileName));

  private void WritePrivateKey(string path, byte[] bytes)
  {
    try
    {
      WriteSecretFile(path, bytes);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(bytes);
    }
  }

  private void WriteSecretFile(string path, byte[] bytes)
  {
    var temporaryPath = $"{path}.{Guid.NewGuid():N}.new";
    File.WriteAllBytes(temporaryPath, bytes);
    SecureFile(temporaryPath);
    File.Move(temporaryPath, path, overwrite: true);
    SecureFile(path);
  }

  private static void DeleteIfExists(string path)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
    }
  }
}
