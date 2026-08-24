using Microsoft.Extensions.Logging;

namespace PitCrew.Dashboard.Features.Support;

internal static partial class SupportRelayLifecycleLog
{
  [LoggerMessage(
      EventId = 1,
      Level = LogLevel.Debug,
      Message = "Support relay session state returned HTTP {StatusCode}.")]
  public static partial void NonSuccessStatus(
      ILogger logger,
      int statusCode);

  [LoggerMessage(
      EventId = 2,
      Level = LogLevel.Warning,
      Message = "Support relay session state refresh failed with {ExceptionType}.")]
  public static partial void RefreshFailed(
      ILogger logger,
      string exceptionType);
}
