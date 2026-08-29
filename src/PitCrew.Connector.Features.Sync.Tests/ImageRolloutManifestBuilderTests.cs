using System.Text.Json;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ImageRolloutManifestBuilderTests
{
  private const string NewRepository = "ghcr.io/example/runner-v2";
  private const string NewDigest =
      "sha256:9999999999999999999999999999999999999999999999999999999999999999";

  [Test]
  public async Task BuildAndWriteManifest_Rewrites_Only_Image_Digest_And_PullImage(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = await File.ReadAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "static-profile.json"),
          cancellationToken);

      var commandId = Guid.NewGuid();
      var manifestPath = builder.BuildAndWriteManifest(
          commandId,
          ImageRolloutTestData.DefaultProfileId,
          staticProfileJson,
          NewRepository,
          NewDigest);

      await Assert.That(File.Exists(manifestPath)).IsTrue();
      await Assert.That(Path.GetFileName(manifestPath))
          .IsEqualTo($"{commandId:N}.json");
      var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
      using var manifest = JsonDocument.Parse(manifestJson);
      var root2 = manifest.RootElement;
      // Removed image authority: no build, and no imageDigest (upstream
      // runner-profile.schema.json is additionalProperties=false and does
      // not allow imageDigest).
      await Assert.That(root2.TryGetProperty("build", out _)).IsFalse();
      await Assert.That(root2.TryGetProperty("imageDigest", out _)).IsFalse();
      await Assert.That(root2.GetProperty("image").GetString())
          .IsEqualTo($"{NewRepository}@{NewDigest}");
      await Assert.That(root2.GetProperty("pullImage").GetBoolean()).IsTrue();
      // Preserved from the upstream manifest.document (runner-profile
      // schema properties): name/description/labels/replicas/etc.
      await Assert.That(root2.GetProperty("name").GetString())
          .IsEqualTo(ImageRolloutTestData.DefaultProfileId);
      await Assert.That(root2.GetProperty("description").GetString())
          .IsEqualTo("Local test runner profile");
      await Assert.That(root2.GetProperty("replicas").GetInt32())
          .IsEqualTo(ImageRolloutTestData.DefaultRepositoryWorkers);
      // serviceNetwork must be an object with a "source" string (schema
      // rejects a bare string; the reconstruction always normalizes to
      // the object shape).
      var serviceNetwork = root2.GetProperty("serviceNetwork");
      await Assert.That(serviceNetwork.ValueKind)
          .IsEqualTo(JsonValueKind.Object);
      await Assert.That(
              serviceNetwork.GetProperty("source").GetString())
          .IsEqualTo("runner-net");
      var labels = root2.GetProperty("labels");
      await Assert.That(labels.GetArrayLength()).IsEqualTo(2);
      // Manifest MUST NOT include additionalProperties-rejected fields
      // like profile / managerContractVersion / scope / namePrefix.
      await Assert.That(root2.TryGetProperty("profile", out _)).IsFalse();
      await Assert.That(root2.TryGetProperty("managerContractVersion", out _))
          .IsFalse();
      await Assert.That(root2.TryGetProperty("scope", out _)).IsFalse();
      await Assert.That(root2.TryGetProperty("namePrefix", out _)).IsFalse();
      await ImageRolloutTestData
          .AssertProducedManifestConformsToSchemaAsync(
              root2,
              cancellationToken);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task BuildAndWriteManifest_Rejects_Static_State_Without_Manifest_Document(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      // Static profile without a manifest.document (upstream shape: root.
      // manifest may be null and configuration alone does not carry a
      // manifest document). Fail closed.
      var staticProfileJson = JsonSerializer.Serialize(new
      {
        schemaVersion = 1,
        fingerprint = ImageRolloutTestData.StaticFingerprint,
        manifest = (object?)null,
        configuration = new
        {
          managerContractVersion = 17,
          profile = ImageRolloutTestData.DefaultProfileId,
        },
      });

      var exception = Assert.Throws<InvalidDataException>(
          () => builder.BuildAndWriteManifest(
              Guid.NewGuid(),
              ImageRolloutTestData.DefaultProfileId,
              staticProfileJson,
              NewRepository,
              NewDigest));

      await Assert.That(exception.Message)
          .Contains("manifest.document");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task PruneOrphans_Keeps_Referenced_And_Bounded_Unreferenced_Manifests(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      options.ImageRolloutRetainedManifests = 2;
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = await File.ReadAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "static-profile.json"),
          cancellationToken);

      var oldest = builder.BuildAndWriteManifest(
          Guid.NewGuid(),
          ImageRolloutTestData.DefaultProfileId,
          staticProfileJson,
          NewRepository,
          NewDigest);
      await Task.Delay(20, cancellationToken);
      var referenced = builder.BuildAndWriteManifest(
          Guid.NewGuid(),
          ImageRolloutTestData.DefaultProfileId,
          staticProfileJson,
          NewRepository,
          NewDigest);
      await Task.Delay(20, cancellationToken);
      var newer1 = builder.BuildAndWriteManifest(
          Guid.NewGuid(),
          ImageRolloutTestData.DefaultProfileId,
          staticProfileJson,
          NewRepository,
          NewDigest);
      await Task.Delay(20, cancellationToken);
      var newer2 = builder.BuildAndWriteManifest(
          Guid.NewGuid(),
          ImageRolloutTestData.DefaultProfileId,
          staticProfileJson,
          NewRepository,
          NewDigest);

      builder.PruneOrphans(
          new HashSet<string>(StringComparer.OrdinalIgnoreCase)
          {
            referenced,
          });

      await Assert.That(File.Exists(referenced)).IsTrue();
      await Assert.That(File.Exists(newer1)).IsTrue();
      await Assert.That(File.Exists(newer2)).IsTrue();
      await Assert.That(File.Exists(oldest)).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 7: the manifest builder fails closed rather than silently
  // creating an insecure ancestor path when the installer has not
  // provisioned the ImageRolloutStatePath root.
  [Test]
  public async Task BuildAndWriteManifest_Fails_Closed_When_State_Root_Does_Not_Exist(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      // Simulate an operator who edited configuration to point at an
      // unprovisioned state root rather than reinstalling.
      var missingRoot = Path.Combine(root, "does-not-exist-image-rollout");
      options.ImageRolloutStatePath = missingRoot;
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
              NewRepository,
              NewDigest));

      await Assert.That(exception.Message)
          .Contains(missingRoot);
      await Assert.That(Directory.Exists(missingRoot)).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Reconstruction correctness: manifest.document is consulted only for
  // description and replicas (and hostAdmission full policy after
  // namespace match). Every other schema-allowed field is projected from
  // the applied configuration. Setting stale values for label/verification
  // fields in manifest.document must not leak through because the builder
  // never reads them from the document; description/replicas overrides
  // must be preserved verbatim.
  [Test]
  public async Task
      BuildAndWriteManifest_Uses_Applied_Configuration_When_Manifest_Document_Differs(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken,
          divergentManifestDocument: new DivergentManifestDocumentValues(
              Labels: new object[] { "stale-source-only" },
              Description: "Stale source description",
              Replicas: 7,
              VerificationCommands: new object[] { "stale-verify" }));
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = await File.ReadAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "static-profile.json"),
          cancellationToken);

      var manifestPath = builder.BuildAndWriteManifest(
          Guid.NewGuid(),
          ImageRolloutTestData.DefaultProfileId,
          staticProfileJson,
          NewRepository,
          NewDigest);

      var manifestJson = await File.ReadAllTextAsync(
          manifestPath,
          cancellationToken);
      using var manifest = JsonDocument.Parse(manifestJson);
      var manifestRoot = manifest.RootElement;

      // Configuration wins for labels: the stale doc-only label is
      // discarded and the applied configuration labels are used.
      var labels = manifestRoot.GetProperty("labels");
      await Assert.That(labels.GetArrayLength()).IsEqualTo(2);
      await Assert.That(labels[0].GetString()).IsEqualTo("general-purpose");
      await Assert.That(labels[1].GetString()).IsEqualTo("linux-amd64");

      // Configuration wins for verificationCommands: the stale doc-only
      // verify command is discarded and the applied commands are used.
      var verificationCommands =
          manifestRoot.GetProperty("verificationCommands");
      await Assert.That(verificationCommands.GetArrayLength()).IsEqualTo(1);
      await Assert.That(verificationCommands[0].GetString())
          .IsEqualTo("which docker");

      // description / replicas are the only fields we intentionally
      // preserve from manifest.document (they are not in configuration
      // and remain a harmless required default in the reconstructed
      // manifest).
      await Assert.That(manifestRoot.GetProperty("description").GetString())
          .IsEqualTo("Stale source description");
      await Assert.That(manifestRoot.GetProperty("replicas").GetInt32())
          .IsEqualTo(7);
      // namePrefix is a CLI-only property; it must not leak into the
      // manifest even though it lives in configuration.
      await Assert.That(manifestRoot.TryGetProperty("namePrefix", out _))
          .IsFalse();
      await ImageRolloutTestData
          .AssertProducedManifestConformsToSchemaAsync(
              manifestRoot,
              cancellationToken);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // hostAdmission full policy is only in manifest.document; configuration
  // carries only its namespace. Preserve the document policy after
  // proving namespaces match.
  [Test]
  public async Task
      BuildAndWriteManifest_Preserves_Manifest_Document_HostAdmission_When_Namespace_Matches(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      // WriteHealthyStateAsync now writes matching hostAdmission.namespace
      // on both configuration and manifest.document.
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = await File.ReadAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "static-profile.json"),
          cancellationToken);

      var manifestPath = builder.BuildAndWriteManifest(
          Guid.NewGuid(),
          ImageRolloutTestData.DefaultProfileId,
          staticProfileJson,
          NewRepository,
          NewDigest);

      var manifestJson = await File.ReadAllTextAsync(
          manifestPath,
          cancellationToken);
      using var manifest = JsonDocument.Parse(manifestJson);
      var hostAdmission =
          manifest.RootElement.GetProperty("hostAdmission");
      await Assert.That(hostAdmission.ValueKind)
          .IsEqualTo(JsonValueKind.Object);
      // Full policy comes from manifest.document (upstream schema: no
      // "identity"; capacityUnits/safetyMarginUnits/workerCostUnits/
      // reservationUnits/borrowable). Configuration only carries the
      // namespace; the full policy is preserved verbatim from the doc.
      await Assert.That(hostAdmission.GetProperty("capacityUnits").GetInt32())
          .IsEqualTo(128);
      await Assert.That(
              hostAdmission.GetProperty("safetyMarginUnits").GetInt32())
          .IsEqualTo(16);
      await Assert.That(
              hostAdmission.GetProperty("workerCostUnits").GetInt32())
          .IsEqualTo(1);
      await Assert.That(
              hostAdmission.GetProperty("reservationUnits").GetInt32())
          .IsEqualTo(4);
      await Assert.That(hostAdmission.GetProperty("borrowable").GetBoolean())
          .IsTrue();
      // namespace is preserved verbatim from the source policy object.
      await Assert.That(hostAdmission.GetProperty("namespace").GetString())
          .IsEqualTo("pitcrew-runner-ns");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // hostAdmission namespace mismatch must fail closed rather than silently
  // apply a policy that diverges from the currently enforced namespace.
  [Test]
  public async Task
      BuildAndWriteManifest_Fails_Closed_When_HostAdmission_Namespace_Mismatches(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);

      // Construct a static profile where configuration.hostAdmission.
      // namespace differs from manifest.document.hostAdmission.namespace.
      var manifestDocument = new Dictionary<string, object?>
      {
        ["schemaVersion"] = 1,
        ["name"] = ImageRolloutTestData.DefaultProfileId,
        ["description"] = "Local test runner profile",
        ["image"] = "ghcr.io/example/runner:main",
        ["labels"] = new[] { "general-purpose" },
        ["replicas"] = 1,
        ["pullImage"] = true,
        ["hostAdmission"] = new
        {
          @namespace = "manifest-ns",
          capacityUnits = 128,
          safetyMarginUnits = 16,
          workerCostUnits = 1,
          reservationUnits = 4,
          borrowable = true,
        },
      };
      var staticProfileJson = JsonSerializer.Serialize(new
      {
        schemaVersion = 1,
        fingerprint = ImageRolloutTestData.StaticFingerprint,
        workerRevision = ImageRolloutTestData.CurrentWorkerRevision,
        manifest = new
        {
          kind = "external",
          sourcePath = Path.GetFullPath("runner-profile.json"),
          sha256 = new string('7', 64),
          document = manifestDocument,
        },
        configuration = new
        {
          managerContractVersion = 17,
          profile = ImageRolloutTestData.DefaultProfileId,
          image = "ghcr.io/example/runner:main",
          labels = new[] { "general-purpose" },
          hostAdmission = new
          {
            @namespace = "configuration-ns",
          },
        },
      });

      var exception = Assert.Throws<InvalidDataException>(
          () => builder.BuildAndWriteManifest(
              Guid.NewGuid(),
              ImageRolloutTestData.DefaultProfileId,
              staticProfileJson,
              NewRepository,
              NewDigest));

      await Assert.That(exception.Message).Contains("hostAdmission");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Configuration profile identity must match the target profile id to
  // prevent reconstructing a manifest under a mismatched authority.
  [Test]
  public async Task
      BuildAndWriteManifest_Fails_Closed_When_Configuration_Profile_Mismatches_Target(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = await File.ReadAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "static-profile.json"),
          cancellationToken);

      var exception = Assert.Throws<InvalidDataException>(
          () => builder.BuildAndWriteManifest(
              Guid.NewGuid(),
              // A different target profile identifier than what the
              // static state was written for.
              "different-profile",
              staticProfileJson,
              NewRepository,
              NewDigest));

      await Assert.That(exception.Message)
          .Contains("configuration.profile");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
