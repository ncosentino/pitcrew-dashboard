using System.Text;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class LinuxCanaryConnectorFixture(
    string _runRoot,
    LinuxCanaryCommandRunner _commands) : IAsyncDisposable
{
  public const string Root =
      "/var/lib/pitcrew-connector";
  private static readonly string[] _fileNames =
  [
      "connector-health.json",
      "connector-events.jsonl",
      "connector-health-acknowledgement.json",
  ];
  private IReadOnlyDictionary<string, string>? _snapshot;
  private bool _created;
  private bool _connectorRootCreated;

  public const string HealthRoot =
      "/var/lib/pitcrew-connector/health";

  public async Task CreateAsync(
      CancellationToken cancellationToken)
  {
    _connectorRootCreated = !Directory.Exists(Root);
    if (_connectorRootCreated &&
        await _commands.RunSudoAsync(
            ["install", "-d", "-m", "0755", Root],
            TimeSpan.FromSeconds(15),
            cancellationToken) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-connector-fixture-rejected");
    }
    var healthRootExitCode = await _commands.RunSudoAsync(
            ["install", "-d", "-m", "0755", HealthRoot],
            TimeSpan.FromSeconds(15),
            cancellationToken);
    if (healthRootExitCode != 0)
    {
      if (_connectorRootCreated)
      {
        var cleanupExitCode = await _commands.RunSudoAsync(
            ["rmdir", "--", Root],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        if (cleanupExitCode != 0)
        {
          throw new CanaryScenarioFailureException(
              "linux-connector-cleanup-rejected");
        }
        _connectorRootCreated = false;
      }
      throw new CanaryScenarioFailureException(
          "linux-connector-fixture-rejected");
    }
    _created = true;

    var stagingRoot = Path.Combine(
        _runRoot,
        "scenario",
        "linux-installed",
        "connector-fixture");
    Directory.CreateDirectory(stagingRoot);
    try
    {
      var fixtureValues = new Dictionary<string, string>(
          StringComparer.Ordinal)
      {
        ["connector-health.json"] = "{}",
        ["connector-events.jsonl"] = string.Empty,
        ["connector-health-acknowledgement.json"] =
            """
            {"schemaVersion":1,"updatedAt":"2026-01-01T00:00:00Z","eventIds":[]}
            """,
      };
      foreach (var pair in fixtureValues)
      {
        var sourcePath = Path.Combine(stagingRoot, pair.Key);
        await File.WriteAllTextAsync(
            sourcePath,
            pair.Value,
            new UTF8Encoding(false),
            cancellationToken);
        if (await _commands.RunSudoAsync(
                [
                    "install",
                    "-m",
                    "0644",
                    sourcePath,
                    Path.Combine(HealthRoot, pair.Key),
                ],
                TimeSpan.FromSeconds(15),
                cancellationToken) != 0)
        {
          throw new CanaryScenarioFailureException(
              "linux-connector-fixture-rejected");
        }
      }
    }
    finally
    {
      Directory.Delete(stagingRoot, recursive: true);
    }
    _snapshot = SnapshotFiles();
  }

  public void AssertUnchanged()
  {
    if (_snapshot is null ||
        !SnapshotsEqual(
            _snapshot,
            SnapshotFiles()))
    {
      throw new CanaryScenarioFailureException(
          "connector-health-fixture-mutated");
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (!_created)
    {
      return;
    }
    foreach (var fileName in _fileNames)
    {
      if (await _commands.RunSudoAsync(
              [
                  "rm",
                  "-f",
                  "--",
                  Path.Combine(HealthRoot, fileName),
              ],
              TimeSpan.FromSeconds(15),
              CancellationToken.None) != 0)
      {
        throw new CanaryScenarioFailureException(
            "linux-connector-cleanup-rejected");
      }
    }
    if (await _commands.RunSudoAsync(
            ["rmdir", "--", HealthRoot],
            TimeSpan.FromSeconds(15),
            CancellationToken.None) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-connector-cleanup-rejected");
    }
    if (_connectorRootCreated &&
        await _commands.RunSudoAsync(
            ["rmdir", "--", Root],
            TimeSpan.FromSeconds(15),
            CancellationToken.None) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-connector-cleanup-rejected");
    }
    _created = false;
    _connectorRootCreated = false;
  }

  private static IReadOnlyDictionary<string, string> SnapshotFiles() =>
      Directory.EnumerateFiles(
              HealthRoot,
              "*",
              SearchOption.TopDirectoryOnly)
          .ToDictionary(
              path => Path.GetFileName(path)!,
              path => Convert.ToHexString(
                  System.Security.Cryptography.SHA256.HashData(
                      File.ReadAllBytes(path))),
              StringComparer.Ordinal);

  private static bool SnapshotsEqual(
      IReadOnlyDictionary<string, string> expected,
      IReadOnlyDictionary<string, string> actual) =>
      expected.Count == actual.Count &&
      expected.All(pair =>
          actual.TryGetValue(pair.Key, out var value) &&
          string.Equals(
              pair.Value,
              value,
              StringComparison.Ordinal));
}
