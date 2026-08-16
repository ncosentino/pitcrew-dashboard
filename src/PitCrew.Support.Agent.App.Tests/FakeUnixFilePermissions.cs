using PitCrew.Support.Agent.App;

namespace PitCrew.Support.Agent.App.Tests;

internal sealed class FakeUnixFilePermissions : IUnixFilePermissions
{
  private readonly Dictionary<string, UnixFileMode> _modes =
      new(StringComparer.Ordinal);
  private readonly HashSet<string> _foreignOwned =
      new(StringComparer.OrdinalIgnoreCase);

  public UnixFileMode Get(string path)
  {
    var fullPath = Path.GetFullPath(path);
    if (_modes.TryGetValue(fullPath, out var mode))
    {
      return mode;
    }
    return Directory.Exists(fullPath)
        ? UnixFileMode.UserRead |
          UnixFileMode.UserWrite |
          UnixFileMode.UserExecute
        : UnixFileMode.UserRead |
          UnixFileMode.UserWrite;
  }

  public void Set(string path, UnixFileMode mode) =>
      _modes[Path.GetFullPath(path)] = mode;

  public bool IsOwnedByCurrentUser(string path) =>
      !_foreignOwned.Contains(Path.GetFullPath(path));

  public void MarkForeignOwned(string path) =>
      _foreignOwned.Add(Path.GetFullPath(path));
}
