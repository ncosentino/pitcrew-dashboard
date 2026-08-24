using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PitCrew.Support.Agent.App;

internal enum SupportEnrollmentFinalizationStatus
{
  Succeeded,
  AlreadyFinalized,
  ActiveIdentityUnavailable,
  RollbackRequired,
  SettingsInvalid,
}

internal enum SupportEnrollmentRollbackStatus
{
  Succeeded,
  BackupUnavailable,
  SettingsInvalid,
}

internal static class SupportAgentSettingsFinalizer
{
  private const int MaximumSettingsBytes = 1_048_576;
  internal const string BackupFileName =
      "enrollment-bootstrap-backup.json";
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
    => FinalizeCore(contentRootPath, preserveBackup: false);

  public static SupportEnrollmentFinalizationStatus FinalizeWithBackup(
      string contentRootPath)
    => FinalizeCore(contentRootPath, preserveBackup: true);

  public static SupportEnrollmentRollbackStatus Rollback(
      string contentRootPath)
  {
    var settingsPath = Path.Combine(
        contentRootPath,
        "appsettings.json");
    var backupPath = Path.Combine(
        contentRootPath,
        BackupFileName);
    FileInfo backup;
    try
    {
      backup = new FileInfo(backupPath);
      if (!backup.Exists)
      {
        return SupportEnrollmentRollbackStatus.BackupUnavailable;
      }
      if (backup.Length is <= 0 or > MaximumSettingsBytes ||
          (backup.Attributes & FileAttributes.ReparsePoint) != 0)
      {
        return SupportEnrollmentRollbackStatus.SettingsInvalid;
      }
      File.Move(
          backupPath,
          settingsPath,
          overwrite: true);
      return SupportEnrollmentRollbackStatus.Succeeded;
    }
    catch (IOException)
    {
      return SupportEnrollmentRollbackStatus.SettingsInvalid;
    }
    catch (UnauthorizedAccessException)
    {
      return SupportEnrollmentRollbackStatus.SettingsInvalid;
    }
  }

  private static SupportEnrollmentFinalizationStatus FinalizeCore(
      string contentRootPath,
      bool preserveBackup)
  {
    var settingsPath = Path.Combine(
        contentRootPath,
        "appsettings.json");
    var backupPath = Path.Combine(
        contentRootPath,
        BackupFileName);
    FileInfo file;
    byte[] originalBytes;
    try
    {
      file = new FileInfo(settingsPath);
      if (!file.Exists ||
          file.Length is <= 0 or > MaximumSettingsBytes ||
          (file.Attributes & FileAttributes.ReparsePoint) != 0)
      {
        return SupportEnrollmentFinalizationStatus.SettingsInvalid;
      }
      if (preserveBackup && File.Exists(backupPath))
      {
        return SupportEnrollmentFinalizationStatus.RollbackRequired;
      }
      originalBytes = File.ReadAllBytes(settingsPath);
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
          new UTF8Encoding(
              encoderShouldEmitUTF8Identifier: false,
              throwOnInvalidBytes: true).GetString(
                  originalBytes)) as JsonObject ??
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

    if (GetObjectPropertyOrNull(
            root,
            "PitCrewSupport") is not { } support ||
        GetObjectPropertyOrNull(
            support,
            "Agent") is not { } agent)
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

    var backupTemporaryPath =
        $"{backupPath}.{Guid.NewGuid():N}.tmp";
    var temporaryPath =
        $"{settingsPath}.{Guid.NewGuid():N}.tmp";
    try
    {
      if (preserveBackup)
      {
        File.WriteAllBytes(
            backupTemporaryPath,
            originalBytes);
        if (!OperatingSystem.IsWindows())
        {
          File.SetUnixFileMode(
              backupTemporaryPath,
              File.GetUnixFileMode(settingsPath));
        }
        File.Move(
            backupTemporaryPath,
            backupPath,
            overwrite: false);
      }
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
      if (File.Exists(backupTemporaryPath))
      {
        File.Delete(backupTemporaryPath);
      }
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }

  private static JsonObject? GetObjectPropertyOrNull(
      JsonObject parent,
      string propertyName)
  {
    var actualName = parent
        .Select(property => property.Key)
        .FirstOrDefault(
            candidate => string.Equals(
                candidate,
                propertyName,
                StringComparison.OrdinalIgnoreCase));
    return actualName is null
        ? null
        : parent[actualName] as JsonObject;
  }
}
