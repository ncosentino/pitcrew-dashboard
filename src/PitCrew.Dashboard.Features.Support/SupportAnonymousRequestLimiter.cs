namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportAnonymousRequestLimiter(TimeProvider _timeProvider)
{
  private const int MaximumKeys = 1024;
  private const int FunctionalPermitLimit = 30;
  private const int SourcePermitLimit = 240;
  private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
  private readonly Dictionary<string, Counter> _functionalCounters =
      new(StringComparer.Ordinal);
  private readonly Dictionary<string, Counter> _sourceCounters =
      new(StringComparer.Ordinal);
  private readonly object _gate = new();

  public bool Allow(
      string operation,
      string networkIdentity,
      string functionalPartition)
  {
    var sourceKey = $"{operation}|{networkIdentity}";
    var functionalKey = $"{sourceKey}|{functionalPartition}";
    var now = _timeProvider.GetUtcNow();
    lock (_gate)
    {
      return Allow(
              _sourceCounters,
              sourceKey,
              SourcePermitLimit,
              now) &&
          Allow(
              _functionalCounters,
              functionalKey,
              FunctionalPermitLimit,
              now);
    }
  }

  private static bool Allow(
      Dictionary<string, Counter> counters,
      string key,
      int permitLimit,
      DateTimeOffset now)
  {
    if (!counters.TryGetValue(key, out var counter))
    {
      if (counters.Count >= MaximumKeys)
      {
        foreach (var expired in counters
            .Where(item => now - item.Value.WindowStartedAt >= Window)
            .Select(item => item.Key)
            .ToArray())
        {
          counters.Remove(expired);
        }
      }
      if (counters.Count >= MaximumKeys)
      {
        return false;
      }
      counter = new Counter(now);
      counters.Add(key, counter);
    }
    if (now - counter.WindowStartedAt >= Window)
    {
      counter.WindowStartedAt = now;
      counter.Count = 0;
    }
    if (counter.Count >= permitLimit)
    {
      return false;
    }
    counter.Count++;
    return true;
  }

  private sealed class Counter(DateTimeOffset windowStartedAt)
  {
    public DateTimeOffset WindowStartedAt { get; set; } = windowStartedAt;

    public int Count { get; set; }
  }
}
