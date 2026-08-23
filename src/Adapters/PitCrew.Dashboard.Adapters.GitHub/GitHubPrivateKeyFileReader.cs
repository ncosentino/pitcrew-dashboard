using System.Buffers;
using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed class GitHubPrivateKeyFileReader(IOptions<GitHubAppOptions> _options)
{
  public async Task<GitHubClientOutcome<byte[]>> ReadAsync(
      CancellationToken cancellationToken)
  {
    var path = _options.Value.PrivateKeyPath;
    if (string.IsNullOrWhiteSpace(path) ||
        path.Length > 1024 ||
        !Path.IsPathFullyQualified(path))
    {
      return Failure("private-key-path-invalid");
    }

    try
    {
      var fileInfo = new FileInfo(path);
      if (!fileInfo.Exists)
      {
        return Failure("private-key-file-missing");
      }
      if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
      {
        return Failure("private-key-file-reparse-point");
      }
      if (fileInfo.Length is <= 0 or > GitHubAppOptionsValidator.MaximumPrivateKeyBytes)
      {
        return Failure("private-key-file-size-invalid");
      }

      await using var stream = new FileStream(
          path,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          bufferSize: 4096,
          FileOptions.Asynchronous | FileOptions.SequentialScan);
      var rented = ArrayPool<byte>.Shared.Rent(
          GitHubAppOptionsValidator.MaximumPrivateKeyBytes + 1);
      try
      {
        var total = 0;
        while (total < rented.Length)
        {
          var read = await stream.ReadAsync(
              rented.AsMemory(total, rented.Length - total),
              cancellationToken);
          if (read == 0)
          {
            break;
          }
          total += read;
        }
        if (total == 0 ||
            total > GitHubAppOptionsValidator.MaximumPrivateKeyBytes ||
            await stream.ReadAsync(rented.AsMemory(0, 1), cancellationToken) != 0)
        {
          return Failure("private-key-file-size-invalid");
        }

        var keyBytes = GC.AllocateUninitializedArray<byte>(total);
        rented.AsSpan(0, total).CopyTo(keyBytes);
        return new(
            GitHubClientOutcomeKind.Success,
            keyBytes,
            null,
            null);
      }
      finally
      {
        CryptographicOperations.ZeroMemory(rented);
        ArrayPool<byte>.Shared.Return(rented);
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return new(GitHubClientOutcomeKind.Cancelled, null, null, "cancelled");
    }
    catch (IOException)
    {
      return Failure("private-key-file-unavailable");
    }
    catch (UnauthorizedAccessException)
    {
      return Failure("private-key-file-unavailable");
    }
  }

  private static GitHubClientOutcome<byte[]> Failure(string detail) =>
      new(GitHubClientOutcomeKind.InvalidRequest, null, null, detail);
}
