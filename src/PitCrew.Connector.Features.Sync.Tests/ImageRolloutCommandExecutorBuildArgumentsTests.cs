namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// Static <see cref="ImageRolloutCommandExecutor.BuildArguments"/> tests
/// covering every routing shape Setup-Runner supports: repo active/paused,
/// org active/paused, ent active/paused. Every test asserts the exact
/// arguments emitted so no credential, remote path, or candidate
/// registry repository/digest can leak into the invocation. Unsupported
/// routing shapes throw <see cref="InvalidOperationException"/> so
/// ExecuteAsync terminalizes the command as <c>failed</c>/<c>unsupported</c>
/// instead of invoking Setup-Runner with an unsafe argument list.
/// </summary>
public sealed class ImageRolloutCommandExecutorBuildArgumentsTests
{
  private const string ProfileId = "test-runner";
  private const string ManifestPath = "/opt/pitcrew/rollout-state/test.json";

  [Test]
  public async Task Repo_Active_Single_Target_Emits_Scope_And_Single_AddRepos(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "repo",
        Paused: false,
        RepositoryTargets:
        [
          new ImageRolloutRepositoryTarget(
              "https://github.com/example/project",
              4),
        ],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);

    var args = ImageRolloutCommandExecutor.BuildArguments(
        ProfileId,
        ManifestPath,
        routing);

