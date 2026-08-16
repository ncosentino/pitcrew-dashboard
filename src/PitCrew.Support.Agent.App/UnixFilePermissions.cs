using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PitCrew.Support.Agent.App;

[UnsupportedOSPlatform("windows")]
internal sealed class UnixFilePermissions : IUnixFilePermissions
{
  private const int AtFileDescriptorCurrentWorkingDirectory = -100;
  private const int AtSymbolicLinkNoFollow = 0x100;
  private const uint StatxUserId = 0x00000008;

  public UnixFileMode Get(string path) => File.GetUnixFileMode(path);

  public bool IsOwnedByCurrentUser(string path)
  {
    if (Statx(
        AtFileDescriptorCurrentWorkingDirectory,
        path,
        AtSymbolicLinkNoFollow,
        StatxUserId,
        out var status) != 0)
    {
      throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    return (status.Mask & StatxUserId) != 0 &&
        status.UserId == GetEffectiveUserId();
  }

  public void Set(string path, UnixFileMode mode) =>
      File.SetUnixFileMode(path, mode);

  [DllImport("libc", EntryPoint = "geteuid")]
  private static extern uint GetEffectiveUserId();

  [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
  private static extern int Statx(
      int directoryFileDescriptor,
      [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
      int flags,
      uint mask,
      out StatxBuffer status);

  [StructLayout(LayoutKind.Sequential, Size = 256)]
  private struct StatxBuffer
  {
    public uint Mask;
    public uint BlockSize;
    public ulong Attributes;
    public uint HardLinkCount;
    public uint UserId;
  }
}
