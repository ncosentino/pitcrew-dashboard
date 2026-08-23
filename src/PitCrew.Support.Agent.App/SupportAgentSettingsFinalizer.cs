using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PitCrew.Support.Agent.App;

internal enum SupportEnrollmentFinalizationStatus
{
  Succeeded,
  AlreadyFinalized,
  ActiveIdentityUnavailable,
  SettingsInvalid,
}

internal static class SupportAgentSettingsFinalizer
{
  private const int MaximumSettingsBytes = 1_048_576;
  private static readonly string[] _bootstrapPropertyNames =
  [
      "DashboardUrl",
      "TenantId",
      "DisplayName",
      "EnrollmentCode",
  ];
  private static readonly JsonSerializerOptions _jsonOptions = new()
  {
    WriteIndented = true,
  };

  public static SupportEnrollmentFinalizationStatus Finalize(
      string contentRootPath)
  {
    var settingsPath = Path.Combine(
        contentRootPath,
        "appsettings.json");
    FileInfo file;
    try
    {
      file = new FileInfo(settingsPath);
      if (!file.Exists ||
          file.Length is <= 0 or > MaximumSettingsBytes ||
          (file.Attributes & FileAttributes.ReparsePoint) != 0)
      {
        return SupportEnrollmentFinalizationStatus.SettingsInvalid;
      }
    }
    catch (IOException)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }
    catch (UnauthorizedAccessException)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }

    JsonObject root;
    try
    {
      root = JsonNode.Parse(
          File.ReadAllText(
              settingsPath,
              new UTF8Encoding(
                  encoderShouldEmitUTF8Identifier: false,
                  throwOnInvalidBytes: true))) as JsonObject ??
          throw new JsonException();
    }
    catch (JsonException)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }
    catch (DecoderFallbackException)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }
    catch (IOException)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }
    catch (UnauthorizedAccessException)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }

    if (root["PitCrewSupport"] is not JsonObject support ||
        support["Agent"] is not JsonObject agent)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }
    var changed = false;
    foreach (var propertyName in _bootstrapPropertyNames)
    {
      var actualName = agent
          .Select(property => property.Key)
          .FirstOrDefault(
              candidate => string.Equals(
                  candidate,
                  propertyName,
                  StringComparison.OrdinalIgnoreCase));
      if (actualName is not null)
      {
        changed |= agent.Remove(actualName);
      }
    }
    if (!changed)
    {
      return SupportEnrollmentFinalizationStatus.AlreadyFinalized;
    }

    var temporaryPath =
        $"{settingsPath}.{Guid.NewGuid():N}.tmp";
    try
    {
      File.WriteAllText(
          temporaryPath,
          root.ToJsonString(_jsonOptions) + "\n",
          new UTF8Encoding(
              encoderShouldEmitUTF8Identifier: false,
              throwOnInvalidBytes: true));
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(
            temporaryPath,
            File.GetUnixFileMode(settingsPath));
      }
      File.Move(
          temporaryPath,
          settingsPath,
          overwrite: true);
      return SupportEnrollmentFinalizationStatus.Succeeded;
    }
    catch (IOException)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }
    catch (UnauthorizedAccessException)
    {
      return SupportEnrollmentFinalizationStatus.SettingsInvalid;
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }
}
