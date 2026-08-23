namespace PitCrew.Support.Canary.Tests;

internal sealed class EnvironmentVariableScope : IDisposable
{
  private readonly IReadOnlyDictionary<string, string?> _originalValues;

  public EnvironmentVariableScope(
      IReadOnlyDictionary<string, string> values)
  {
    _originalValues = values.Keys.ToDictionary(
        key => key,
        Environment.GetEnvironmentVariable,
        StringComparer.Ordinal);
    foreach (var pair in values)
    {
      Environment.SetEnvironmentVariable(
          pair.Key,
          pair.Value);
    }
  }

  public void Dispose()
  {
    foreach (var pair in _originalValues)
    {
      Environment.SetEnvironmentVariable(
          pair.Key,
          pair.Value);
    }
  }
}
