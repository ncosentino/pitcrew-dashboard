namespace PitCrew.Support.Agent.App;

internal interface IUnixFilePermissions
{
  UnixFileMode Get(string path);

  bool IsOwnedByCurrentUser(string path);

  void Set(string path, UnixFileMode mode);
}
