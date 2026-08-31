using System.Globalization;
using System.Text.Json;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Projection helpers that translate applied static
/// <c>configuration</c> properties into the upstream
/// <c>runner-profile.schema.json</c> shape when the connector reconstructs
/// a command-specific local worker profile manifest.
/// </summary>
/// <remarks>
/// Kept as a companion partial of <see cref="ImageRolloutManifestBuilder"/>
/// so the projection surface stays cohesive without exceeding the
/// repository's matched-context file ceilings.
/// </remarks>
internal sealed partial class ImageRolloutManifestBuilder
{
  private static void WriteConfigurationLabels(
      Utf8JsonWriter writer,
      JsonElement configuration)
  {
    // labels are required by upstream runner-profile.schema.json; the
    // reconstructed manifest cannot silently omit them or fall back to a
    // divergent policy. Fail closed when applied configuration.labels is
    // missing or malformed.
    if (!configuration.TryGetProperty("labels", out var labels) ||
        labels.ValueKind != JsonValueKind.Array)
    {
      throw new InvalidDataException(
          "Static profile configuration is missing the required labels array.");
    }
    var labelCount = 0;
    using (var labelEnumerator = labels.EnumerateArray())
    {
      while (labelEnumerator.MoveNext())
      {
        var label = labelEnumerator.Current;
        if (label.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(label.GetString()))
        {
          throw new InvalidDataException(
              "Static profile configuration.labels contains a non-string or " +
              "blank entry.");
        }
        labelCount++;
      }
    }
    if (labelCount == 0)
    {
      throw new InvalidDataException(
          "Static profile configuration.labels must not be empty.");
    }
    writer.WritePropertyName("labels");
    labels.WriteTo(writer);
  }

  private static void WriteConfigurationVerificationCommands(
      Utf8JsonWriter writer,
      JsonElement configuration)
  {
    if (!configuration.TryGetProperty(
            "verificationCommands",
            out var commands) ||
        commands.ValueKind != JsonValueKind.Array)
    {
      return;
    }
    writer.WritePropertyName("verificationCommands");
    commands.WriteTo(writer);
  }

