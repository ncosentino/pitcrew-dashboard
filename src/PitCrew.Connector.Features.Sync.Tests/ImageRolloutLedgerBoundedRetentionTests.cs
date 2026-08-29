namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// Verifies the local image-rollout ledger enforces bounded manifest
/// retention while keeping every ledger entry (including old terminals) on
/// disk as durable at-most-once command-id tombstones. Started attempts and
/// the currently applied static-profile manifest source path are always
/// protected in the referenced set; only a bounded newest set of
/// succeeded/indeterminate terminal entries additionally stay referenced.
/// Older terminal manifests become eligible for the orphan sweep, but the
/// ledger records themselves are never pruned so a redelivered command id
/// cannot execute a second time.
/// </summary>
public sealed class ImageRolloutLedgerBoundedRetentionTests
{
  [Test]
  public async Task EnumerateReferencedManifestPaths_Retains_Newest_Terminal_Entries_Within_Cap(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var manifestPaths = new List<string>();
      var start = ImageRolloutTestData.Now;
      const int totalTerminals = 5;
      for (var i = 0; i < totalTerminals; i++)
      {
        var manifestPath = Path.Combine(
            root,
            "rollout-state",
            $"succeeded-{i}.json");
        manifestPaths.Add(manifestPath);
        var commandId = Guid.NewGuid();
        await ledger.RecordStartedAsync(
            NewEntry(
                commandId: commandId,
                manifestPath: manifestPath,
                startedAt: start.AddSeconds(i)),
            cancellationToken);
        await ledger.RecordTerminalAsync(
            NewEntry(
                commandId: commandId,
                manifestPath: manifestPath,
                startedAt: start.AddSeconds(i),
                phase: ImageRolloutLedgerPhases.Terminal,
                status: "succeeded",
                completedAt: start.AddSeconds(i + 100)),
            cancellationToken);
      }

      var referenced = ledger.EnumerateReferencedManifestPaths(
          extraProtectedPaths: null,
          terminalRetentionCap: 3);

      // Newest 3 (indices 4, 3, 2 by CompletedAt) are protected; older (0, 1)
      // are not in the reference set, so PruneOrphans could remove them.
      await Assert.That(referenced.Count).IsEqualTo(3);
      await Assert.That(referenced.Contains(manifestPaths[4])).IsTrue();
      await Assert.That(referenced.Contains(manifestPaths[3])).IsTrue();
      await Assert.That(referenced.Contains(manifestPaths[2])).IsTrue();
      await Assert.That(referenced.Contains(manifestPaths[1])).IsFalse();
      await Assert.That(referenced.Contains(manifestPaths[0])).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task EnumerateReferencedManifestPaths_Always_Protects_Started_Entries_Above_Cap(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var manifestPaths = new List<string>();
      var start = ImageRolloutTestData.Now;
      // Beyond the cap: every started entry is unconditionally protected.
      const int totalStarted = 10;
      for (var i = 0; i < totalStarted; i++)
      {
        var manifestPath = Path.Combine(
            root,
            "rollout-state",
            $"started-{i}.json");
        manifestPaths.Add(manifestPath);
        await ledger.RecordStartedAsync(
            NewEntry(
                commandId: Guid.NewGuid(),
                manifestPath: manifestPath,
                startedAt: start.AddSeconds(i)),
            cancellationToken);
      }

      var referenced = ledger.EnumerateReferencedManifestPaths(
          extraProtectedPaths: null,
          terminalRetentionCap: 3);

      foreach (var manifestPath in manifestPaths)
      {
        await Assert.That(referenced.Contains(manifestPath))
            .IsTrue()
            .Because(
                "started entries are unconditionally protected regardless "
                + "of the terminal-retention cap");
      }
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task EnumerateReferencedManifestPaths_Protects_Extra_Paths_Even_When_Not_In_Ledger(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var currentManifest = Path.Combine(
          root,
          "rollout-state",
          "current-static.json");

      var referenced = ledger.EnumerateReferencedManifestPaths(
          extraProtectedPaths: [currentManifest],
          terminalRetentionCap: 8);

      // The current static-profile manifest is protected even when it is
      // not any ledger entry's LocalManifestPath (i.e. after every prior
      // ledger entry has been prune-eligible).
      await Assert.That(referenced.Contains(currentManifest)).IsTrue();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Terminal_Ledger_Entries_Persist_Beyond_Manifest_Retention_Cap(
      CancellationToken cancellationToken)
  {
    // At-most-once command-id semantics require every terminal ledger entry
    // to remain readable indefinitely; only the generated manifest files are
    // bounded via the referenced-path filter above. This test proves that a
    // ledger with far more terminal records than the manifest retention cap
    // exposes only the newest cap-worth in the referenced set (so the orphan
    // sweep bounds manifests) while FindAsync still returns every terminal
    // entry (so a redelivered command id cannot re-execute).
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var start = ImageRolloutTestData.Now;
      const int totalTerminals = 12;
      const int retentionCap = 3;
      var commandIds = new List<Guid>();
      var manifestPaths = new List<string>();
      for (var i = 0; i < totalTerminals; i++)
      {
        var commandId = Guid.NewGuid();
        commandIds.Add(commandId);
        var manifestPath = Path.Combine(
            root,
            "rollout-state",
            $"terminal-{i}.json");
        manifestPaths.Add(manifestPath);
        await ledger.RecordStartedAsync(
            NewEntry(
                commandId: commandId,
                manifestPath: manifestPath,
                startedAt: start.AddSeconds(i)),
            cancellationToken);
        await ledger.RecordTerminalAsync(
            NewEntry(
                commandId: commandId,
                manifestPath: manifestPath,
                startedAt: start.AddSeconds(i),
                phase: ImageRolloutLedgerPhases.Terminal,
                status: "succeeded",
                completedAt: start.AddSeconds(i + 500)),
            cancellationToken);
      }

      var referenced = ledger.EnumerateReferencedManifestPaths(
          extraProtectedPaths: null,
          terminalRetentionCap: retentionCap);

      // Bounded manifest reference set: only the newest 3 are protected.
      await Assert.That(referenced.Count).IsEqualTo(retentionCap);
      await Assert.That(referenced.Contains(manifestPaths[totalTerminals - 1]))
          .IsTrue();
      await Assert.That(referenced.Contains(manifestPaths[0])).IsFalse();

      // Every terminal ledger entry — including entries older than the
      // manifest retention cap — must remain readable so a redelivered
      // command id resolves to its recorded terminal outcome and cannot
      // trigger a second execution.
      for (var i = 0; i < totalTerminals; i++)
      {
        var entry = await ledger.FindAsync(commandIds[i], cancellationToken);
        await Assert.That(entry)
            .IsNotNull()
            .Because(
                "terminal ledger entries are durable at-most-once "
                + "tombstones and must never be pruned");
        await Assert.That(entry!.Phase)
            .IsEqualTo(ImageRolloutLedgerPhases.Terminal);
        await Assert.That(entry.Status).IsEqualTo("succeeded");
      }
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static ImageRolloutLedgerEntry NewEntry(
      Guid commandId,
      string manifestPath,
      DateTimeOffset startedAt,
      string phase = ImageRolloutLedgerPhases.Started,
      string? status = null,
      DateTimeOffset? completedAt = null) =>
      new(
          CommandId: commandId,
          ProfileId: "default",
          CandidateId: Guid.NewGuid(),
          RecipeId: "test-recipe",
          TargetDigest: "sha256:" + new string('a', 64),
          TargetPlatform: "linux/amd64",
          RegistryRepository: "ghcr.io/example/runner",
          LocalManifestPath: manifestPath,
          ExpectedCurrentImageReference: null,
          ExpectedCurrentImageDigest: null,
          ExpectedCurrentLocalImageId: null,
          ExpectedCurrentWorkerRevision: null,
          ExpectedStaticFingerprint: new string('f', 64),
          ExpectedPreservedConfigurationFingerprint: new string('e', 64),
          ExpectedRoutingFingerprint: new string('d', 64),
          ExpectedDesiredGeneration: 7,
          ExpectedDesiredStateHash: new string('c', 64),
          ResolvedPreOperationRevision: null,
          ManifestSourcePath: null,
          StartedAt: startedAt,
          Phase: phase,
          Status: status,
          FailureCategory: null,
          Message: null,
          TargetWorkerRevision: null,
          ManagerConvergenceStatus: null,
          CurrentWorkers: null,
          StaleWorkers: null,
          LastError: null,
          CompletedAt: completedAt);
}
