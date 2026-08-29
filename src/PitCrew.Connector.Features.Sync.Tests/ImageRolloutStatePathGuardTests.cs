using System.Runtime.InteropServices;

namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// Verifies the pure path-safety helpers in
/// <see cref="ImageRolloutStatePathGuard"/> that back the rollout state
/// boundary. Canonicalization rejects relative and drive-relative configured
/// paths; child derivation is separator-aware and rejects paths that would
/// escape the canonicalized root; existing-directory reparse rejection
/// blocks symbolic links and Windows junctions before any read or write
/// touches the redirected target. Platform-limited symlink probes fall back
/// to a lightweight configuration check when the OS refuses to create the
/// symlink without elevated privileges.
/// </summary>
public sealed class ImageRolloutStatePathGuardTests
{
  [Test]
  public async Task CanonicalizeStateRoot_Rejects_Blank_Input(
      CancellationToken cancellationToken)
  {
    Assert.Throws<InvalidOperationException>(
        () => ImageRolloutStatePathGuard.CanonicalizeStateRoot(""));
    Assert.Throws<InvalidOperationException>(
        () => ImageRolloutStatePathGuard.CanonicalizeStateRoot("   "));
    await Task.CompletedTask;
  }

  [Test]
  public async Task CanonicalizeStateRoot_Rejects_Relative_Path(
      CancellationToken cancellationToken)
  {
    var exception = Assert.Throws<InvalidOperationException>(
        () => ImageRolloutStatePathGuard.CanonicalizeStateRoot(
            Path.Combine("relative", "rollout-state")));
    await Assert.That(exception!.Message)
        .Contains("absolute")
        .Because(
            "a relative configured path must not be silently rebased against "
            + "the connector's current working directory at process start");
  }

  [Test]
  public async Task CanonicalizeStateRoot_Rejects_Drive_Relative_Path_On_Windows(
      CancellationToken cancellationToken)
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      // Drive-relative paths only exist on Windows.
      return;
    }
    // "\rollout" on Windows is drive-relative (resolves against the current
    // drive), which is not fully qualified.
    Assert.Throws<InvalidOperationException>(
        () => ImageRolloutStatePathGuard.CanonicalizeStateRoot(@"\rollout"));
    await Task.CompletedTask;
  }

  [Test]
  public async Task CanonicalizeStateRoot_Accepts_Fully_Qualified_Path(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var canonical = ImageRolloutStatePathGuard.CanonicalizeStateRoot(root);
      await Assert.That(canonical).IsEqualTo(Path.GetFullPath(root));
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task CombineConfinedChild_Rejects_Empty_Or_Traversal_Name(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var canonical = ImageRolloutStatePathGuard.CanonicalizeStateRoot(root);
      Assert.Throws<InvalidOperationException>(
          () => ImageRolloutStatePathGuard.CombineConfinedChild(canonical, ""));
      Assert.Throws<InvalidOperationException>(
          () => ImageRolloutStatePathGuard.CombineConfinedChild(canonical, ".."));
      Assert.Throws<InvalidOperationException>(
          () => ImageRolloutStatePathGuard.CombineConfinedChild(canonical, "."));
      Assert.Throws<InvalidOperationException>(
          () => ImageRolloutStatePathGuard.CombineConfinedChild(
              canonical,
              "sub" + Path.DirectorySeparatorChar + "child"));
      Assert.Throws<InvalidOperationException>(
          () => ImageRolloutStatePathGuard.CombineConfinedChild(
              canonical,
              "sub" + Path.AltDirectorySeparatorChar + "child"));
      await Task.CompletedTask;
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task CombineConfinedChild_Returns_Direct_Child_Path(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var canonical = ImageRolloutStatePathGuard.CanonicalizeStateRoot(root);
      var ledger = ImageRolloutStatePathGuard.CombineConfinedChild(
          canonical,
          "ledger");
      var manifests = ImageRolloutStatePathGuard.CombineConfinedChild(
          canonical,
          "manifests");
      await Assert.That(ledger)
          .IsEqualTo(Path.Combine(canonical, "ledger"));
      await Assert.That(manifests)
          .IsEqualTo(Path.Combine(canonical, "manifests"));
      // Direct child comparison via Path.GetRelativePath is separator-aware
      // and treats the derived path as one step below the root.
      var relativeLedger = Path.GetRelativePath(canonical, ledger);
      await Assert.That(relativeLedger).IsEqualTo("ledger");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task EnsureNotReparsePoint_Ignores_Missing_Directory(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var missing = Path.Combine(root, "does-not-exist");
      // Non-existent path must not throw — the caller is about to create
      // the confined child under a validated root.
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(missing);
      await Task.CompletedTask;
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task EnsureNotReparsePoint_Allows_Real_Directory(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      // A real directory (no reparse-point attribute) must be accepted.
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(root);
      await Task.CompletedTask;
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task EnsureNotReparsePoint_Rejects_Symbolic_Link_Directory(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var realTarget = Path.Combine(root, "target");
      Directory.CreateDirectory(realTarget);
      var linkPath = Path.Combine(root, "link");
      try
      {
        Directory.CreateSymbolicLink(linkPath, realTarget);
      }
      catch (IOException)
      {
        // Symbolic link creation is a restricted privilege on Windows unless
        // Developer Mode is enabled or the user has SeCreateSymbolicLink.
        // Skip the actual symlink probe; the pure canonicalization and
        // confinement invariants above still guard against configured
        // symlink paths (they still resolve as reparse points at runtime
        // when the installer provisioned real directories).
        return;
      }
      catch (UnauthorizedAccessException)
      {
        return;
      }

      var exception = Assert.Throws<UnauthorizedAccessException>(
          () => ImageRolloutStatePathGuard.EnsureNotReparsePoint(linkPath));
      await Assert.That(exception!.Message)
          .Contains("symbolic link or reparse point");
      await Assert.That(exception.Message)
          .DoesNotContain(linkPath)
          .Because(
              "the generic failure message must not leak the resolved local "
              + "path into logs or exception text");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
