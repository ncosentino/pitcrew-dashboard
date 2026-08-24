using System.Text;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub;

internal static class GitHubTransportValidation
{
  public const int MaximumArtifacts = 100;

  public static bool IsPositiveId(long value) => value > 0;

  public static bool IsRepository(GitHubRepositoryIdentity? repository) =>
      repository is not null &&
      IsPositiveId(repository.Id) &&
      IsOwner(repository.Owner) &&
      IsRepositoryName(repository.Name);

  public static bool IsOwner(string? value) =>
      IsSimpleName(value, 100, allowDot: false);

  public static bool IsRepositoryName(string? value) =>
      IsSimpleName(value, 100, allowDot: true) &&
      !value!.EndsWith(".git", StringComparison.OrdinalIgnoreCase);

  public static bool IsWorkflowPath(string? value)
  {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > 256 ||
        value[0] == '/' ||
        value.Contains('\\') ||
        HasControl(value))
    {
      return false;
    }

    var segments = value.Split('/');
    return segments.Length == 3 &&
        segments[0] == ".github" &&
        segments[1] == "workflows" &&
        segments.All(static segment =>
            segment.Length is > 0 and <= 100 &&
            segment is not "." and not "..");
  }

  public static bool IsReference(string? value) =>
      !string.IsNullOrWhiteSpace(value) &&
      value.Length <= 255 &&
      !HasControl(value) &&
      !value.Contains(' ') &&
      !value.Contains('\\') &&
      !value.Contains("..", StringComparison.Ordinal) &&
      !value.Contains("@{", StringComparison.Ordinal) &&
      !value.StartsWith('/') &&
      !value.EndsWith('/') &&
      !value.StartsWith('.') &&
      !value.EndsWith('.') &&
      !value.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);

  public static bool IsBranchOrTagReference(string? value) =>
      IsReference(value) && !LooksLikeSha1(value);

  public static bool IsSha1(string? value) =>
      value is { Length: 40 } &&
      value.All(static character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static bool LooksLikeSha1(string? value) =>
      value is { Length: 40 } &&
      value.All(static character =>
          character is >= '0' and <= '9' or
              >= 'a' and <= 'f' or
              >= 'A' and <= 'F');

  public static bool CopyInputs(
      IReadOnlyDictionary<string, string>? inputs,
      out IReadOnlyDictionary<string, string> boundedInputs)
  {
    boundedInputs = new Dictionary<string, string>(StringComparer.Ordinal);
    if (inputs is null || inputs.Count > 25)
    {
      return false;
    }

    var totalBytes = 0;
    var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in inputs)
    {
      if (!IsInputKey(pair.Key) ||
          pair.Value is null ||
          pair.Value.Length > 1024 ||
          HasControl(pair.Value))
      {
        return false;
      }
      totalBytes += Encoding.UTF8.GetByteCount(pair.Key);
      totalBytes += Encoding.UTF8.GetByteCount(pair.Value);
      if (totalBytes > 8192)
      {
        return false;
      }
      copy.Add(pair.Key, pair.Value);
    }

    boundedInputs = copy;
    return true;
  }

  public static string RepositoryPath(GitHubRepositoryIdentity repository) =>
      $"repos/{Encode(repository.Owner)}/{Encode(repository.Name)}";

  public static string EncodeWorkflowPath(string path) =>
      string.Join('/', path.Split('/').Select(Encode));

  public static string Encode(string value) => Uri.EscapeDataString(value);

  public static bool GetHttpsUri(string? value, out Uri? uri)
  {
    uri = null;
    return value is { Length: > 0 and <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment);
  }

  public static bool IsBoundedText(string? value, int maximumLength) =>
      value is { Length: > 0 } &&
      value.Length <= maximumLength &&
      !HasControl(value);

  private static bool IsInputKey(string? value) =>
      value is { Length: > 0 and <= 64 } &&
      value.All(static character =>
          character is >= 'a' and <= 'z' or
              >= 'A' and <= 'Z' or
              >= '0' and <= '9' or
              '_' or '-');

  private static bool IsSimpleName(
      string? value,
      int maximumLength,
      bool allowDot) =>
      value is { Length: > 0 } &&
      value.Length <= maximumLength &&
      value[0] != '-' &&
      value[^1] != '-' &&
      value.All(character =>
          character is >= 'a' and <= 'z' or
              >= 'A' and <= 'Z' or
              >= '0' and <= '9' or
              '-' or '_' ||
          allowDot && character == '.');

  private static bool HasControl(string value) =>
      value.Any(char.IsControl);
}
