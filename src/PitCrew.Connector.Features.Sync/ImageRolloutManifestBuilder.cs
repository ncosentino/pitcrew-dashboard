using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Reconstructs a command-specific local worker profile manifest from the
/// applied static <c>configuration</c> together with the schema-required
/// descriptive/policy fields from <c>manifest.document</c>.
/// </summary>
/// <remarks>
/// <para>
/// The static <c>configuration</c> is authoritative because it captures
/// prior CLI overrides that the original manifest document may not reflect.
/// The generated manifest therefore derives labels, runnerGroup,
/// verificationCommands, autoscaling, resources, runtime, readOnlyVolumes,
/// serviceNetwork, and disableDefaultLabels from configuration, while
/// description, replicas, and hostAdmission come from the applied manifest
/// document.
/// </para>
/// <para>
/// Only the immutable image authority is intentionally changed: <c>image</c>
/// is emitted as <c>&lt;local repo&gt;@&lt;digest&gt;</c> and
/// <c>pullImage</c> is forced true. <c>build</c>, configuration-only fields
/// (<c>managerContractVersion</c>, <c>profile</c>, <c>scope</c>,
/// <c>resolvedImageId</c>, <c>namePrefix</c>, <c>organization</c>,
/// <c>enterprise</c>, <c>workerRuntimeContractVersion</c>) and any
/// non-schema property are omitted so the produced manifest passes upstream
/// <c>runner-profile.schema.json</c> with <c>additionalProperties = false</c>.
/// </para>
/// <para>
/// Property names are normalized to their exact upstream schema counterparts
/// during projection: applied <c>configuration.resources.memoryBytes</c>,
/// <c>memorySwapBytes</c>, and <c>cpuCores</c> map to manifest
/// <c>resources.memory</c>, <c>memorySwap</c>, and <c>cpus</c>; applied
/// <c>configuration.runtime.sharedMemoryBytes</c> maps to manifest
/// <c>runtime.sharedMemory</c>; applied <c>configuration.serviceNetwork</c>
/// is always emitted as an object <c>{ source }</c> (never a bare string).
/// </para>
/// <para>
/// The full hostAdmission policy is preserved from the manifest document
/// only after proving its <c>namespace</c> matches
/// <c>configuration.hostAdmission.namespace</c>; otherwise the build fails
/// closed rather than silently applying a divergent policy.
/// </para>
/// <para>
/// <c>namePrefix</c>, current capacity, current routing scope and identity,
/// and pause state remain on the Setup-Runner CLI; they are not
/// schema-permitted manifest properties and would be rejected upstream.
/// </para>
/// </remarks>
internal sealed partial class ImageRolloutManifestBuilder(
    IOptions<ConnectorOptions> _options,
    ILogger<ImageRolloutManifestBuilder> _logger)
{
  public void ValidateReconstructable(
      string profileId,
      string staticProfileJson) =>
      _ = BuildManifest(
          profileId,
          staticProfileJson,
          "registry.invalid/pitcrew",
          $"sha256:{new string('0', 64)}");

  public string BuildAndWriteManifest(
      Guid commandId,
      string profileId,
      string staticProfileJson,
      string registryRepository,
      string targetDigest)
  {
    var content = BuildManifest(
        profileId,
        staticProfileJson,
        registryRepository,
        targetDigest);
    var directory = EnsureManifestDirectory();
    var manifestPath = Path.Combine(directory, $"{commandId:N}.json");
    using var writeStream = new FileStream(
        manifestPath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        4096,
        FileOptions.WriteThrough);
    writeStream.Write(content);
    writeStream.Flush(flushToDisk: true);

    LogManifestWritten(commandId, profileId);
    return manifestPath;
  }

  private static byte[] BuildManifest(
      string profileId,
      string staticProfileJson,
      string registryRepository,
      string targetDigest)
  {
    using var staticProfile = JsonDocument.Parse(staticProfileJson);
    var root = staticProfile.RootElement;
    if (root.GetProperty("schemaVersion").GetInt32() != 1)
    {
      throw new InvalidDataException(
          "Static profile schema is not supported.");
    }
    if (!root.TryGetProperty("configuration", out var configuration) ||
        configuration.ValueKind != JsonValueKind.Object)
    {
      throw new InvalidDataException(
          "Static profile has no applied configuration.");
    }
    if (!root.TryGetProperty("manifest", out var manifest) ||
        manifest.ValueKind != JsonValueKind.Object ||
        !manifest.TryGetProperty("document", out var manifestDocument) ||
        manifestDocument.ValueKind != JsonValueKind.Object)
    {
      throw new InvalidDataException(
          "Static profile has no locally applied manifest.document.");
    }
    var manifestKind = GetRequiredString(manifest, "kind");
    if (manifestKind is not ("built-in" or "external"))
    {
      throw new InvalidDataException(
          "Static profile manifest kind is not supported.");
    }
    var manifestSourcePath = GetRequiredString(manifest, "sourcePath");
    if (!Path.IsPathFullyQualified(manifestSourcePath))
    {
      throw new InvalidDataException(
          "Static profile manifest sourcePath is not absolute.");
    }
    if (NormalizeHexOrNull(GetRequiredString(manifest, "sha256")) is null)
    {
      throw new InvalidDataException(
          "Static profile manifest sha256 is invalid.");
    }

    var configurationProfile = GetRequiredString(configuration, "profile");
    if (!string.Equals(configurationProfile, profileId, StringComparison.Ordinal))
    {
      throw new InvalidDataException(
          "Static profile configuration.profile does not match the target " +
          "profile identifier.");
    }
    var manifestName = GetRequiredString(manifestDocument, "name");
    if (!string.Equals(manifestName, configurationProfile, StringComparison.Ordinal))
    {
      throw new InvalidDataException(
          "Manifest document name does not match configuration.profile.");
    }
    if (!manifestDocument.TryGetProperty("description", out var description) ||
        description.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(description.GetString()))
    {
      throw new InvalidDataException(
          "Manifest document is missing the required nonblank description.");
    }
    if (!manifestDocument.TryGetProperty("replicas", out var replicas) ||
        replicas.ValueKind != JsonValueKind.Number ||
        !replicas.TryGetInt32(out var replicasValue) ||
        replicasValue < 1)
    {
      throw new InvalidDataException(
          "Manifest document is missing a positive integer replicas value.");
    }

    // hostAdmission full policy lives in the manifest document. The static
    // configuration only carries hostAdmission.namespace. Preserve the
    // document's policy only after proving the namespaces match; otherwise
    // fail closed so we never apply a policy divergent from the currently
    // enforced one.
    var manifestHostAdmission = GetObjectOrNull(
        manifestDocument,
        "hostAdmission");
    var configurationHostAdmission = GetObjectOrNull(
        configuration,
        "hostAdmission");
    if (manifestHostAdmission is null ^ configurationHostAdmission is null)
    {
      throw new InvalidDataException(
          "hostAdmission is present in only one of manifest.document or " +
          "configuration; the applied policy cannot be reconstructed safely.");
    }
    if (manifestHostAdmission is not null &&
        configurationHostAdmission is not null)
    {
      var manifestNamespace = GetStringOrNull(
          manifestHostAdmission.Value,
          "namespace");
      var configurationNamespace = GetStringOrNull(
          configurationHostAdmission.Value,
          "namespace");
      if (!string.Equals(
              manifestNamespace,
              configurationNamespace,
              StringComparison.Ordinal))
      {
        throw new InvalidDataException(
            "hostAdmission.namespace differs between manifest.document and " +
            "configuration; the applied policy cannot be reconstructed safely.");
      }
    }

    var immutableReference = $"{registryRepository}@{targetDigest}";

    using var writeStream = new MemoryStream();
    using var writer = new Utf8JsonWriter(
        writeStream,
        new JsonWriterOptions
        {
          Indented = true,
        });

    writer.WriteStartObject();
    // Emit schema-required properties in a predictable order so the file is
    // stable/deterministic across rebuilds.
    writer.WriteNumber("schemaVersion", 1);
    writer.WriteString("name", configurationProfile);
    writer.WriteString("description", description.GetString()!);
    writer.WriteString("image", immutableReference);
    writer.WriteNumber("replicas", replicasValue);
    writer.WriteBoolean("pullImage", true);
    WriteBooleanIfPresent(
        writer,
        configuration,
        "disableDefaultLabels",
        "disableDefaultLabels");
    WriteStringIfPresent(writer, configuration, "runnerGroup", "runnerGroup");
    WriteConfigurationLabels(writer, configuration);
    WriteConfigurationVerificationCommands(writer, configuration);
    WriteConfigurationAutoscaling(writer, configuration);
    WriteConfigurationResources(writer, configuration);
    WriteConfigurationRuntime(writer, configuration);
    WriteConfigurationReadOnlyVolumes(writer, configuration);
    WriteConfigurationServiceNetwork(writer, configuration);
    if (manifestHostAdmission is not null)
    {
      writer.WritePropertyName("hostAdmission");
      manifestHostAdmission.Value.WriteTo(writer);
    }
    writer.WriteEndObject();
    writer.Flush();
    return writeStream.ToArray();
  }

  private static string? NormalizeHexOrNull(string value)
  {
    if (value.Length != 64)
    {
      return null;
    }
    foreach (var character in value)
    {
      if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
      {
        return null;
      }
    }
    return value;
  }

  private static void WriteBooleanIfPresent(
      Utf8JsonWriter writer,
      JsonElement source,
      string sourceName,
      string outputName)
  {
    if (source.TryGetProperty(sourceName, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         value.ValueKind == JsonValueKind.False))
    {
      writer.WriteBoolean(outputName, value.GetBoolean());
    }
  }

  private static void WriteStringIfPresent(
      Utf8JsonWriter writer,
      JsonElement source,
      string sourceName,
      string outputName)
  {
    if (source.TryGetProperty(sourceName, out var value) &&
        value.ValueKind == JsonValueKind.String)
    {
      writer.WriteString(outputName, value.GetString());
    }
  }

  public void PruneOrphans(IReadOnlySet<string> referencedManifestPaths)
  {
    var directory = GetManifestDirectory();
    if (!Directory.Exists(directory))
    {
      return;
    }
    var files = Directory.GetFiles(directory, "*.json")
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .ToArray();
    var retained = 0;
    foreach (var path in files)
    {
      if (referencedManifestPaths.Contains(path))
      {
        continue;
      }
      if (retained < _options.Value.ImageRolloutRetainedManifests)
      {
        retained++;
        continue;
      }
      try
      {
        File.Delete(path);
      }
      catch (IOException)
      {
        LogManifestCleanupFailure();
      }
      catch (UnauthorizedAccessException)
      {
        LogManifestCleanupFailure();
      }
    }
  }

  private string EnsureManifestDirectory()
  {
    var stateRoot = ImageRolloutStatePathGuard.CanonicalizeStateRoot(
        _options.Value.ImageRolloutStatePath);
    if (!Directory.Exists(stateRoot))
    {
      // Fail closed rather than silently creating an insecure ancestor chain:
      // the installer is responsible for provisioning ImageRolloutStatePath
      // with restrictive ownership/permissions before the connector runs.
      throw new UnauthorizedAccessException(
          $"Image rollout state root '{stateRoot}' does not exist. " +
          "Reinstall the connector with -EnableImageRollout so the installer " +
          "can provision the protected rollout state directory.");
    }
    // Refuse to follow a state root that is itself a symlink/junction: the
    // installer must materialize a real protected directory.
    ImageRolloutStatePathGuard.EnsureNotReparsePoint(stateRoot);
    var directory = ImageRolloutStatePathGuard.CombineConfinedChild(
        stateRoot,
        ManifestsSubdirectory);
    if (Directory.Exists(directory))
    {
      // Refuse an existing manifests child that is a symlink/junction
      // pointing elsewhere; only real directories are trusted.
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(directory);
      return directory;
    }
    if (OperatingSystem.IsWindows())
    {
      Directory.CreateDirectory(directory);
      return directory;
    }
    Directory.CreateDirectory(
        directory,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    return directory;
  }

  private string GetManifestDirectory()
  {
    var stateRoot = ImageRolloutStatePathGuard.CanonicalizeStateRoot(
        _options.Value.ImageRolloutStatePath);
    return ImageRolloutStatePathGuard.CombineConfinedChild(
        stateRoot,
        ManifestsSubdirectory);
  }

  private const string ManifestsSubdirectory = "manifests";

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Reconstructed rollout manifest for command {CommandId} profile {ProfileId} written to the connector-controlled rollout state directory.")]
  private partial void LogManifestWritten(
      Guid commandId,
      string profileId);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "A rollout manifest could not be pruned from the connector-controlled rollout state directory.")]
  private partial void LogManifestCleanupFailure();
}
