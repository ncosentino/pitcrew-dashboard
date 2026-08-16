namespace PitCrew.Support.Broker.App;

internal static class UnixSocketAccessPolicy
{
  public const UnixFileMode RequiredMode =
      UnixFileMode.UserRead |
      UnixFileMode.UserWrite |
      UnixFileMode.GroupRead |
      UnixFileMode.GroupWrite;

  public static bool IsExpected(
      UnixSocketMetadata metadata,
      uint brokerUid,
      uint ipcGroupGid) =>
      metadata.IsSocket &&
      metadata.UserId == brokerUid &&
      metadata.GroupId == ipcGroupGid &&
      metadata.Mode == RequiredMode;
}