  private static void WriteConfigurationAutoscaling(
      Utf8JsonWriter writer,
      JsonElement configuration)
  {
    if (!configuration.TryGetProperty("autoscaling", out var autoscaling) ||
        autoscaling.ValueKind != JsonValueKind.Object)
    {
      return;
    }
    // Reconstruct the schema-shape autoscaling: mode/minimumIdle/
    // scaleDownDelaySeconds/maximumActiveWorkers (optional). Nullable
    // maximumActiveWorkers is omitted rather than emitting a JSON null into
    // an integer-only schema slot. Reject unknown properties so a stale
    // configuration cannot leak through.
    string? mode = null;
    int? minimumIdle = null;
    int? scaleDownDelaySeconds = null;
    int? maximumActiveWorkers = null;
    using (var propertyEnumerator = autoscaling.EnumerateObject())
    {
      while (propertyEnumerator.MoveNext())
      {
        var property = propertyEnumerator.Current;
        switch (property.Name)
        {
          case "mode":
            if (property.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
              throw new InvalidDataException(
                  "Static profile configuration.autoscaling.mode is invalid.");
            }
            mode = property.Value.GetString();
            break;
          case "minimumIdle":
            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt32(out var minimumIdleValue) ||
                minimumIdleValue < 0)
            {
              throw new InvalidDataException(
                  "Static profile configuration.autoscaling.minimumIdle is invalid.");
            }
            minimumIdle = minimumIdleValue;
            break;
          case "scaleDownDelaySeconds":
            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt32(out var delayValue) ||
                delayValue < 0)
            {
              throw new InvalidDataException(
                  "Static profile configuration.autoscaling.scaleDownDelaySeconds is invalid.");
            }
            scaleDownDelaySeconds = delayValue;
            break;
          case "maximumActiveWorkers":
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
              // Absent maximum: omit rather than emit null.
              break;
            }
            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt32(out var maxValue) ||
                maxValue < 0)
            {
              throw new InvalidDataException(
                  "Static profile configuration.autoscaling.maximumActiveWorkers is invalid.");
            }
            maximumActiveWorkers = maxValue;
            break;
          default:
            throw new InvalidDataException(
                "Static profile configuration.autoscaling contains an unknown property.");
        }
      }
    }
    writer.WritePropertyName("autoscaling");
    writer.WriteStartObject();
    if (mode is not null)
    {
      writer.WriteString("mode", mode);
    }
    if (minimumIdle is not null)
    {
      writer.WriteNumber("minimumIdle", minimumIdle.Value);
    }
    if (scaleDownDelaySeconds is not null)
    {
      writer.WriteNumber("scaleDownDelaySeconds", scaleDownDelaySeconds.Value);
    }
    if (maximumActiveWorkers is not null)
    {
      writer.WriteNumber("maximumActiveWorkers", maximumActiveWorkers.Value);
    }
    writer.WriteEndObject();
  }

  private static void WriteConfigurationResources(
      Utf8JsonWriter writer,
      JsonElement configuration)
  {
    if (!configuration.TryGetProperty("resources", out var resources) ||
        resources.ValueKind != JsonValueKind.Object)
    {
      return;
    }
    // Project applied configuration byte-count properties into their
    // upstream schema counterparts: memoryBytes→memory, memorySwapBytes→
    // memorySwap, cpuCores→cpus (string; already an invariant-culture
    // string in configuration), pids→pids (integer). Reject unknown
    // properties so a stale configuration cannot leak through.
    string? memory = null;
    string? memorySwap = null;
    string? cpus = null;
    int? pids = null;
    using (var propertyEnumerator = resources.EnumerateObject())
    {
      while (propertyEnumerator.MoveNext())
      {
        var property = propertyEnumerator.Current;
        switch (property.Name)
        {
          case "memoryBytes":
            memory = FormatByteSizeFromNumber(property.Value, property.Name);
            break;
          case "memorySwapBytes":
            memorySwap = FormatByteSizeFromNumber(property.Value, property.Name);
            break;
          case "cpuCores":
            if (property.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
              throw new InvalidDataException(
                  "Static profile configuration.resources.cpuCores must be a nonblank string.");
            }
            cpus = property.Value.GetString();
            break;
          case "pids":
            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt32(out var pidsValue) ||
                pidsValue < 0)
            {
              throw new InvalidDataException(
                  "Static profile configuration.resources.pids is not a non-negative integer.");
            }
            pids = pidsValue;
            break;
          default:
            throw new InvalidDataException(
                "Static profile configuration.resources contains an unknown property.");
        }
      }
    }
    writer.WritePropertyName("resources");
    writer.WriteStartObject();
    if (memory is not null)
    {
      writer.WriteString("memory", memory);
    }
    if (memorySwap is not null)
    {
      writer.WriteString("memorySwap", memorySwap);
    }
    if (cpus is not null)
    {
      writer.WriteString("cpus", cpus);
    }
    if (pids is not null)
    {
      writer.WriteNumber("pids", pids.Value);
    }
    writer.WriteEndObject();
  }

  private static void WriteConfigurationRuntime(
      Utf8JsonWriter writer,
      JsonElement configuration)
  {
    if (!configuration.TryGetProperty("runtime", out var runtime) ||
        runtime.ValueKind != JsonValueKind.Object)
    {
      return;
    }
    // Project applied configuration runtime into upstream schema shape:
    // sharedMemoryBytes→sharedMemory (byte-size string), devices as an
    // array whose entries are drawn from a closed literal set. There is
    // no runtimeClass in the upstream runner-profile schema. Reject
    // unknown properties.
    string? sharedMemory = null;
    List<string>? devices = null;
    using (var propertyEnumerator = runtime.EnumerateObject())
    {
      while (propertyEnumerator.MoveNext())
      {
        var property = propertyEnumerator.Current;
        switch (property.Name)
        {
          case "sharedMemoryBytes":
            sharedMemory = FormatByteSizeFromNumber(property.Value, property.Name);
            break;
          case "devices":
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
              throw new InvalidDataException(
                  "Static profile configuration.runtime.devices is not an array.");
            }
            devices = ProjectRuntimeDevices(property.Value);
            break;
          default:
            throw new InvalidDataException(
                "Static profile configuration.runtime contains an unknown property.");
        }
      }
    }
    writer.WritePropertyName("runtime");
    writer.WriteStartObject();
    if (sharedMemory is not null)
    {
      writer.WriteString("sharedMemory", sharedMemory);
    }
    if (devices is not null)
    {
      writer.WritePropertyName("devices");
      writer.WriteStartArray();
      foreach (var device in devices)
      {
        writer.WriteStringValue(device);
      }
      writer.WriteEndArray();
    }
    writer.WriteEndObject();
  }

  /// <summary>
  /// Projects the applied configuration runtime.devices array into the
  /// upstream <c>runner-profile.schema.json</c> shape: each entry MUST
  /// match the closed literal set (currently <c>kvm</c>) and the array
  /// MUST contain at least one entry when the property is emitted.
  /// Raw filesystem device paths (e.g. <c>/dev/kvm</c>) are a Setup-Runner
  /// implementation detail and are schema-invalid here.
  /// </summary>
  private static List<string> ProjectRuntimeDevices(JsonElement devices)
  {
    var projected = new List<string>();
    using var deviceEnumerator = devices.EnumerateArray();
    while (deviceEnumerator.MoveNext())
    {
      var entry = deviceEnumerator.Current;
      if (entry.ValueKind != JsonValueKind.String)
      {
        throw new InvalidDataException(
            "Static profile configuration.runtime.devices entry is not a string.");
      }
      var value = entry.GetString();
      if (string.IsNullOrEmpty(value) ||
          !ImageRolloutManifestSchema.AllowedRuntimeDevices.Contains(
              value,
              StringComparer.Ordinal))
      {
        throw new InvalidDataException(
            "Static profile configuration.runtime.devices entry is not a " +
            "schema-allowed literal.");
      }
      projected.Add(value);
    }
    if (projected.Count == 0)
    {
      throw new InvalidDataException(
          "Static profile configuration.runtime.devices must contain at " +
          "least one entry when present.");
    }
    return projected;
  }

  private static void WriteConfigurationReadOnlyVolumes(
      Utf8JsonWriter writer,
      JsonElement configuration)
  {
    if (!configuration.TryGetProperty(
            "readOnlyVolumes",
            out var volumes) ||
        volumes.ValueKind != JsonValueKind.Array)
    {
      return;
    }
    // Each volume entry must be an object with a Docker-volume-name shaped
    // name and source; the computed target is dropped so Setup-Runner
    // recomputes it. Reject unknown volume properties and any name/source
    // that violates the closed volume-name pattern (a raw filesystem path
    // is not a Docker volume name and must be rejected).
    writer.WritePropertyName("readOnlyVolumes");
    writer.WriteStartArray();
    using (var arrayEnumerator = volumes.EnumerateArray())
    {
      while (arrayEnumerator.MoveNext())
      {
        var volume = arrayEnumerator.Current;
        if (volume.ValueKind != JsonValueKind.Object)
        {
          throw new InvalidDataException(
              "Static profile readOnlyVolumes entry is not an object.");
        }
        string? name = null;
        string? source = null;
        using (var propertyEnumerator = volume.EnumerateObject())
        {
          while (propertyEnumerator.MoveNext())
          {
            var property = propertyEnumerator.Current;
            switch (property.Name)
            {
              case "name":
                name = ExtractSchemaVolumeName(property, "name");
                break;
              case "source":
                source = ExtractSchemaVolumeName(property, "source");
                break;
              case "target":
                // Intentionally dropped: computed by Setup-Runner and never
                // forwarded through the reconstructed manifest.
                break;
              default:
                throw new InvalidDataException(
                    "Static profile readOnlyVolumes entry contains an unknown property.");
            }
          }
        }
        if (name is null || source is null)
        {
          throw new InvalidDataException(
              "Static profile readOnlyVolumes entry must have name and source.");
        }
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("source", source);
        writer.WriteEndObject();
      }
    }
    writer.WriteEndArray();
  }

  private static string ExtractSchemaVolumeName(
      JsonProperty property,
      string field)
  {
    if (property.Value.ValueKind != JsonValueKind.String)
    {
      throw new InvalidDataException(
          $"Static profile readOnlyVolumes[].{field} is not a string.");
    }
    var value = property.Value.GetString();
    if (!ImageRolloutManifestSchema.IsValidVolumeName(value))
    {
      throw new InvalidDataException(
          $"Static profile readOnlyVolumes[].{field} violates the closed " +
          "Docker volume-name pattern.");
    }
    return value!;
  }

  private static void WriteConfigurationServiceNetwork(
      Utf8JsonWriter writer,
      JsonElement configuration)
  {
    if (!configuration.TryGetProperty(
            "serviceNetwork",
            out var serviceNetwork) ||
        serviceNetwork.ValueKind == JsonValueKind.Null ||
        serviceNetwork.ValueKind == JsonValueKind.Undefined)
    {
      return;
    }
    // Upstream runner-profile.schema.json requires serviceNetwork to be
    // an object with a "source" property; a bare string is schema-invalid.
    // Fail closed if the applied configuration violates that shape.
    if (serviceNetwork.ValueKind != JsonValueKind.Object)
    {
      throw new InvalidDataException(
          "Static profile configuration.serviceNetwork must be an object.");
    }
    string? source = null;
    using (var propertyEnumerator = serviceNetwork.EnumerateObject())
    {
      while (propertyEnumerator.MoveNext())
      {
        var property = propertyEnumerator.Current;
        switch (property.Name)
        {
          case "source":
            if (property.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
              throw new InvalidDataException(
                  "Static profile configuration.serviceNetwork.source is invalid.");
            }
            source = property.Value.GetString();
            break;
          default:
            throw new InvalidDataException(
                "Static profile configuration.serviceNetwork contains an unknown property.");
        }
      }
    }
    if (source is null)
    {
      throw new InvalidDataException(
          "Static profile configuration.serviceNetwork must contain a source.");
    }
    writer.WritePropertyName("serviceNetwork");
    writer.WriteStartObject();
    writer.WriteString("source", source);
    writer.WriteEndObject();
  }

  private static string FormatByteSizeFromNumber(
      JsonElement number,
      string propertyName)
  {
    if (number.ValueKind != JsonValueKind.Number ||
        !number.TryGetInt64(out var bytes) ||
        bytes < 0)
    {
      throw new InvalidDataException(
          $"Static profile configuration.{propertyName} is not a " +
          "non-negative integer.");
    }
    return FormatByteSize(bytes);
  }

  private static JsonElement? GetObjectOrNull(
      JsonElement source,
      string propertyName)
  {
    if (source.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Object)
    {
      return value;
    }
    return null;
  }

  private static string GetRequiredString(
      JsonElement source,
      string propertyName)
  {
    if (!source.TryGetProperty(propertyName, out var value) ||
        value.ValueKind != JsonValueKind.String)
    {
      throw new InvalidDataException(
          $"Static profile is missing the required '{propertyName}' string.");
    }
    var text = value.GetString();
    if (string.IsNullOrWhiteSpace(text))
    {
      throw new InvalidDataException(
          $"Static profile '{propertyName}' must not be empty.");
    }
    return text;
  }

  private static string? GetStringOrNull(
      JsonElement source,
      string propertyName)
  {
    if (source.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String)
    {
      return value.GetString();
    }
    return null;
  }

  private static string FormatByteSize(long bytes)
  {
    // Emit Docker-style byte-size suffixes. Compose/Docker accept "k", "m",
    // "g" (and their bare integer form for raw bytes). Preferring the
    // largest exact suffix keeps generated manifests readable while
    // remaining unambiguous.
    const long Gibibyte = 1024L * 1024L * 1024L;
    const long Mebibyte = 1024L * 1024L;
    const long Kibibyte = 1024L;
    if (bytes == 0)
    {
      return "0";
    }
    if (bytes % Gibibyte == 0)
    {
      return (bytes / Gibibyte).ToString(CultureInfo.InvariantCulture) + "g";
    }
    if (bytes % Mebibyte == 0)
    {
      return (bytes / Mebibyte).ToString(CultureInfo.InvariantCulture) + "m";
    }
    if (bytes % Kibibyte == 0)
    {
      return (bytes / Kibibyte).ToString(CultureInfo.InvariantCulture) + "k";
    }
    return bytes.ToString(CultureInfo.InvariantCulture);
  }
}
