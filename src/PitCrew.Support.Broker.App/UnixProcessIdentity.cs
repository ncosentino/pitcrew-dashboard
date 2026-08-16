using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("linux")]
internal static class UnixProcessIdentity
{
  public static uint GetEffectiveUserId() => GetEffectiveUserIdNative();

  public static uint GetEffectiveGroupId() => GetEffectiveGroupIdNative();

  [DllImport("libc", EntryPoint = "geteuid")]
  private static extern uint GetEffectiveUserIdNative();

  [DllImport("libc", EntryPoint = "getegid")]
  private static extern uint GetEffectiveGroupIdNative();
}
