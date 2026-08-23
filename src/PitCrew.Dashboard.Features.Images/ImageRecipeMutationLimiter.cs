using System.Collections.Concurrent;

namespace PitCrew.Dashboard.Features.Images;

internal sealed class ImageRecipeMutationLimiter(
    TimeProvider _timeProvider)
{
  private const int MaximumTenants = 1024;
  private const int PermitLimit = 10;
  private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
  private readonly object _capacityGate = new();
  private readonly ConcurrentDictionary<string, Counter> _tenantCounters =
      new(StringComparer.Ordinal);

  public bool Acquire(
      string tenantId,
      out DateTimeOffset? retryAt)
  {
    var now = _timeProvider.GetUtcNow();
    Counter counter;
    lock (_capacityGate)
    {
      if (!_tenantCounters.TryGetValue(
              tenantId,
              out var existing))
      {
        if (_tenantCounters.Count >= MaximumTenants)
        {
          var eviction = _tenantCounters
              .OrderBy(static item => item.Value.WindowStartedAt)
              .First();
          _tenantCounters.TryRemove(
              eviction.Key,
              out _);
        }

        counter = new Counter(now);
        _tenantCounters[tenantId] = counter;
      }
      else
      {
        counter = existing;
      }
    }

    lock (counter)
    {
      if (now - counter.WindowStartedAt >= Window)
      {
        counter.WindowStartedAt = now;
        counter.Count = 0;
      }
      if (counter.Count >= PermitLimit)
      {
        retryAt = counter.WindowStartedAt + Window;
        return false;
      }

      counter.Count++;
      retryAt = null;
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
