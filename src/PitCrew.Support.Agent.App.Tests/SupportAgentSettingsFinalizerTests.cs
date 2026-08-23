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
            "PitCrewSupport": {
              "Agent": {
                "IdentityRoot": "identity",
                "ReplayRoot": "replay",
                "PipeName": "pipe",
                "DashboardUrl": "http://localhost:5000/",
                "TenantId": "local",
                "DisplayName": "Canary",
                "EnrollmentCode": "one-time-value"
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
          .GetProperty("PitCrewSupport")
          .GetProperty("Agent");

      await Assert.That(first)
          .IsEqualTo(
              SupportEnrollmentFinalizationStatus.Succeeded);
      await Assert.That(second)
          .IsEqualTo(
              SupportEnrollmentFinalizationStatus.AlreadyFinalized);
      await Assert.That(agent.TryGetProperty("DashboardUrl", out _))
          .IsFalse()
          .Because("the stored identity owns the Dashboard origin after enrollment");
      await Assert.That(agent.TryGetProperty("EnrollmentCode", out _))
          .IsFalse()
          .Because("one-time authorization cannot remain in runtime settings");
      await Assert.That(agent.GetProperty("IdentityRoot").GetString())
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
}
