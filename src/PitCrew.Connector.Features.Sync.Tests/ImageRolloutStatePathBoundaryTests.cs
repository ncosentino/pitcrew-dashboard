namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// End-to-end proof that both the ledger and the manifest builder route
/// their state-directory derivation through the shared
/// <see cref="ImageRolloutStatePathGuard"/>: a relative
/// <c>ImageRolloutStatePath</c> and an existing state-root reparse point are
/// rejected before any file is opened. The pure-helper tests in
/// <see cref="ImageRolloutStatePathGuardTests"/> cover the confinement
/// invariants directly; the tests here prove the wiring at each writer
/// entry point.
/// </summary>
public sealed class ImageRolloutStatePathBoundaryTests
{
  // The reparse-point rejection message is a fixed literal that never
  // embeds the resolved local path. Asserting the exact literal doubles as
  // a path-leak regression guard.
  private const string ExpectedReparsePointMessage =
      "The configured rollout state directory or one of its immediate "
      + "children is a symbolic link or reparse point; the connector "
      + "refuses to follow it. Reinstall the connector with "
      + "-EnableImageRollout so the installer can provision a real "
      + "protected directory.";

  [Test]
  public async Task ImageRolloutLedger_Rejects_Relative_State_Path(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      // Overwrite the pre-provisioned absolute path with a relative value
      // so the guard has to reject it during the very first write.
      options.ImageRolloutStatePath = Path.Combine(
          "relative-image-rollout",
          "state");
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var entry = NewEntry(Guid.NewGuid());

      // ImageRolloutStatePathGuard.CanonicalizeStateRoot fires on the first
      // directory derivation — RecordStartedAsync must not silently rebase
      // against the connector's current working directory.
      await Assert.That(async () =>
              await ledger.RecordStartedAsync(entry, cancellationToken))
          .Throws<InvalidOperationException>()
          .WithMessageContaining("absolute");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ImageRolloutManifestBuilder_Rejects_Relative_State_Path(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      options.ImageRolloutStatePath = Path.Combine(
          "relative-image-rollout",
          "state");
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = await File.ReadAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "static-profile.json"),
          cancellationToken);

      var exception = Assert.Throws<InvalidOperationException>(
          () => builder.BuildAndWriteManifest(
              Guid.NewGuid(),
              ImageRolloutTestData.DefaultProfileId,
              staticProfileJson,
              "ghcr.io/example/target",
              "sha256:" + new string('a', 64)));
      await Assert.That(exception!.Message).Contains("absolute");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ImageRolloutLedger_Rejects_State_Root_Symbolic_Link(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      // Delete the pre-provisioned real directory and replace it with a
      // symbolic link pointing to a real directory elsewhere. If the OS
      // refuses to create the link (Windows without Developer Mode), fall
      // back to the pure-helper coverage in
      // ImageRolloutStatePathGuardTests.
      Directory.Delete(options.ImageRolloutStatePath, recursive: true);
      var realTarget = Path.Combine(root, "linked-target");
      Directory.CreateDirectory(realTarget);
      try
      {
        Directory.CreateSymbolicLink(
            options.ImageRolloutStatePath,
            realTarget);
      }
      catch (IOException)
      {
        return;
      }
      catch (UnauthorizedAccessException)
      {
        return;
      }

      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var entry = NewEntry(Guid.NewGuid());

      // The message is a fixed literal that never embeds the resolved local
      // path — asserting the exact literal here doubles as a path-leak
      // regression guard.
      await Assert.That(async () =>
              await ledger.RecordStartedAsync(entry, cancellationToken))
          .Throws<UnauthorizedAccessException>()
          .WithMessage(ExpectedReparsePointMessage);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        try
        {
          Directory.Delete(root, true);
        }
        catch (IOException)
        {
          // Deleting a directory that contains a symbolic link can fail if
          // Directory.Delete tries to recurse into the link target; the
          // temp directory will be swept by the runner regardless.
        }
      }
    }
  }

  [Test]
  public async Task ImageRolloutManifestBuilder_Rejects_State_Root_Symbolic_Link(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      Directory.Delete(options.ImageRolloutStatePath, recursive: true);
      var realTarget = Path.Combine(root, "linked-target");
      Directory.CreateDirectory(realTarget);
      try
      {
        Directory.CreateSymbolicLink(
            options.ImageRolloutStatePath,
            realTarget);
      }
      catch (IOException)
      {
        return;
      }
      catch (UnauthorizedAccessException)
      {
        return;
      }
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = await File.ReadAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "static-profile.json"),
          cancellationToken);

      var exception = Assert.Throws<UnauthorizedAccessException>(
          () => builder.BuildAndWriteManifest(
              Guid.NewGuid(),
              ImageRolloutTestData.DefaultProfileId,
              staticProfileJson,
              "ghcr.io/example/target",
              "sha256:" + new string('a', 64)));
      await Assert.That(exception!.Message)
          .IsEqualTo(ExpectedReparsePointMessage);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        try
        {
          Directory.Delete(root, true);
        }
        catch (IOException)
        {
        }
      }
    }
  }

  private static ImageRolloutLedgerEntry NewEntry(Guid commandId) =>
      new(
          CommandId: commandId,
          ProfileId: "default",
          CandidateId: Guid.NewGuid(),
          RecipeId: "test-recipe",
          TargetDigest: "sha256:" + new string('a', 64),
          TargetPlatform: "linux/amd64",
          RegistryRepository: "ghcr.io/example/runner",
          LocalManifestPath: Path.Combine("state", $"{commandId:N}.json"),
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
          StartedAt: ImageRolloutTestData.Now,
          Phase: ImageRolloutLedgerPhases.Started,
          Status: null,
          FailureCategory: null,
          Message: null,
          TargetWorkerRevision: null,
          ManagerConvergenceStatus: null,
          CurrentWorkers: null,
          StaleWorkers: null,
          LastError: null,
          CompletedAt: null);
}
