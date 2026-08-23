using System.Diagnostics;
using System.Text.Json;

using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class CanaryScenarioExecution(
    CanaryRuntimeManifest _runtime,
    string _scenarioId,
    TimeProvider _timeProvider)
{
  private readonly List<CanaryScenarioStepResult> _steps = [];
  private readonly DateTimeOffset _startedAt = _timeProvider.GetUtcNow();

  public string? FailureCategory { get; private set; }

  public bool Succeeded => FailureCategory is null;

  public async Task RunStepAsync(
      string name,
      Func<CancellationToken, Task<string>> action,
      CancellationToken cancellationToken)
  {
    if (!Succeeded)
    {
      return;
    }
    var stopwatch = Stopwatch.StartNew();
    try
    {
      var category = await action(cancellationToken);
      _steps.Add(new CanaryScenarioStepResult(
          name,
          "succeeded",
          category,
          stopwatch.ElapsedMilliseconds));
    }
    catch (CanaryScenarioFailureException exception)
    {
      RecordFailure(
          name,
          exception.Category,
          stopwatch.ElapsedMilliseconds);
    }
    catch (HttpRequestException)
    {
      RecordFailure(
          name,
          "http-unavailable",
          stopwatch.ElapsedMilliseconds);
    }
    catch (IOException)
    {
      RecordFailure(
          name,
          "filesystem-or-process-io-failed",
          stopwatch.ElapsedMilliseconds);
    }
    catch (UnauthorizedAccessException)
    {
      RecordFailure(
          name,
          "filesystem-forbidden",
          stopwatch.ElapsedMilliseconds);
    }
    catch (JsonException)
    {
      RecordFailure(
          name,
          "json-contract-invalid",
          stopwatch.ElapsedMilliseconds);
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      RecordFailure(
          name,
          "step-timeout",
          stopwatch.ElapsedMilliseconds);
    }
  }

  public CanaryScenarioResult Complete() =>
      new(
          CanaryManifestFile.ScenarioResultSchemaVersion,
          _runtime.RunId,
          _scenarioId,
          _runtime.TopologyProfile,
          Succeeded ? "succeeded" : "failed",
          FailureCategory,
          _steps,
          _startedAt,
          _timeProvider.GetUtcNow());

  private void RecordFailure(
      string name,
      string category,
      long durationMilliseconds)
  {
    FailureCategory = category;
    _steps.Add(new CanaryScenarioStepResult(
        name,
        "failed",
        category,
        durationMilliseconds));
  }
}

internal sealed class CanaryScenarioFailureException : Exception
{
  public CanaryScenarioFailureException()
      : this("scenario-failed")
  {
  }

  public CanaryScenarioFailureException(string category)
      : base(category)
  {
    Category = category;
  }

  public CanaryScenarioFailureException(
      string category,
      Exception innerException)
      : base(category, innerException)
  {
    Category = category;
  }

  public string Category { get; }
}
