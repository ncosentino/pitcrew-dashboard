namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// Verifies capacity-only operations preserve any locally applied
/// static-profile manifest source path via <c>-ProfilePath</c> so that a
/// later capacity change cannot resolve back to the repository's original
/// build/image manifest and silently undo an intervening image rollout.
/// Also verifies fail-closed behaviour when the recorded manifest is
/// incomplete or references a missing/unsafe file.
/// </summary>
public sealed class CapacityProfileResolverManifestPathTests
{
  [Test]
  public async Task Resolve_Preserves_ManifestSourcePath_From_Current_Static_Profile(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          generation: 3,
          maximum: 4,
          cancellationToken);
      var manifestPath = Path.Combine(
          root,
          "image-rollout",
          "manifests",
          $"{Guid.NewGuid():N}.json");
      await CapacityTestData.RewriteStaticProfileWithManifestAsync(
          root,
          manifestPath,
          cancellationToken);
      var options = CapacityTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateCapacityResolver(options);

      var resolution = await resolver.ResolveAsync(
          "default",
          cancellationToken);

      await Assert.That(resolution.Error).IsNull();
      await Assert.That(resolution.Profile).IsNotNull();
      await Assert.That(resolution.Profile!.ManifestSourcePath)
          .IsEqualTo(manifestPath);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task BuildArguments_Inserts_ProfilePath_When_Manifest_Present(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          generation: 3,
          maximum: 4,
          cancellationToken);
      var manifestPath = Path.Combine(
          root,
          "image-rollout",
          "manifests",
          $"{Guid.NewGuid():N}.json");
      await CapacityTestData.RewriteStaticProfileWithManifestAsync(
          root,
          manifestPath,
          cancellationToken);
      var options = CapacityTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateCapacityResolver(options);

      var resolution = await resolver.ResolveAsync(
          "default",
          cancellationToken);

      var args = CapacityCommandExecutor.BuildArguments(
          resolution.Profile!,
          maximum: 4);

      var index = -1;
      for (var i = 0; i < args.Count; i++)
      {
        if (args[i] == "-CapacityOnly")
        {
          index = i;
          break;
        }
      }
      await Assert.That(index).IsNotEqualTo(-1);
      await Assert.That(args[index + 1]).IsEqualTo("-ProfilePath");
      await Assert.That(args[index + 2]).IsEqualTo(manifestPath);
      // The rest of the invocation still contains -Image / -PullImage /
      // -Labels — capacity does not remove the image authority; it just
      // now uses the current manifest source.
      await Assert.That(args.Contains("-Image")).IsTrue();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task BuildArguments_Omits_ProfilePath_When_No_Manifest_Present(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          generation: 3,
          maximum: 4,
          cancellationToken);
      var options = CapacityTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateCapacityResolver(options);

      var resolution = await resolver.ResolveAsync(
          "default",
          cancellationToken);

      await Assert.That(resolution.Profile!.ManifestSourcePath).IsNull();
      var args = CapacityCommandExecutor.BuildArguments(
          resolution.Profile!,
          maximum: 4);
      await Assert.That(args.Contains("-ProfilePath")).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Resolve_Fails_Closed_When_Manifest_Object_Missing_SourcePath(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          generation: 3,
          maximum: 4,
          cancellationToken);
      await CapacityTestData
          .RewriteStaticProfileWithManifestMissingSourcePathAsync(
              root,
              cancellationToken);
      var options = CapacityTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateCapacityResolver(options);

      var resolution = await resolver.ResolveAsync(
          "default",
          cancellationToken);

      await Assert.That(resolution.Profile).IsNull();
      await Assert.That(resolution.Error).Contains("sourcePath");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Resolve_Fails_Closed_When_Manifest_SourcePath_Missing_File(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          generation: 3,
          maximum: 4,
          cancellationToken);
      var manifestPath = Path.Combine(
          root,
          "image-rollout",
          "manifests",
          $"{Guid.NewGuid():N}.json");
      await CapacityTestData.RewriteStaticProfileWithManifestAsync(
          root,
          manifestPath,
          cancellationToken);
      File.Delete(manifestPath);
      var options = CapacityTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateCapacityResolver(options);

      var resolution = await resolver.ResolveAsync(
          "default",
          cancellationToken);

      await Assert.That(resolution.Profile).IsNull();
      await Assert.That(resolution.Error).Contains("invalid");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
