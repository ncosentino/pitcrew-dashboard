using System.Text.Json;

namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// Deterministic focused checks that validate a reconstructed
/// runner-profile manifest against a bounded subset of the upstream
/// <c>runner-profile.schema.json</c> without embedding the full schema.
/// </summary>
/// <remarks>
/// Property allowlists are declared as ordinary <see cref="string"/>
/// arrays and matched with
/// <c>Enumerable.Contains(name, StringComparer.Ordinal)</c>. That keeps
/// the case-sensitive JSON-schema comparison explicit without the
/// analyzer suppressions that a <c>HashSet&lt;string&gt;</c> would need.
/// </remarks>
internal static class ImageRolloutManifestSchemaAssertions
{
  // Allowed top-level properties per runner-profile.schema.json.
  private static readonly string[] AllowedTopLevel =
  [
    "$schema", "schemaVersion", "name", "description", "image", "labels",
    "replicas", "pullImage", "disableDefaultLabels", "runnerGroup",
    "autoscaling", "hostAdmission", "readOnlyVolumes", "serviceNetwork",
    "runtime", "resources", "verificationCommands", "build",
  ];

  private static readonly string[] SchemaRejected =
  [
    "profile", "managerContractVersion", "scope", "namePrefix",
    "imageDigest", "resolvedImageId", "workerRuntimeContractVersion",
    "organization", "enterprise",
  ];

  private static readonly string[] AllowedResources =
  [
    "memory", "memorySwap", "cpus", "pids",
  ];

  private static readonly string[] AllowedRuntime =
  [
    "sharedMemory", "devices",
  ];

  private static readonly string[] AllowedServiceNetwork =
  [
    "source",
  ];

  private static readonly string[] AllowedVolumeKeys =
  [
    "name", "source",
  ];

  private static readonly string[] AllowedAutoscaling =
  [
    "mode", "minimumIdle", "scaleDownDelaySeconds", "maximumActiveWorkers",
  ];

