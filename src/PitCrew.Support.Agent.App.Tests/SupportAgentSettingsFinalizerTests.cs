using System.Text.Json;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportAgentSettingsFinalizerTests
{
  [Test]
  public async Task Finalize_Removes_Only_Enrollment_Bootstrap()
  {
    var root = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-finalization-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var settingsPath = Path.Combine(
        root,
        "appsettings.json");
    try
    {
      await File.WriteAllTextAsync(
          settingsPath,
          """
          {
            "pitCrewSupport": {
              "agent": {
                "identityRoot": "identity",
                "replayRoot": "replay",
                "pipeName": "pipe",
                "dashboardUrl": "http://localhost:5000/",
                "tenantId": "local",
                "displayName": "Canary",
                "enrollmentCode": "one-time-value"
              }
            },
            "Unrelated": {
              "Value": "preserved"
            }
          }
          """);

      var first = SupportAgentSettingsFinalizer.Finalize(root);
      var second = SupportAgentSettingsFinalizer.Finalize(root);
      using var document = JsonDocument.Parse(
          await File.ReadAllTextAsync(settingsPath));
      var agent = document.RootElement
          .GetProperty("pitCrewSupport")
          .GetProperty("agent");

      await Assert.That(first)
          .IsEqualTo(
              SupportEnrollmentFinalizationStatus.Succeeded);
      await Assert.That(second)
          .IsEqualTo(
              SupportEnrollmentFinalizationStatus.AlreadyFinalized);
      await Assert.That(agent.TryGetProperty("dashboardUrl", out _))
          .IsFalse()
          .Because("the stored identity owns the Dashboard origin after enrollment");
      await Assert.That(agent.TryGetProperty("enrollmentCode", out _))
          .IsFalse()
          .Because("one-time authorization cannot remain in runtime settings");
      await Assert.That(agent.GetProperty("identityRoot").GetString())
          .IsEqualTo("identity");
      await Assert.That(
              document.RootElement
                  .GetProperty("Unrelated")
                  .GetProperty("Value")
                  .GetString())
          .IsEqualTo("preserved");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Test]
  public async Task Finalize_Rejects_Malformed_Settings()
  {
    var root = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-finalization-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      await File.WriteAllTextAsync(
          Path.Combine(root, "appsettings.json"),
          "{");

      var result = SupportAgentSettingsFinalizer.Finalize(root);

      await Assert.That(result)
          .IsEqualTo(
              SupportEnrollmentFinalizationStatus.SettingsInvalid);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Test]
  public async Task Finalize_With_Backup_Rolls_Back_Exact_Bytes()
  {
    var root = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-finalization-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var settingsPath = Path.Combine(root, "appsettings.json");
    var backupPath = Path.Combine(
        root,
        SupportAgentSettingsFinalizer.BackupFileName);
    var original = """
        {
          "PitCrewSupport": {
            "Agent": {
              "IdentityRoot": "identity",
              "DashboardUrl": "http://localhost:5000/",
              "TenantId": "local",
              "DisplayName": "Canary",
              "EnrollmentCode": "one-time-value"
            }
          }
        }
        """u8.ToArray();
    try
    {
      await File.WriteAllBytesAsync(settingsPath, original);

      var finalized =
          SupportAgentSettingsFinalizer.FinalizeWithBackup(root);
      var backup = await File.ReadAllBytesAsync(backupPath);
      var rolledBack = SupportAgentSettingsFinalizer.Rollback(root);
      var restored = await File.ReadAllBytesAsync(settingsPath);

      await Assert.That(finalized)
          .IsEqualTo(SupportEnrollmentFinalizationStatus.Succeeded);
      await Assert.That(backup).IsEquivalentTo(original);
      await Assert.That(rolledBack)
          .IsEqualTo(SupportEnrollmentRollbackStatus.Succeeded);
      await Assert.That(restored).IsEquivalentTo(original);
      await Assert.That(File.Exists(backupPath)).IsFalse();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
