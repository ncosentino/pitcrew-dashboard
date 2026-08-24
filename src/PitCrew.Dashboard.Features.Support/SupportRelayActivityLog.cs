using Microsoft.Extensions.Logging;

namespace PitCrew.Dashboard.Features.Support;

internal static partial class SupportRelayActivityLog
{
  [LoggerMessage(
      EventId = 1,
      Level = LogLevel.Warning,
      Message = "Support relay activity was not refreshed because the tenant has more than {MaximumCount} identities.")]
  public static partial void BatchTooLarge(
      ILogger logger,
      int maximumCount);

  [LoggerMessage(
      EventId = 2,
      Level = LogLevel.Warning,
      Message = "Support relay activity refresh returned HTTP {StatusCode}.")]
  public static partial void NonSuccessStatus(
      ILogger logger,
      int statusCode);

  [LoggerMessage(
      EventId = 3,
      Level = LogLevel.Warning,
      Message = "Support relay activity refresh returned an invalid bounded projection.")]
  public static partial void InvalidProjection(ILogger logger);

  [LoggerMessage(
      EventId = 4,
      Level = LogLevel.Warning,
      Message = "Support relay activity refresh failed with {ExceptionType}.")]
  public static partial void RefreshFailed(
      ILogger logger,
      string exceptionType);
}