    await Assert.That(args).IsEquivalentTo(
        [
          "-NoProfile",
          "-File",
          "Setup-Runner.ps1",
          "-Profile",
          ProfileId,
          "-ProfilePath",
          ManifestPath,
          "-Scope",
          "repo",
          "-AddRepos",
          "https://github.com/example/project=4",
        ]);
  }

  [Test]
  public async Task Repo_Multiple_Targets_Throws_InvalidOperationException(
      CancellationToken cancellationToken)
  {
    // PowerShell -File binding cannot bind more than one value into a
    // string-array parameter safely: -AddRepos a -AddRepos b exits with
    // "parameter specified more than once", adjacent values bind only
    // the first value, and comma-joined tokens stay as a single string.
    // Protocol v11 therefore refuses multi-target repo routing. The
    // routing projection catches this first with unsupported-topology;
    // BuildArguments asserts defensively as a second gate.
    var routing = new ImageRolloutRoutingState(
        Scope: "repo",
        Paused: false,
        RepositoryTargets:
        [
          new ImageRolloutRepositoryTarget(
              "https://github.com/example/alpha",
              2),
          new ImageRolloutRepositoryTarget(
              "https://github.com/example/beta",
              5),
        ],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);

    var exception = Assert.Throws<InvalidOperationException>(
        () => ImageRolloutCommandExecutor.BuildArguments(
            ProfileId,
            ManifestPath,
            routing));

    await Assert.That(exception.Message)
        .Contains("exactly one repository target");
  }

  [Test]
  public async Task Repo_Fully_Paused_Emits_Pause_Not_AddRepos(
      CancellationToken cancellationToken)
  {
    // Paused single-target repo scope: routing carries exactly one
    // zero-count repository target and Paused=true derived from that
    // target's Workers==0.
    var routing = new ImageRolloutRoutingState(
        Scope: "repo",
        Paused: true,
        RepositoryTargets:
        [
          new ImageRolloutRepositoryTarget(
              "https://github.com/example/project",
              0),
        ],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);

    var args = ImageRolloutCommandExecutor.BuildArguments(
        ProfileId,
        ManifestPath,
        routing);

    await Assert.That(args).IsEquivalentTo(
        [
          "-NoProfile",
          "-File",
          "Setup-Runner.ps1",
          "-Profile",
          ProfileId,
          "-ProfilePath",
          ManifestPath,
          "-Scope",
          "repo",
          "-Pause",
        ]);
    await Assert.That(args.Contains("-AddRepos")).IsFalse();
    await Assert.That(args.Contains("-Replicas")).IsFalse();
  }

  [Test]
  public async Task Repo_Active_Without_Targets_Throws_InvalidOperationException(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "repo",
        Paused: false,
        RepositoryTargets: [],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);

    var exception = Assert.Throws<InvalidOperationException>(
        () => ImageRolloutCommandExecutor.BuildArguments(
            ProfileId,
            ManifestPath,
            routing));

    await Assert.That(exception.Message)
        .Contains("exactly one repository target");
  }

  [Test]
  public async Task Org_Active_Emits_OrgName_And_Replicas(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "org",
        Paused: false,
        RepositoryTargets: [],
        Organization: "acme",
        Enterprise: string.Empty,
        Replicas: 6);

    var args = ImageRolloutCommandExecutor.BuildArguments(
        ProfileId,
        ManifestPath,
        routing);

    await Assert.That(args).IsEquivalentTo(
        [
          "-NoProfile",
          "-File",
          "Setup-Runner.ps1",
          "-Profile",
          ProfileId,
          "-ProfilePath",
          ManifestPath,
          "-Scope",
          "org",
          "-OrgName",
          "acme",
          "-Replicas",
          "6",
        ]);
    await Assert.That(args.Contains("-AddRepos")).IsFalse();
    await Assert.That(args.Contains("-EnterpriseName")).IsFalse();
    await Assert.That(args.Contains("-Pause")).IsFalse();
  }

  [Test]
  public async Task Org_Paused_Emits_OrgName_And_Pause(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "org",
        Paused: true,
        RepositoryTargets: [],
        Organization: "acme",
        Enterprise: string.Empty,
        Replicas: null);

    var args = ImageRolloutCommandExecutor.BuildArguments(
        ProfileId,
        ManifestPath,
        routing);

    await Assert.That(args).IsEquivalentTo(
        [
          "-NoProfile",
          "-File",
          "Setup-Runner.ps1",
          "-Profile",
          ProfileId,
          "-ProfilePath",
          ManifestPath,
          "-Scope",
          "org",
          "-OrgName",
          "acme",
          "-Pause",
        ]);
    await Assert.That(args.Contains("-Replicas")).IsFalse();
  }

  [Test]
  public async Task Org_Active_Without_Identity_Throws_InvalidOperationException(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "org",
        Paused: false,
        RepositoryTargets: [],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: 6);

    var exception = Assert.Throws<InvalidOperationException>(
        () => ImageRolloutCommandExecutor.BuildArguments(
            ProfileId,
            ManifestPath,
            routing));

    await Assert.That(exception.Message).Contains("organization");
  }

  [Test]
  public async Task Org_Active_Without_Replicas_Throws_InvalidOperationException(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "org",
        Paused: false,
        RepositoryTargets: [],
        Organization: "acme",
        Enterprise: string.Empty,
        Replicas: null);

    var exception = Assert.Throws<InvalidOperationException>(
        () => ImageRolloutCommandExecutor.BuildArguments(
            ProfileId,
            ManifestPath,
            routing));

    await Assert.That(exception.Message).Contains("replicas");
  }

  [Test]
  public async Task Ent_Active_Emits_EnterpriseName_And_Replicas(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "ent",
        Paused: false,
        RepositoryTargets: [],
        Organization: string.Empty,
        Enterprise: "example-ent",
        Replicas: 10);

    var args = ImageRolloutCommandExecutor.BuildArguments(
        ProfileId,
        ManifestPath,
        routing);

    await Assert.That(args).IsEquivalentTo(
        [
          "-NoProfile",
          "-File",
          "Setup-Runner.ps1",
          "-Profile",
          ProfileId,
          "-ProfilePath",
          ManifestPath,
          "-Scope",
          "ent",
          "-EnterpriseName",
          "example-ent",
          "-Replicas",
          "10",
        ]);
    await Assert.That(args.Contains("-OrgName")).IsFalse();
  }

  [Test]
  public async Task Ent_Paused_Emits_EnterpriseName_And_Pause(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "ent",
        Paused: true,
        RepositoryTargets: [],
        Organization: string.Empty,
        Enterprise: "example-ent",
        Replicas: null);

    var args = ImageRolloutCommandExecutor.BuildArguments(
        ProfileId,
        ManifestPath,
        routing);

    await Assert.That(args).IsEquivalentTo(
        [
          "-NoProfile",
          "-File",
          "Setup-Runner.ps1",
          "-Profile",
          ProfileId,
          "-ProfilePath",
          ManifestPath,
          "-Scope",
          "ent",
          "-EnterpriseName",
          "example-ent",
          "-Pause",
        ]);
    await Assert.That(args.Contains("-Replicas")).IsFalse();
  }

  [Test]
  public async Task Ent_Active_Without_Identity_Throws_InvalidOperationException(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "ent",
        Paused: false,
        RepositoryTargets: [],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: 10);

    var exception = Assert.Throws<InvalidOperationException>(
        () => ImageRolloutCommandExecutor.BuildArguments(
            ProfileId,
            ManifestPath,
            routing));

    await Assert.That(exception.Message).Contains("enterprise");
  }

  [Test]
  public async Task Unsupported_Scope_Throws_InvalidOperationException(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "invalid",
        Paused: false,
        RepositoryTargets: [],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);

    var exception = Assert.Throws<InvalidOperationException>(
        () => ImageRolloutCommandExecutor.BuildArguments(
            ProfileId,
            ManifestPath,
            routing));

    await Assert.That(exception.Message).Contains("Unsupported");
  }

  [Test]
  public async Task No_Argument_Contains_Registry_Repository_Or_Digest(
      CancellationToken cancellationToken)
  {
    // Every shape MUST refuse to leak candidate registry authority through
    // the process invocation; server-provided target digest/repo stay only
    // inside the local manifest file, never in the argument list.
    var routing = new ImageRolloutRoutingState(
        Scope: "repo",
        Paused: false,
        RepositoryTargets:
        [
          new ImageRolloutRepositoryTarget(
              "https://github.com/example/project",
              1),
        ],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);

    var args = ImageRolloutCommandExecutor.BuildArguments(
        ProfileId,
        ManifestPath,
        routing);

    foreach (var arg in args)
    {
      await Assert.That(
              arg.Contains(
                  "ghcr.io/example/candidate",
                  StringComparison.OrdinalIgnoreCase))
          .IsFalse();
      await Assert.That(
              arg.Contains(
                  "sha256:aaaaaaaaaaaa",
                  StringComparison.OrdinalIgnoreCase))
          .IsFalse();
      await Assert.That(
              arg.Contains(
                  "enrollment",
                  StringComparison.OrdinalIgnoreCase))
          .IsFalse();
    }
  }

  // -NamePrefix is a schema-rejected manifest property (runner-profile.
  // schema.json is additionalProperties=false and has no namePrefix), so
  // the connector preserves the applied configuration.namePrefix on the
  // Setup-Runner CLI instead. Verify it appears exactly once, in the
  // expected position (right after -ProfilePath and before the routing
  // switch), when a non-empty value is supplied.
  [Test]
  public async Task NamePrefix_Emitted_Between_ProfilePath_And_Routing(
      CancellationToken cancellationToken)
  {
    var routing = new ImageRolloutRoutingState(
        Scope: "repo",
        Paused: false,
        RepositoryTargets:
        [
          new ImageRolloutRepositoryTarget(
              "https://github.com/example/project",
              4),
        ],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);

    var args = ImageRolloutCommandExecutor.BuildArguments(
        ProfileId,
        ManifestPath,
        namePrefix: "runner",
        routing);

    await Assert.That(args).IsEquivalentTo(
        [
          "-NoProfile",
          "-File",
          "Setup-Runner.ps1",
          "-Profile",
          ProfileId,
          "-ProfilePath",
          ManifestPath,
          "-NamePrefix",
          "runner",
          "-Scope",
          "repo",
          "-AddRepos",
          "https://github.com/example/project=4",
        ]);
    // Sanity: only one NamePrefix pair, positioned right after
    // -ProfilePath so it applies independently of routing.
    await Assert.That(args.Where(a => a == "-NamePrefix").Count()).IsEqualTo(1);
    await Assert.That(args[7]).IsEqualTo("-NamePrefix");
    await Assert.That(args[8]).IsEqualTo("runner");
  }

  [Test]
  public async Task NamePrefix_Omitted_When_Configuration_Value_Is_Null_Or_Whitespace(
      CancellationToken cancellationToken)
  {
    // Paused single-target repo shape: one zero-count entry, Paused=true.
    var routing = new ImageRolloutRoutingState(
        Scope: "repo",
        Paused: true,
        RepositoryTargets:
        [
          new ImageRolloutRepositoryTarget(
              "https://github.com/example/project",
              0),
        ],
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);

    foreach (var value in new[] { null, string.Empty, "   " })
    {
      var args = ImageRolloutCommandExecutor.BuildArguments(
          ProfileId,
          ManifestPath,
          namePrefix: value,
          routing);
      await Assert.That(args.Contains("-NamePrefix")).IsFalse();
    }
  }
}
