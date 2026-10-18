using Microsoft.Extensions.Hosting.WindowsServices;

namespace PitCrew.Connector.Features.Sync;

internal sealed class ConnectorProcessLifetime : IConnectorProcessLifetime
{
  public void Fail()
  {
    if (OperatingSystem.IsWindows() &&
        WindowsServiceHelpers.IsWindowsService())
    {
      // Reporting a normal service stop first would bypass SCM failure recovery.
      Environment.Exit(1);
    }
    Environment.ExitCode = 1;
  }
}
