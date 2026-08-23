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

    var fileInfo = new FileInfo(options.PrivateKeyPath);
    if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
        fileInfo.Length is <= 0 or > MaximumPrivateKeyBytes)
    {
      yield return new ValidationError(
          "The configured GitHub App private-key file is invalid or oversized.")
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
}
