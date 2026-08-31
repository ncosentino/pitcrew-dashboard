using System.Text.RegularExpressions;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Enforces closed strict validation for locally allowed recipe identifiers
/// and registry repository values used by the image rollout executor.
/// </summary>
internal static partial class ImageRolloutRecipePolicy
{
  public static bool IsValidRecipeId(string recipeId) =>
      !string.IsNullOrWhiteSpace(recipeId) &&
      recipeId.Length is >= 1 and <= 100 &&
      recipeId.All(character =>
          character is
              >= 'a' and <= 'z' or
              >= 'A' and <= 'Z' or
              >= '0' and <= '9' or
              '-' or '_');

  public static bool IsValidRegistryRepository(string repository)
  {
    if (string.IsNullOrEmpty(repository) ||
        repository.Length > 255)
    {
      return false;
    }
    foreach (var character in repository)
    {
      if (character is
          ':' or '@' or ' ' or '\t' or '\r' or '\n' or
          '\"' or '\\' or '<' or '>' or '|' or '?' or '*' or '#' ||
          char.IsControl(character))
      {
        return false;
      }
    }
    if (repository.Contains("://", StringComparison.Ordinal))
    {
      return false;
    }
    return RegistryRepositoryPattern().IsMatch(repository);
  }

  [GeneratedRegex(
      @"^(?=.{1,255}$)(?:[a-z0-9]+(?:(?:\.|__|[_-]+)[a-z0-9]+)*)(?:/(?:[a-z0-9]+(?:(?:\.|__|[_-]+)[a-z0-9]+)*))*$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex RegistryRepositoryPattern();
}
