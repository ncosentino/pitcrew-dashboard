using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("linux")]
internal static class UnixSocketMetadataReader
{
  private const int AtFileWorkingDirectory = -100;
  private const int AtSymbolicLinkNoFollow = 0x100;
  private const uint StatxBasicStats = 0x7ff;
  private const ushort FileTypeMask = 0xf000;
  private const ushort SocketFileType = 0xc000;
  private const ushort PermissionMask = 0x01ff;

  public static UnixSocketMetadata Read(string path)
  {
    if (Statx(
        AtFileWorkingDirectory,
        path,
        AtSymbolicLinkNoFollow,
        StatxBasicStats,
        out var metadata) != 0)
    {
      throw new Win32Exception(
          Marshal.GetLastPInvokeError(),
          "Could not inspect the support broker socket.");
    }
    return new UnixSocketMetadata(
        metadata.UserId,
        metadata.GroupId,
        (UnixFileMode)(metadata.Mode & PermissionMask),
        (metadata.Mode & FileTypeMask) == SocketFileType);
  }

  [DllImport(
      "libc",
      EntryPoint = "statx",
      SetLastError = true)]
  private static extern int Statx(
      int directoryFileDescriptor,
      [MarshalAs(UnmanagedType.LPUTF8Str)]
      string path,
      int flags,
      uint mask,
      out NativeStatx metadata);

  [StructLayout(LayoutKind.Sequential, Size = 256)]
  private struct NativeStatx
  {
    public uint Mask;
    public uint BlockSize;
    public ulong Attributes;
    public uint HardLinkCount;
    public uint UserId;
    public uint GroupId;
    public ushort Mode;
  }
}
