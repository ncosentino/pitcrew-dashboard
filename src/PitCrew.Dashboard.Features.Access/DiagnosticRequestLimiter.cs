using System.Collections.Concurrent;

namespace PitCrew.Dashboard.Features.Access;

internal sealed class DiagnosticRequestLimiter(
    TimeProvider _timeProvider)
{
  private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
  private readonly ConcurrentDictionary<Guid, Counter>
      _credentialCounters = new();
  private readonly ConcurrentDictionary<string, Counter>
      _networkCounters = new(StringComparer.Ordinal);

  public bool AllowCredential(Guid credentialId) =>
      Allow(
          _credentialCounters,
          credentialId,
          120);

  public bool AllowNetwork(string networkIdentity) =>
      Allow(
          _networkCounters,
          networkIdentity,
          240);

  private bool Allow<TKey>(
      ConcurrentDictionary<TKey, Counter> counters,
      TKey key,
      int permitLimit)
      where TKey : notnull
  {
    var now = _timeProvider.GetUtcNow();
    if (counters.Count > 1024)
    {
      foreach (var item in counters)
      {
        if (now - item.Value.WindowStartedAt >= Window + Window)
        {
          counters.TryRemove(item.Key, out _);
        }
      }
    }
    var counter = counters.GetOrAdd(
        key,
        _ => new Counter(now));
    lock (counter)
    {
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
  }

  private sealed class Counter(DateTimeOffset windowStartedAt)
  {
    public DateTimeOffset WindowStartedAt { get; set; } =
        windowStartedAt;

    public int Count { get; set; }
  }
}
