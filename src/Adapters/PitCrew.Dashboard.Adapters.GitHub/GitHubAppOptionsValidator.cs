using System.Buffers;
using System.Security.Cryptography;
using System.Text;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Adapters.GitHub;

/// <summary>
/// Validates GitHub App transport configuration during generated options binding.
/// </summary>
public sealed class GitHubAppOptionsValidator : IOptionsValidator<GitHubAppOptions>
{
  internal const int MaximumPrivateKeyBytes = 65_536;

  /// <inheritdoc />
  public IEnumerable<ValidationError> Validate(GitHubAppOptions options)
  {
    if (!options.Enabled)
    {
      yield break;
    }

    if (options.AppId <= 0)
    {
      yield return new ValidationError(
          "The enabled GitHub App identifier must be positive.")
      {
        PropertyName = nameof(GitHubAppOptions.AppId),
      };
    }

    if (!IsAllowedBaseAddress(options.BaseAddress))
    {
      yield return new ValidationError(
          "The GitHub API base address must be an HTTPS origin.")
      {
        PropertyName = nameof(GitHubAppOptions.BaseAddress),
      };
    }

    if (options.Timeout < TimeSpan.FromSeconds(1) ||
        options.Timeout > TimeSpan.FromMinutes(2))
    {
      yield return new ValidationError(
          "The GitHub request timeout must be between 1 second and 2 minutes.")
      {
        PropertyName = nameof(GitHubAppOptions.Timeout),
      };
    }

    if (string.IsNullOrWhiteSpace(options.PrivateKeyPath) ||
        options.PrivateKeyPath.Length > 1024 ||
        options.PrivateKeyPath.IndexOfAny(['\r', '\n', '\0']) >= 0)
    {
      yield return new ValidationError(
          "The GitHub App private-key path is invalid.")
      {
        PropertyName = nameof(GitHubAppOptions.PrivateKeyPath),
      };
      yield break;
    }

    if (!Path.IsPathFullyQualified(options.PrivateKeyPath) ||
        !File.Exists(options.PrivateKeyPath))
    {
      yield return new ValidationError(
          "The configured GitHub App private-key file does not exist.")
      {
        PropertyName = nameof(GitHubAppOptions.PrivateKeyPath),
      };
      yield break;
    }

    if (!IsValidPrivateKeyFile(options.PrivateKeyPath))
    {
      yield return new ValidationError(
          "The configured GitHub App private-key file is invalid, malformed, or oversized.")
      {
        PropertyName = nameof(GitHubAppOptions.PrivateKeyPath),
      };
    }
  }

  private static bool IsAllowedBaseAddress(Uri? value) =>
      value is { IsAbsoluteUri: true } &&
      value.Scheme == Uri.UriSchemeHttps &&
      value.UserInfo.Length == 0 &&
      value.Query.Length == 0 &&
      value.Fragment.Length == 0 &&
      value.AbsolutePath == "/";

  private static bool IsValidPrivateKeyFile(string path)
  {
    var bytes = ArrayPool<byte>.Shared.Rent(MaximumPrivateKeyBytes + 1);
    char[]? characters = null;
    try
    {
      var fileInfo = new FileInfo(path);
      if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
          fileInfo.Length is <= 0 or > MaximumPrivateKeyBytes)
      {
        return false;
      }

      using var stream = new FileStream(
          path,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          bufferSize: 4096,
          FileOptions.SequentialScan);
      var total = 0;
      while (total < bytes.Length)
      {
        var read = stream.Read(bytes.AsSpan(total, bytes.Length - total));
        if (read == 0)
        {
          break;
        }
        total += read;
      }
      if (total is <= 0 or > MaximumPrivateKeyBytes)
      {
        return false;
      }

      characters = GC.AllocateUninitializedArray<char>(
          Encoding.UTF8.GetCharCount(bytes.AsSpan(0, total)));
      var characterCount = Encoding.UTF8.GetChars(
          bytes.AsSpan(0, total),
          characters);
      using var rsa = RSA.Create();
      rsa.ImportFromPem(characters.AsSpan(0, characterCount));
      var signature = rsa.SignData(
          "PitCrew GitHub App private-key validation"u8,
          HashAlgorithmName.SHA256,
          RSASignaturePadding.Pkcs1);
      try
      {
        return signature.Length > 0;
      }
      finally
      {
        CryptographicOperations.ZeroMemory(signature);
      }
    }
    catch (ArgumentException)
    {
      return false;
    }
    catch (CryptographicException)
    {
      return false;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(bytes);
      ArrayPool<byte>.Shared.Return(bytes);
      if (characters is not null)
      {
        Array.Clear(characters);
      }
    }
  }
}
