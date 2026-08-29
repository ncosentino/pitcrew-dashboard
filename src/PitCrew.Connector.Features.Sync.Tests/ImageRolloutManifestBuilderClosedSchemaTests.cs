using System.Text.Json;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ImageRolloutManifestBuilderClosedSchemaTests
{
  private const string NewRepository = "ghcr.io/example/runner-v2";
  private const string NewDigest =
      "sha256:9999999999999999999999999999999999999999999999999999999999999999";

  // Configuration carries integer byte counts and string cpuCores from
  // applied configuration but must be emitted as Docker-style byte-size
  // strings under the upstream schema names memory / memorySwap /
  // sharedMemory. cpuCores in configuration is already an invariant-
  // culture string (e.g. "1"), emitted as-is under the schema name cpus.
  // Runtime has no "runtimeClass" property in the upstream schema.
  [Test]
  public async Task
      BuildAndWriteManifest_Converts_Resource_And_Runtime_Byte_Sizes_And_Cpu(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var manifestDocument = new Dictionary<string, object?>
      {
        ["schemaVersion"] = 1,
        ["name"] = ImageRolloutTestData.DefaultProfileId,
        ["description"] = "Local test runner profile",
        ["image"] = "ghcr.io/example/runner:main",
        ["replicas"] = 1,
        ["pullImage"] = true,
      };
      // Configuration uses integer byte counts and string cpuCores. The
      // manifest builder must project them into the schema-shape
      // resources.memory / memorySwap / cpus / pids and runtime.
      // sharedMemory / devices, rejecting any additional properties.
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
          resources = new
          {
            cpuCores = "1",
            memoryBytes = 2L * 1024L * 1024L * 1024L,
            memorySwapBytes = 4L * 1024L * 1024L * 1024L,
            pids = 4096,
          },
          runtime = new
          {
            sharedMemoryBytes = 64L * 1024L * 1024L,
            devices = new[] { "kvm" },
          },
        },
      });

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
      var resources = manifestRoot.GetProperty("resources");
      // Byte counts are emitted as accepted byte-size strings under the
      // schema names memory / memorySwap; cpus is emitted as-is.
      await Assert.That(resources.GetProperty("cpus").GetString())
          .IsEqualTo("1");
      await Assert.That(resources.GetProperty("memory").GetString())
          .IsEqualTo("2g");
      await Assert.That(resources.GetProperty("memorySwap").GetString())
          .IsEqualTo("4g");
      await Assert.That(resources.GetProperty("pids").GetInt32())
          .IsEqualTo(4096);
      // The old byte-suffixed source names must not survive projection.
      await Assert.That(resources.TryGetProperty("cpuCores", out _))
          .IsFalse();
      await Assert.That(resources.TryGetProperty("memoryBytes", out _))
          .IsFalse();
      await Assert.That(resources.TryGetProperty("memorySwapBytes", out _))
          .IsFalse();

      var runtime = manifestRoot.GetProperty("runtime");
      // runtime is projected into sharedMemory + devices only; there is
      // no runtimeClass in the upstream runner-profile schema.
      await Assert.That(
              runtime.GetProperty("sharedMemory").GetString())
          .IsEqualTo("64m");
      await Assert.That(runtime.TryGetProperty("runtimeClass", out _))
          .IsFalse();
      await Assert.That(runtime.TryGetProperty("sharedMemoryBytes", out _))
          .IsFalse();
      // devices must be preserved verbatim.
      var devices = runtime.GetProperty("devices");
      await Assert.That(devices.GetArrayLength()).IsEqualTo(1);
      await Assert.That(devices[0].GetString()).IsEqualTo("kvm");
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

  // readOnlyVolumes computed target must be dropped; only name/source
  // are preserved so Setup-Runner recomputes target from name.
  [Test]
  public async Task
      BuildAndWriteManifest_ReadOnlyVolumes_Omit_Computed_Target(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = BuildStaticProfileWithReadOnlyVolumes(
          new object[]
          {
            new
            {
              name = "runner-cache",
              source = "runner-cache-src",
              target = "/computed/mount/target-should-be-dropped",
            },
          });

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
      var volumes =
          manifest.RootElement.GetProperty("readOnlyVolumes");
      await Assert.That(volumes.GetArrayLength()).IsEqualTo(1);
      var volume = volumes[0];
      await Assert.That(volume.GetProperty("name").GetString())
          .IsEqualTo("runner-cache");
      await Assert.That(volume.GetProperty("source").GetString())
          .IsEqualTo("runner-cache-src");
      await Assert.That(volume.TryGetProperty("target", out _)).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // runtime.devices is a closed literal set (currently only "kvm"). A raw
  // host device path such as "/dev/kvm" is a Setup-Runner implementation
  // detail and is schema-invalid; the builder must fail closed on it
  // rather than silently emit an invalid manifest.
  [Test]
  public async Task
      BuildAndWriteManifest_Rejects_Runtime_Device_Not_In_Closed_Set(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = BuildStaticProfileWithRuntimeDevices(
          new[] { "/dev/kvm" });

      var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
      {
        builder.BuildAndWriteManifest(
            Guid.NewGuid(),
            ImageRolloutTestData.DefaultProfileId,
            staticProfileJson,
            NewRepository,
            NewDigest);
        await Task.CompletedTask;
      });
      await Assert.That(ex).IsNotNull();
      await Assert.That(cancellationToken.IsCancellationRequested).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task
      BuildAndWriteManifest_Rejects_Runtime_Device_With_NonString_Element(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = BuildStaticProfileWithRawRuntimeJson(
          "\"runtime\":{\"devices\":[123]}");

      var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
      {
        builder.BuildAndWriteManifest(
            Guid.NewGuid(),
            ImageRolloutTestData.DefaultProfileId,
            staticProfileJson,
            NewRepository,
            NewDigest);
        await Task.CompletedTask;
      });
      await Assert.That(ex).IsNotNull();
      await Assert.That(ex!.Message)
          .IsEqualTo(
              "Static profile configuration.runtime.devices entry is not a string.");
      await Assert.That(cancellationToken.IsCancellationRequested).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // readOnlyVolumes.source is a Docker volume name (not a filesystem
  // path). A leading path separator or any '/' is schema-invalid.
  [Test]
  public async Task
      BuildAndWriteManifest_Rejects_ReadOnlyVolume_Source_With_Path_Separator(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = BuildStaticProfileWithReadOnlyVolumes(
          new object[]
          {
            new
            {
              name = "runner-cache",
              source = "/var/lib/pitcrew/cache",
            },
          });

      var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
      {
        builder.BuildAndWriteManifest(
            Guid.NewGuid(),
            ImageRolloutTestData.DefaultProfileId,
            staticProfileJson,
            NewRepository,
            NewDigest);
        await Task.CompletedTask;
      });
      await Assert.That(ex).IsNotNull();
      await Assert.That(cancellationToken.IsCancellationRequested).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // readOnlyVolumes.name must match the Docker volume-name pattern
  // (alphanumeric-led, no whitespace or control characters). Rejection
  // must be schema-shape, not a coincidental parse failure.
  [Test]
  public async Task
      BuildAndWriteManifest_Rejects_ReadOnlyVolume_Name_With_Whitespace(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var builder = ConnectorTestFactory.CreateImageRolloutManifestBuilder(
          options);
      var staticProfileJson = BuildStaticProfileWithReadOnlyVolumes(
          new object[]
          {
            new
            {
              name = "bad name",
              source = "runner-cache-src",
            },
          });

      var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
      {
        builder.BuildAndWriteManifest(
            Guid.NewGuid(),
            ImageRolloutTestData.DefaultProfileId,
            staticProfileJson,
            NewRepository,
            NewDigest);
        await Task.CompletedTask;
      });
      await Assert.That(ex).IsNotNull();
      await Assert.That(cancellationToken.IsCancellationRequested).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static string BuildStaticProfileWithReadOnlyVolumes(
      object[] readOnlyVolumes)
  {
    var manifestDocument = new Dictionary<string, object?>
    {
      ["schemaVersion"] = 1,
      ["name"] = ImageRolloutTestData.DefaultProfileId,
      ["description"] = "Local test runner profile",
      ["image"] = "ghcr.io/example/runner:main",
      ["replicas"] = 1,
      ["pullImage"] = true,
    };
    return JsonSerializer.Serialize(new
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
        readOnlyVolumes,
      },
    });
  }

  private static string BuildStaticProfileWithRuntimeDevices(string[] devices)
  {
    var manifestDocument = new Dictionary<string, object?>
    {
      ["schemaVersion"] = 1,
      ["name"] = ImageRolloutTestData.DefaultProfileId,
      ["description"] = "Local test runner profile",
      ["image"] = "ghcr.io/example/runner:main",
      ["replicas"] = 1,
      ["pullImage"] = true,
    };
    return JsonSerializer.Serialize(new
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
        runtime = new
        {
          devices,
        },
      },
    });
  }

  private static string BuildStaticProfileWithRawRuntimeJson(
      string rawRuntimeProperty)
  {
    var manifestDocument =
        "{\"schemaVersion\":1,"
        + "\"name\":\"" + ImageRolloutTestData.DefaultProfileId + "\","
        + "\"description\":\"Local test runner profile\","
        + "\"image\":\"ghcr.io/example/runner:main\","
        + "\"replicas\":1,"
        + "\"pullImage\":true}";
    var manifestBlock =
        "\"manifest\":{"
        + "\"kind\":\"external\","
        + "\"sourcePath\":"
        + JsonSerializer.Serialize(Path.GetFullPath("runner-profile.json"))
        + ","
        + "\"sha256\":\"" + new string('7', 64) + "\","
        + "\"document\":" + manifestDocument
        + "}";
    var configurationBlock =
        "\"configuration\":{"
        + "\"managerContractVersion\":17,"
        + "\"profile\":\"" + ImageRolloutTestData.DefaultProfileId + "\","
        + "\"image\":\"ghcr.io/example/runner:main\","
        + "\"labels\":[\"general-purpose\"],"
        + rawRuntimeProperty
        + "}";
    return "{"
        + "\"schemaVersion\":1,"
        + "\"fingerprint\":\"" + ImageRolloutTestData.StaticFingerprint
        + "\","
        + "\"workerRevision\":\""
        + ImageRolloutTestData.CurrentWorkerRevision + "\","
        + manifestBlock + ","
        + configurationBlock
        + "}";
  }
}
