namespace PitCrew.Support.Broker.App;

internal static class SupportBrokerServerFactory
{
  public static ISupportBrokerServer Create(
      SupportBrokerOptions options,
      SupportDiagnosticsBroker broker)
  {
    if (OperatingSystem.IsWindows())
    {
      return new SupportBrokerPipeServer(options, broker);
    }
    if (OperatingSystem.IsLinux())
    {
      return new SupportBrokerUnixSocketServer(options, broker);
    }
    throw new PlatformNotSupportedException(
        "PitCrew support broker IPC supports Windows and Linux only.");
  }
}
