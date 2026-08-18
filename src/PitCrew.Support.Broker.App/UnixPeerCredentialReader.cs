using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("linux")]
internal static class UnixPeerCredentialReader
{
  private const int SolSocket = 1;
  private const int SoPeerCred = 17;

  public static UnixPeerCredentials Read(Socket socket)
  {
    var length = (uint)Marshal.SizeOf<NativePeerCredentials>();
    if (GetSocketOption(
        socket.SafeHandle.DangerousGetHandle(),
        SolSocket,
        SoPeerCred,
        out var credentials,
        ref length) != 0)
    {
      throw new Win32Exception(
          Marshal.GetLastPInvokeError(),
          "Could not read Unix socket peer credentials.");
    }
    return new UnixPeerCredentials(
        credentials.ProcessId,
        credentials.UserId,
        credentials.GroupId);
  }

  [DllImport(
      "libc",
      EntryPoint = "getsockopt",
      SetLastError = true)]
  private static extern int GetSocketOption(
      IntPtr socket,
      int level,
      int optionName,
      out NativePeerCredentials optionValue,
      ref uint optionLength);

  [StructLayout(LayoutKind.Sequential)]
  private struct NativePeerCredentials
  {
    public int ProcessId;
    public uint UserId;
    public uint GroupId;
  }
}
