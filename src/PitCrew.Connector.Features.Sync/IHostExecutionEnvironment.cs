namespace PitCrew.Connector.Features.Sync;

internal interface IHostExecutionEnvironment
{
  /// <summary>
  /// Gets whether this connector process runs inside the read-only container image.
  /// </summary>
  bool IsContainer { get; }
}
