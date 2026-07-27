using System.Globalization;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal enum HistoryQueryStatus
{
  Succeeded,
  Invalid,
  NotFound,
}

internal sealed record HistoryQueryInput(
    string? From,
    string? To,
    string? Resolution,
    string? Points,
    string? Events);

internal sealed record HistoryQueryResult(
    HistoryQueryStatus Status,
    string? Error,
    NodeHistoryResponse? Response);

internal interface IGetFleetHistoryUnitOfWork
{
  Task<HistoryQueryResult> GetNodeHistoryAsync(
      string tenantId,
      Guid nodeId,
      HistoryQueryInput input,
      CancellationToken cancellationToken);

  Task<HistoryQueryResult> GetProfileHistoryAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      HistoryQueryInput input,
      CancellationToken cancellationToken);
}

internal sealed class GetFleetHistoryUnitOfWork(
    IFleetHistoryStore _historyStore,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : IGetFleetHistoryUnitOfWork
{
  public async Task<HistoryQueryResult> GetNodeHistoryAsync(
      string tenantId,
      Guid nodeId,
      HistoryQueryInput input,
      CancellationToken cancellationToken)
  {
    var generatedAt = _timeProvider.GetUtcNow();
    var window = CreateWindow(input, generatedAt, out var error);
    if (window is null)
    {
      return new HistoryQueryResult(
          HistoryQueryStatus.Invalid,
          error,
          null);
    }

    var response = await _historyStore.GetNodeHistoryAsync(
        tenantId,
        nodeId,
        window,
        generatedAt,
        cancellationToken);
    return Complete(response);
  }

  public async Task<HistoryQueryResult> GetProfileHistoryAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      HistoryQueryInput input,
      CancellationToken cancellationToken)
  {
    if (!SyncConnectorUnitOfWork.IsValidProfileId(profileId))
    {
      return new HistoryQueryResult(
          HistoryQueryStatus.Invalid,
          "Profile ID must be a valid manager profile identifier.",
          null);
    }

    var generatedAt = _timeProvider.GetUtcNow();
    var window = CreateWindow(input, generatedAt, out var error);
    if (window is null)
    {
      return new HistoryQueryResult(
          HistoryQueryStatus.Invalid,
          error,
          null);
    }

    var response = await _historyStore.GetProfileHistoryAsync(
        tenantId,
        nodeId,
        profileId,
        window,
        generatedAt,
        cancellationToken);
    return Complete(response);
  }

  private static HistoryQueryResult Complete(
      NodeHistoryResponse? response) =>
      response is null
          ? new HistoryQueryResult(
              HistoryQueryStatus.NotFound,
              null,
              null)
          : new HistoryQueryResult(
              HistoryQueryStatus.Succeeded,
              null,
              response);

  private HistoryWindow? CreateWindow(
      HistoryQueryInput input,
      DateTimeOffset generatedAt,
      out string? error)
  {
    error = null;
    var options = _options.Value;
    var to = ParseTimeOrNull(input.To, generatedAt);
    if (to is null)
    {
      error = "The 'to' bound must be an ISO-8601 timestamp with an offset.";
      return null;
    }
    var from = ParseTimeOrNull(
        input.From,
        to.Value.AddHours(-options.DefaultHistoryRangeHours));
    if (from is null)
    {
      error = "The 'from' bound must be an ISO-8601 timestamp with an offset.";
      return null;
    }
    if (from.Value >= to.Value)
    {
      error = "The 'from' bound must be earlier than the 'to' bound.";
      return null;
    }
    if (to.Value - from.Value >
        TimeSpan.FromHours(options.MaximumHistoryRangeHours))
    {
      error =
          $"A history range cannot exceed {options.MaximumHistoryRangeHours} hours.";
      return null;
    }

    var resolution = HistoryResolution.Raw;
    if (!string.IsNullOrWhiteSpace(input.Resolution))
    {
      if (string.Equals(
          input.Resolution,
          "hourly",
          StringComparison.OrdinalIgnoreCase))
      {
        resolution = HistoryResolution.Hourly;
      }
      else if (!string.Equals(
          input.Resolution,
          "raw",
          StringComparison.OrdinalIgnoreCase))
      {
        error = "Resolution must be 'raw' or 'hourly'.";
        return null;
      }
    }

    var points = ParseLimitOrNull(
        input.Points,
        options.MaximumHistoryPoints);
    if (points is null)
    {
      error =
          $"The 'points' limit must be between 1 and {options.MaximumHistoryPoints}.";
      return null;
    }
    var events = ParseLimitOrNull(
        input.Events,
        options.MaximumHistoryEvents);
    if (events is null)
    {
      error =
          $"The 'events' limit must be between 1 and {options.MaximumHistoryEvents}.";
      return null;
    }

    return new HistoryWindow(
        from.Value,
        to.Value,
        resolution,
        points.Value,
        events.Value);
  }

  private static DateTimeOffset? ParseTimeOrNull(
      string? value,
      DateTimeOffset fallback)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return fallback;
    }

    return DateTimeOffset.TryParse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind,
        out var parsed)
        ? parsed
        : null;
  }

  private static int? ParseLimitOrNull(
      string? value,
      int maximum)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return maximum;
    }

    return int.TryParse(
        value,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var parsed) &&
        parsed >= 1 &&
        parsed <= maximum
        ? parsed
        : null;
  }
}
