using Microsoft.Extensions.Logging;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

internal sealed class RecordingLogger<T> : ILogger<T>
{
  private int _entryCount;

  public int EntryCount => Volatile.Read(ref _entryCount);

  public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull =>
      EmptyScope.Instance;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter) =>
      Interlocked.Increment(ref _entryCount);

  private sealed class EmptyScope : IDisposable
  {
    public static EmptyScope Instance { get; } = new();

    public void Dispose()
    {
    }
  }
}
