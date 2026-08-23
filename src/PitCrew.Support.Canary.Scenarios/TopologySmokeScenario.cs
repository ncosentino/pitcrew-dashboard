using System.Diagnostics;

using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Verifies that an external runner can attach to healthy Dashboard and relay
/// resources through the runtime manifest.
/// </summary>
public sealed class TopologySmokeScenario : ICanaryScenario
{
  /// <inheritdoc />
  public string Id => "topology-smoke-v1";

  /// <inheritdoc />
  public IReadOnlySet<string> RequiredCapabilities { get; } =
      new HashSet<string>(
      [
          CanaryCapabilities.DashboardHttp,
          CanaryCapabilities.RelayHttp,
      ],
      StringComparer.OrdinalIgnoreCase);

  /// <inheritdoc />
  public async Task<CanaryScenarioResult> RunAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(context);
    var startedAt = context.TimeProvider.GetUtcNow();
    var steps = new List<CanaryScenarioStepResult>();
    var dashboard = await ProbeAsync(
        "dashboard-health",
        new Uri(new Uri(runtime.DashboardUrl), "health"),
        cancellationToken);
    steps.Add(dashboard);
    if (dashboard.Status == "failed")
    {
      return Failure(
          runtime,
          steps,
          startedAt,
          dashboard.Category,
          context.TimeProvider);
    }
    var relay = await ProbeAsync(
        "relay-health",
        new Uri(new Uri(runtime.RelayUrl), "healthz"),
        cancellationToken);
    steps.Add(relay);
    return relay.Status == "succeeded"
        ? new CanaryScenarioResult(
            CanaryManifestFile.ScenarioResultSchemaVersion,
            runtime.RunId,
            Id,
            runtime.TopologyProfile,
            "succeeded",
            null,
            steps,
            startedAt,
            context.TimeProvider.GetUtcNow())
        : Failure(
            runtime,
            steps,
            startedAt,
            relay.Category,
            context.TimeProvider);
  }

  private CanaryScenarioResult Failure(
      CanaryRuntimeManifest runtime,
      IReadOnlyList<CanaryScenarioStepResult> steps,
      DateTimeOffset startedAt,
      string category,
      TimeProvider? timeProvider = null) =>
      new(
          CanaryManifestFile.ScenarioResultSchemaVersion,
          runtime.RunId,
          Id,
          runtime.TopologyProfile,
          "failed",
          category,
          steps,
          startedAt,
          (timeProvider ?? TimeProvider.System).GetUtcNow());

  private static async Task<CanaryScenarioStepResult> ProbeAsync(
      string name,
      Uri endpoint,
      CancellationToken cancellationToken)
  {
    var stopwatch = Stopwatch.StartNew();
    try
    {
      using var client = new HttpClient
      {
        Timeout = TimeSpan.FromSeconds(10),
      };
      using var response = await client.GetAsync(
          endpoint,
          HttpCompletionOption.ResponseHeadersRead,
          cancellationToken);
      return new CanaryScenarioStepResult(
          name,
          response.IsSuccessStatusCode ? "succeeded" : "failed",
          response.IsSuccessStatusCode
              ? "healthy"
              : "http-status-rejected",
          stopwatch.ElapsedMilliseconds);
    }
    catch (HttpRequestException)
    {
      return new CanaryScenarioStepResult(
          name,
          "failed",
          "http-unavailable",
          stopwatch.ElapsedMilliseconds);
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      return new CanaryScenarioStepResult(
          name,
          "failed",
          "http-timeout",
          stopwatch.ElapsedMilliseconds);
    }
  }
}
