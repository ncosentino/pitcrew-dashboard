namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Pure path-safety helpers that enforce the connector-controlled
/// <c>ImageRolloutStatePath</c> boundary. The rollout state root is a
/// protected directory provisioned by the installer; every derived
/// <c>ledger</c>/<c>manifests</c> child must remain a real descendant of the
/// canonicalized root and must never be a symbolic link or reparse point that
/// could redirect writes outside the boundary.
/// </summary>
/// <remarks>
/// These helpers are intentionally pure and side-effect free apart from
/// reading directory attributes so they can be unit-tested directly without
/// touching the DI container. Callers should canonicalize the root first,
/// derive each fixed child through <see cref="CombineConfinedChild"/>, and
/// reject reparse points on both the root and each existing child before
/// creating or opening files. Exception messages are intentionally generic
/// so a failure to satisfy the boundary does not leak the resolved local
/// path into logs.
/// </remarks>
internal static class ImageRolloutStatePathGuard
{
  /// <summary>
  /// Canonicalizes a configured rollout state root. The configured value must
  /// be non-blank and fully qualified (an absolute drive path on Windows or
  /// an absolute rooted path on Linux). Relative and drive-relative paths
  /// (for example <c>foo</c> or <c>\foo</c> on Windows) are rejected so the
  /// resolved directory can never depend on the connector's current working
  /// directory at process start.
  /// </summary>
  /// <param name="configuredPath">
  /// The raw <c>ImageRolloutStatePath</c> option value.
  /// </param>
  /// <returns>The canonicalized absolute path.</returns>
  /// <exception cref="InvalidOperationException">
  /// The configured value is blank or is not a fully qualified absolute path.
  /// </exception>
  public static string CanonicalizeStateRoot(string configuredPath)
  {
    if (string.IsNullOrWhiteSpace(configuredPath))
    {
      throw new InvalidOperationException(
          "The configured rollout state path is not set.");
    }
    if (!Path.IsPathFullyQualified(configuredPath))
    {
      throw new InvalidOperationException(
          "The configured rollout state path must be an absolute path.");
    }
    return Path.GetFullPath(configuredPath);
  }

  /// <summary>
  /// Combines a canonicalized root with a fixed simple child name and proves
  /// the derived path is a direct child of the root. The child name must be
  /// a bounded simple identifier (no separators, no <c>..</c>, no <c>.</c>);
  /// the derived path is validated with a separator-aware
  /// <see cref="Path.GetRelativePath(string, string)"/> comparison so a
  /// misconfigured root ending in a traversal token cannot silently escape
  /// the boundary.
  /// </summary>
  /// <param name="canonicalRoot">The canonicalized rollout state root.</param>
  /// <param name="childName">
  /// A fixed simple identifier (for example <c>"ledger"</c> or
  /// <c>"manifests"</c>) supplied by the caller.
  /// </param>
  /// <returns>The confined absolute child path.</returns>
  /// <exception cref="InvalidOperationException">
  /// The child name contains separators/traversal tokens, or the resulting
  /// derived path escapes the canonicalized root.
  /// </exception>
  public static string CombineConfinedChild(
      string canonicalRoot,
      string childName)
  {
    if (string.IsNullOrEmpty(childName))
    {
      throw new InvalidOperationException(
          "Rollout state child name must not be empty.");
    }
    if (childName.Contains(Path.DirectorySeparatorChar) ||
        childName.Contains(Path.AltDirectorySeparatorChar) ||
        childName.Equals("..", StringComparison.Ordinal) ||
        childName.Equals(".", StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
          "Rollout state child name must be a simple bounded identifier.");
    }
    var derived = Path.GetFullPath(Path.Combine(canonicalRoot, childName));
    var relative = Path.GetRelativePath(canonicalRoot, derived);
    // A properly confined child's relative path is exactly the child name in
    // the platform's canonical form. Any traversal, absolute path, or
    // mismatched segment count means the derived path escapes the root.
    if (!string.Equals(relative, childName, StringComparison.Ordinal) ||
        relative.Length == 0 ||
        relative.StartsWith("..", StringComparison.Ordinal) ||
        Path.IsPathRooted(relative))
    {
      throw new InvalidOperationException(
          "The derived rollout state child escapes the configured root.");
    }
    return derived;
  }

  /// <summary>
  /// Rejects an existing directory that is a symbolic link or Windows reparse
  /// point (junction). Non-existent directories are permitted because the
  /// caller is about to create them under a validated root.
  /// </summary>
  /// <param name="directoryPath">The canonicalized directory path.</param>
  /// <exception cref="UnauthorizedAccessException">
  /// The directory exists and has the reparse-point attribute set. The
  /// message is intentionally generic and does not include the resolved
  /// local path.
  /// </exception>
  public static void EnsureNotReparsePoint(string directoryPath)
  {
    if (!Directory.Exists(directoryPath))
    {
      return;
    }
    FileAttributes attributes;
    try
    {
      attributes = File.GetAttributes(directoryPath);
    }
    catch (FileNotFoundException)
    {
      return;
    }
    catch (DirectoryNotFoundException)
    {
      return;
    }
    if ((attributes & FileAttributes.ReparsePoint) != 0)
    {
      throw new UnauthorizedAccessException(
          "The configured rollout state directory or one of its immediate "
          + "children is a symbolic link or reparse point; the connector "
          + "refuses to follow it. Reinstall the connector with "
          + "-EnableImageRollout so the installer can provision a real "
          + "protected directory.");
    }
  }
}
