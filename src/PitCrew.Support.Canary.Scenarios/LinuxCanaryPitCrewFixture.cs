namespace PitCrew.Support.Canary.Scenarios;

internal sealed class LinuxCanaryPitCrewFixture : IAsyncDisposable
{
  private const string BaseRoot =
      "/var/lib/pitcrew-support-canary";
  private readonly string _sourceRoot;
  private readonly LinuxCanaryCommandRunner _commands;
  private IReadOnlyDictionary<string, string>? _snapshot;
  private bool _baseRootCreated;
  private bool _created;

  public LinuxCanaryPitCrewFixture(
      string sourceRoot,
      string runId,
      LinuxCanaryCommandRunner commands)
  {
    if (runId.Length != 32 ||
        runId.Any(character =>
            character is not (
                >= '0' and <= '9' or
                >= 'a' and <= 'f')))
    {
      throw new CanaryScenarioFailureException(
          "linux-pitcrew-fixture-rejected");
    }
    _sourceRoot = sourceRoot;
    _commands = commands;
    RunRoot = Path.Combine(BaseRoot, runId);
    Root = Path.Combine(RunRoot, "pitcrew");
  }

  public string Root { get; }

  private string RunRoot { get; }

  public async Task CreateAsync(
      CancellationToken cancellationToken)
  {
    if (Directory.Exists(RunRoot))
    {
      throw new CanaryScenarioFailureException(
          "linux-pitcrew-fixture-rejected");
    }
    _baseRootCreated = !Directory.Exists(BaseRoot);
    if (_baseRootCreated &&
        await _commands.RunSudoAsync(
            ["install", "-d", "-m", "0755", BaseRoot],
            TimeSpan.FromSeconds(15),
            cancellationToken) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-pitcrew-fixture-rejected");
    }
    if (await _commands.RunSudoAsync(
            ["install", "-d", "-m", "0755", RunRoot],
            TimeSpan.FromSeconds(15),
            cancellationToken) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-pitcrew-fixture-rejected");
    }
    _created = true;
    if (await _commands.RunSudoAsync(
            ["cp", "-a", "--", _sourceRoot, Root],
            TimeSpan.FromSeconds(30),
            cancellationToken) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-pitcrew-fixture-rejected");
    }
    _snapshot = SnapshotFiles(Root);
  }

  public void AssertUnchanged()
  {
    if (_snapshot is null ||
        !SnapshotsEqual(
            _snapshot,
            SnapshotFiles(Root)))
    {
      throw new CanaryScenarioFailureException(
          "pitcrew-fixture-mutated");
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (!_created)
    {
      return;
    }
    if (await _commands.RunSudoAsync(
            ["rm", "-rf", "--", RunRoot],
            TimeSpan.FromSeconds(30),
            CancellationToken.None) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-pitcrew-cleanup-rejected");
    }
    if (_baseRootCreated &&
        await _commands.RunSudoAsync(
            ["rmdir", "--", BaseRoot],
            TimeSpan.FromSeconds(15),
            CancellationToken.None) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-pitcrew-cleanup-rejected");
    }
    _created = false;
    _baseRootCreated = false;
  }

  private static IReadOnlyDictionary<string, string> SnapshotFiles(
      string root)
  {
    var snapshot = new Dictionary<string, string>(
        StringComparer.Ordinal);
    var count = 0;
    foreach (var path in Directory.EnumerateFiles(
        root,
        "*",
        SearchOption.AllDirectories))
    {
      count++;
      var file = new FileInfo(path);
      if (count > 256 ||
          file.Length > 4_194_304 ||
          (file.Attributes & FileAttributes.ReparsePoint) != 0)
      {
        throw new CanaryScenarioFailureException(
            "fixture-snapshot-bound-exceeded");
      }
      snapshot[Path.GetRelativePath(root, path)] =
          Convert.ToHexString(
              System.Security.Cryptography.SHA256.HashData(
                  File.ReadAllBytes(path)));
    }
    return snapshot;
  }

  private static bool SnapshotsEqual(
      IReadOnlyDictionary<string, string> expected,
      IReadOnlyDictionary<string, string> actual) =>
      expected.Count == actual.Count &&
      expected.All(pair =>
          actual.TryGetValue(
              pair.Key,
              out var value) &&
          string.Equals(
              pair.Value,
              value,
              StringComparison.Ordinal));
}