  /// <summary>
  /// Validates that the produced runner-profile manifest matches a focused
  /// subset of the upstream <c>runner-profile.schema.json</c>: allowed
  /// top-level properties, required fields with expected JSON kinds and
  /// semantic constraints, nested resources/runtime/serviceNetwork/
  /// readOnlyVolumes/autoscaling shapes, and configuration-only or
  /// schema-rejected properties absent.
  /// </summary>
  public static async Task AssertConformsAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    await AssertTopLevelPropertiesAsync(manifest, cancellationToken);
    await AssertRequiredFieldsAsync(manifest, cancellationToken);
    await AssertLabelsAsync(manifest, cancellationToken);
    await AssertSchemaRejectedAbsentAsync(manifest, cancellationToken);
    await AssertResourcesAsync(manifest, cancellationToken);
    await AssertRuntimeAsync(manifest, cancellationToken);
    await AssertServiceNetworkAsync(manifest, cancellationToken);
    await AssertReadOnlyVolumesAsync(manifest, cancellationToken);
    await AssertAutoscalingAsync(manifest, cancellationToken);
  }

  private static async Task AssertTopLevelPropertiesAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    using var propertyEnumerator = manifest.EnumerateObject();
    while (propertyEnumerator.MoveNext())
    {
      var property = propertyEnumerator.Current;
      await Assert.That(
              AllowedTopLevel.Contains(property.Name, StringComparer.Ordinal))
          .IsTrue()
          .Because(
              $"manifest property '{property.Name}' is not allowed by upstream " +
              "runner-profile.schema.json (additionalProperties=false)");
    }
  }

  private static async Task AssertRequiredFieldsAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    await Assert.That(manifest.GetProperty("schemaVersion").GetInt32())
        .IsEqualTo(1);
    await Assert.That(manifest.GetProperty("name").ValueKind)
        .IsEqualTo(JsonValueKind.String);
    await Assert.That(
            manifest.GetProperty("description").GetString()!.Length > 0)
        .IsTrue()
        .Because("description must be nonblank");
    await Assert.That(manifest.GetProperty("image").ValueKind)
        .IsEqualTo(JsonValueKind.String);
    await Assert.That(manifest.GetProperty("replicas").GetInt32() >= 1)
        .IsTrue()
        .Because("replicas must be a positive integer");
    await Assert.That(manifest.GetProperty("pullImage").GetBoolean())
        .IsTrue();
  }

  private static async Task AssertLabelsAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var labels = manifest.GetProperty("labels");
    await Assert.That(labels.ValueKind).IsEqualTo(JsonValueKind.Array);
    await Assert.That(labels.GetArrayLength() > 0)
        .IsTrue()
        .Because("labels array must not be empty");
    using var labelEnumerator = labels.EnumerateArray();
    while (labelEnumerator.MoveNext())
    {
      var label = labelEnumerator.Current;
      await Assert.That(label.ValueKind).IsEqualTo(JsonValueKind.String);
      await Assert.That(string.IsNullOrWhiteSpace(label.GetString())).IsFalse();
    }
  }

  private static async Task AssertSchemaRejectedAbsentAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    foreach (var name in SchemaRejected)
    {
      await Assert.That(manifest.TryGetProperty(name, out _))
          .IsFalse()
          .Because(
              $"'{name}' must never appear in the reconstructed manifest");
    }
  }

  private static async Task AssertResourcesAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!manifest.TryGetProperty("resources", out var resources))
    {
      return;
    }
    using (var propertyEnumerator = resources.EnumerateObject())
    {
      while (propertyEnumerator.MoveNext())
      {
        var property = propertyEnumerator.Current;
        await Assert.That(
                AllowedResources.Contains(property.Name, StringComparer.Ordinal))
            .IsTrue()
            .Because(
                $"resources.{property.Name} is not a schema-allowed field");
      }
    }
    if (resources.TryGetProperty("memory", out var memory))
    {
      await Assert.That(memory.ValueKind).IsEqualTo(JsonValueKind.String);
    }
    if (resources.TryGetProperty("memorySwap", out var memSwap))
    {
      await Assert.That(memSwap.ValueKind).IsEqualTo(JsonValueKind.String);
    }
    if (resources.TryGetProperty("cpus", out var cpus))
    {
      await Assert.That(cpus.ValueKind).IsEqualTo(JsonValueKind.String);
    }
    if (resources.TryGetProperty("pids", out var pids))
    {
      await Assert.That(pids.ValueKind).IsEqualTo(JsonValueKind.Number);
    }
  }

  private static async Task AssertRuntimeAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!manifest.TryGetProperty("runtime", out var runtime))
    {
      return;
    }
    using (var propertyEnumerator = runtime.EnumerateObject())
    {
      while (propertyEnumerator.MoveNext())
      {
        var property = propertyEnumerator.Current;
        await Assert.That(
                AllowedRuntime.Contains(property.Name, StringComparer.Ordinal))
            .IsTrue()
            .Because(
                $"runtime.{property.Name} is not a schema-allowed field");
      }
    }
    if (runtime.TryGetProperty("sharedMemory", out var shared))
    {
      await Assert.That(shared.ValueKind).IsEqualTo(JsonValueKind.String);
    }
    if (!runtime.TryGetProperty("devices", out var devices))
    {
      return;
    }
    await Assert.That(devices.ValueKind).IsEqualTo(JsonValueKind.Array);
    await Assert.That(devices.GetArrayLength() > 0)
        .IsTrue()
        .Because("runtime.devices must contain at least one entry when present");
    using var deviceEnumerator = devices.EnumerateArray();
    while (deviceEnumerator.MoveNext())
    {
      var entry = deviceEnumerator.Current;
      await Assert.That(entry.ValueKind).IsEqualTo(JsonValueKind.String);
      await Assert.That(
              ImageRolloutManifestSchema.AllowedRuntimeDevices.Contains(
                  entry.GetString(),
                  StringComparer.Ordinal))
          .IsTrue()
          .Because(
              "runtime.devices entries must belong to the closed schema literal set");
    }
  }

  private static async Task AssertServiceNetworkAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!manifest.TryGetProperty("serviceNetwork", out var serviceNetwork))
    {
      return;
    }
    await Assert.That(serviceNetwork.ValueKind)
        .IsEqualTo(JsonValueKind.Object)
        .Because("serviceNetwork must be an object, never a bare string");
    await Assert.That(serviceNetwork.GetProperty("source").ValueKind)
        .IsEqualTo(JsonValueKind.String);
    using var propertyEnumerator = serviceNetwork.EnumerateObject();
    while (propertyEnumerator.MoveNext())
    {
      var property = propertyEnumerator.Current;
      await Assert.That(
              AllowedServiceNetwork.Contains(
                  property.Name,
                  StringComparer.Ordinal))
          .IsTrue()
          .Because(
              $"serviceNetwork.{property.Name} is not schema-allowed");
    }
  }

  private static async Task AssertReadOnlyVolumesAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!manifest.TryGetProperty("readOnlyVolumes", out var volumes))
    {
      return;
    }
    await Assert.That(volumes.ValueKind).IsEqualTo(JsonValueKind.Array);
    using var volumeEnumerator = volumes.EnumerateArray();
    while (volumeEnumerator.MoveNext())
    {
      var volume = volumeEnumerator.Current;
      await Assert.That(volume.ValueKind).IsEqualTo(JsonValueKind.Object);
      using (var propertyEnumerator = volume.EnumerateObject())
      {
        while (propertyEnumerator.MoveNext())
        {
          var property = propertyEnumerator.Current;
          await Assert.That(
                  AllowedVolumeKeys.Contains(
                      property.Name,
                      StringComparer.Ordinal))
              .IsTrue()
              .Because(
                  $"readOnlyVolumes[].{property.Name} is not schema-allowed");
        }
      }
      // Docker volume-name shape: alphanumeric-led, 2-64 length, only
      // alphanumerics/underscore/dot/hyphen. Never a filesystem path.
      var name = volume.GetProperty("name").GetString();
      var source = volume.GetProperty("source").GetString();
      await Assert.That(ImageRolloutManifestSchema.IsValidVolumeName(name))
          .IsTrue()
          .Because(
              "readOnlyVolumes[].name must match the Docker volume-name pattern");
      await Assert.That(ImageRolloutManifestSchema.IsValidVolumeName(source))
          .IsTrue()
          .Because(
              "readOnlyVolumes[].source must match the Docker volume-name pattern");
    }
  }

  private static async Task AssertAutoscalingAsync(
      JsonElement manifest,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!manifest.TryGetProperty("autoscaling", out var autoscaling))
    {
      return;
    }
    using var propertyEnumerator = autoscaling.EnumerateObject();
    while (propertyEnumerator.MoveNext())
    {
      var property = propertyEnumerator.Current;
      await Assert.That(
              AllowedAutoscaling.Contains(
                  property.Name,
                  StringComparer.Ordinal))
          .IsTrue()
          .Because(
              $"autoscaling.{property.Name} is not schema-allowed");
      if (string.Equals(
              property.Name,
              "maximumActiveWorkers",
              StringComparison.Ordinal))
      {
        await Assert.That(property.Value.ValueKind)
            .IsEqualTo(JsonValueKind.Number)
            .Because("maximumActiveWorkers is emitted as int, never null");
      }
    }
  }
}
