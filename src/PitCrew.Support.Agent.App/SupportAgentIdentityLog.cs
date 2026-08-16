using Microsoft.Extensions.Logging;

namespace PitCrew.Support.Agent.App;

internal static partial class SupportAgentIdentityLog
{
  [LoggerMessage(
      EventId = 10,
      Level = LogLevel.Error,
      Message = "The support identity is unavailable or not active; polling did not start.")]
  public static partial void IdentityUnavailable(ILogger logger);

  [LoggerMessage(
      EventId = 11,
      Level = LogLevel.Warning,
      Message = "The support relay rejected the node credential; the local identity requires explicit operator action.")]
  public static partial void CredentialRejected(ILogger logger);
}
