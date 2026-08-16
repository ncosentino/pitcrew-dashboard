using System.Security.Cryptography;

namespace PitCrew.Support.Agent.App;

internal interface ISupportNodeKeyProvider
{
  string Name { get; }

  void SecureDirectory(string directoryPath);

  void SecureFile(string filePath);

  bool IsFileSecure(string filePath);

  SupportNodeKeyDescriptor Generate(string directoryPath, string keySetId);

  bool Validate(string directoryPath, SupportNodeKeyDescriptor descriptor);

  ECDsa OpenSigningKey(string directoryPath, SupportNodeKeyDescriptor descriptor);

  RSA OpenEncryptionKey(string directoryPath, SupportNodeKeyDescriptor descriptor);

  void WriteCredential(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor,
      string credential);

  string? ReadCredential(
      string directoryPath,
      SupportNodeKeyDescriptor descriptor);

  void DeleteCredential(string directoryPath);

  void DeleteKeySet(string directoryPath, string keySetId);
}
