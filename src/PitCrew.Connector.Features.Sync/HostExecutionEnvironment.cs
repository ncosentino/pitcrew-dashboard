using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync;

[DoNotAutoRegister]
internal sealed class HostExecutionEnvironment : IHostExecutionEnvironment
{
  public bool IsContainer { get; } =
      string.Equals(
          Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
          "true",
          StringComparison.OrdinalIgnoreCase);
}
