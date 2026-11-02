using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using PitCrew.Dashboard.Features.Fleet;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

[NotInParallel]
public sealed class ProfileStatusHostingTests
{
  [Test]
  public async Task Diagnostic_Status_Distinguishes_Scale_To_Zero_From_Absence(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var administrator = factory.CreateClient();
      using var diagnostics = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          administrator,
          cancellationToken);
      var code = await DashboardTestHelpers.CreateEnrollmentCodeAsync(
          administrator,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "Profile status node",
          cancellationToken);
      var node = await DashboardTestHelpers.EnrollAsync(
          administrator,
          "profile-status-node",
          "Profile Status Node",
          code.Code,
          cancellationToken);
      var baseline = DashboardTestHelpers.CreateObservedState(
          "default",
          "https://github.com/example/project");
      var observedState = baseline with
      {
        DesiredSlots = 0,
        ActiveSlots = 0,
        Slots = [],
        ConfiguredSlots = 4,
        EligibleSlots = 0,
        Autoscaling = new ManagerAutoscalingState(
            "scale-set",
            "running",
            0,
            4,
            0,
            0,
            0,
            0,
            0,
            0,
            30,
            1,
            null,
            null),
      };
      await DashboardTestHelpers.SynchronizeAsync(
          administrator,
          node.Credential,
          "2.0.0",
          observedState,
          cancellationToken);
      var credential =
          await DashboardTestHelpers.CreateDiagnosticCredentialAsync(
              administrator,
              session.AntiforgeryToken,
              DashboardTestHelpers.TenantId,
              "Profile status",
              DateTimeOffset.UtcNow.AddHours(1),
              [node.NodeId],
              [],
              cancellationToken);

      using var configured =
          await DashboardTestHelpers.SendDiagnosticAsync(
              diagnostics,
              HttpMethod.Get,
              $"/api/diagnostics/v1/tenants/{DashboardTestHelpers.TenantId}/fleet/nodes/{node.NodeId:D}/profiles/default/status",
              credential.Value,
              null,
              cancellationToken);
      var configuredStatus =
          await configured.Content.ReadFromJsonAsync<
              ProfileOperationalStatusResponse>(
                  cancellationToken);
      using var absent =
          await DashboardTestHelpers.SendDiagnosticAsync(
              diagnostics,
              HttpMethod.Get,
              $"/api/diagnostics/v1/tenants/{DashboardTestHelpers.TenantId}/fleet/nodes/{node.NodeId:D}/profiles/missing-profile/status",
              credential.Value,
              null,
              cancellationToken);
      var absentStatus =
          await absent.Content.ReadFromJsonAsync<
              ProfileOperationalStatusResponse>(
                  cancellationToken);

      await Assert.That(configured.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(configuredStatus).IsNotNull();
      await Assert.That(configuredStatus!.ConfigurationState)
          .IsEqualTo("configured");
      await Assert.That(configuredStatus.RuntimeState)
          .IsEqualTo("idle-scale-to-zero");
      await Assert.That(configuredStatus.Summary)
          .IsEqualTo(
              "CONFIGURED · AUTOSCALED · IDLE (SCALE-TO-ZERO)");
      await Assert.That(absent.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(absentStatus).IsNotNull();
      await Assert.That(absentStatus!.ConfigurationState)
          .IsEqualTo("not-configured");
      await Assert.That(absentStatus.RuntimeState).IsNull();
      await Assert.That(absentStatus.InventoryAuthoritative).IsTrue()
          .Because("the synchronized profile inventory is complete");
      await Assert.That(configured.Headers.CacheControl?.NoStore)
          .IsTrue();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }
}
