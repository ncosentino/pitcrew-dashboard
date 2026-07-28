using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Fleet;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

[NotInParallel]
public sealed class HostingTests
{
  [Test]
  public async Task Administrator_Views_And_Acknowledges_Active_Incident(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var now = DateTimeOffset.UtcNow;
      var store = factory.Services.GetRequiredService<IAlertIncidentStore>();
      await store.ReconcileAsync(
          [
              new AlertCandidate(
                  "api-test-incident",
                  DashboardTestHelpers.TenantId,
                  Guid.NewGuid(),
                  "default",
                  "test-alert",
                  "warning",
                  now,
                  TimeSpan.Zero,
                  "Test operational incident",
                  "Test incident summary.",
                  "test-reason",
                  null,
                  $"/tenants/{DashboardTestHelpers.TenantId}/fleet"),
          ],
          [],
          now,
          now.AddDays(-90),
          100,
          cancellationToken);

      var incidents =
          await client.GetFromJsonAsync<AlertIncidentListResponse>(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/fleet/v1/incidents?status=active",
              cancellationToken);
      await Assert.That(incidents).IsNotNull();
      await Assert.That(incidents!.Incidents).HasSingleItem();
      var incident = incidents.Incidents[0];

      using var acknowledgement =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/fleet/v1/incidents/{incident.IncidentId:D}/acknowledge",
              session.AntiforgeryToken,
              null,
              cancellationToken);
      var acknowledged =
          await client.GetFromJsonAsync<AlertIncidentListResponse>(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/fleet/v1/incidents?status=active",
              cancellationToken);

      await Assert.That(acknowledgement.StatusCode)
          .IsEqualTo(HttpStatusCode.NoContent);
      await Assert.That(acknowledged).IsNotNull();
      await Assert.That(acknowledged!.Incidents).HasSingleItem();
      await Assert.That(acknowledged.Incidents[0].Status)
          .IsEqualTo("acknowledged");
      await Assert.That(
          acknowledged.Incidents[0].AcknowledgedByGitHubUserId)
          .IsEqualTo(session.User.GitHubUserId);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Tenant_Owner_Renames_Display_Name_Without_Changing_Id(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      using var request = new HttpRequestMessage(
          HttpMethod.Put,
          $"/api/tenants/{DashboardTestHelpers.TenantId}")
      {
        Content = JsonContent.Create(
            new RenameTenantRequest("  Renamed tenant  ")),
      };
      request.Headers.Add(
          DashboardTestHelpers.AntiforgeryHeader,
          session.AntiforgeryToken);

      using var response = await client.SendAsync(
          request,
          cancellationToken);
      var renamedSession = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.NoContent);
      await Assert.That(renamedSession.Tenants).HasSingleItem();
      await Assert.That(renamedSession.Tenants[0].TenantId)
          .IsEqualTo(DashboardTestHelpers.TenantId);
      await Assert.That(renamedSession.Tenants[0].DisplayName)
          .IsEqualTo("Renamed tenant");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Session_Uses_Client_Compatible_GitHub_Property_Names(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      using var response = await client.GetAsync(
          "/api/session",
          cancellationToken);
      await using var stream = await response.Content.ReadAsStreamAsync(
          cancellationToken);
      using var document = await JsonDocument.ParseAsync(
          stream,
          cancellationToken: cancellationToken);
      var user = document.RootElement.GetProperty("user");
      var hasIncorrectUserIdProperty = user.TryGetProperty(
          "gitHubUserId",
          out _);
      var hasIncorrectLoginProperty = user.TryGetProperty(
          "gitHubLogin",
          out _);

      response.EnsureSuccessStatusCode();
      await Assert.That(user.GetProperty("githubUserId").GetString())
          .IsEqualTo("0");
      await Assert.That(user.GetProperty("githubLogin").GetString())
          .IsEqualTo("local-operator");
      await Assert.That(hasIncorrectUserIdProperty)
          .IsFalse()
          .Because("the React session contract requires githubUserId");
      await Assert.That(hasIncorrectLoginProperty)
          .IsFalse()
          .Because("the React session contract requires githubLogin");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Authenticated_Tenant_Enrolls_And_Views_Connector(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var code = await DashboardTestHelpers.CreateEnrollmentCodeAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "Build server",
          cancellationToken);
      var identity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-instance",
          "Build Server",
          code.Code,
          cancellationToken);
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          identity.Credential,
          "2.0.0",
          DashboardTestHelpers.CreateObservedState(
              "default",
              "https://github.com/example/project"),
          cancellationToken);

      var fleet = await client.GetFromJsonAsync<FleetResponse>(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/fleet/v1/nodes",
          cancellationToken);

      await Assert.That(session.User.GitHubLogin)
          .IsEqualTo("local-operator");
      await Assert.That(session.Tenants).HasSingleItem();
      await Assert.That(fleet).IsNotNull();
      await Assert.That(fleet!.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].DisplayName)
          .IsEqualTo("Build Server");
      await Assert.That(fleet.Nodes[0].IsRevoked).IsFalse();
      await Assert.That(fleet.Nodes[0].Profiles).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots)
          .HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles[0].EligibleSlots)
          .IsEqualTo(1);
      await Assert.That(
              fleet.Nodes[0].Profiles[0].Slots[0].RegistrationStatus)
          .IsEqualTo("connected");
      await Assert.That(
              fleet.Nodes[0].Profiles[0].ResourceTelemetry?.Host)
          .IsEqualTo(new HostResourceCapacity(
              8,
              34_359_738_368));
      await Assert.That(
              fleet.Nodes[0].Profiles[0].ResourceTelemetry?.Manager)
          .IsEqualTo(new ResourceUsage(
              0.5,
              201_326_592,
              11));
      await Assert.That(
              fleet.Nodes[0].Profiles[0].Slots[0].Resources)
          .IsEqualTo(new ResourceUsage(
              1.25,
              1_073_741_824,
              48));
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Administrator_Queues_And_Observes_Capacity_Command(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
            databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var code = await DashboardTestHelpers.CreateEnrollmentCodeAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "Capacity server",
          cancellationToken);
      var identity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-capacity",
          "Capacity Server",
          code.Code,
          cancellationToken);
      var observed = DashboardTestHelpers.CreateObservedState(
          "default",
          "https://github.com/example/project");
      var capability = new CapacityOperatorCapability(
          [
              new CapacityOperatorProfile(
                    "default",
                    3,
                    1,
                    50),
            ]);
      await DashboardTestHelpers.SynchronizeCapacityAsync(
          client,
          identity.Credential,
          "3.0.0",
          observed,
          capability,
          null,
          cancellationToken);
      using var queue = await DashboardTestHelpers.PostAuthenticatedAsync(
          client,
          $"/api/tenants/local/fleet/v1/nodes/{identity.NodeId:D}/profiles/default/capacity-maximum",
          session.AntiforgeryToken,
          new SetCapacityMaximumRequest(10),
          cancellationToken);
      await Assert.That(queue.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);

      var delivery = await DashboardTestHelpers.SynchronizeCapacityAsync(
          client,
          identity.Credential,
          "3.0.0",
          observed,
          capability,
          null,
          cancellationToken);
      await Assert.That(delivery.CapacityCommand).IsNotNull();
      await Assert.That(delivery.CapacityCommand!.Maximum)
          .IsEqualTo(10);

      var updatedObserved = observed with
      {
        Generation = 4,
        DesiredSlots = 10,
        ConfiguredSlots = 10,
      };
      await DashboardTestHelpers.SynchronizeCapacityAsync(
          client,
          identity.Credential,
          "3.0.0",
          updatedObserved,
          new CapacityOperatorCapability(
              [
                  new CapacityOperatorProfile(
                        "default",
                        4,
                        10,
                        50),
              ]),
          new CapacityCommandOutcome(
              delivery.CapacityCommand.CommandId,
              "succeeded",
              "Capacity maximum was acknowledged.",
              4,
              updatedObserved.ObservedAt.AddSeconds(1)),
          cancellationToken);

      var fleet = await client.GetFromJsonAsync<FleetResponse>(
          "/api/tenants/local/fleet/v1/nodes",
          cancellationToken);
      await Assert.That(fleet).IsNotNull();
      await Assert.That(fleet!.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].CapacityControls).HasSingleItem();
      await Assert.That(
              fleet.Nodes[0].CapacityControls[0].CurrentMaximum)
          .IsEqualTo(10);
      await Assert.That(
              fleet.Nodes[0].CapacityControls[0].LatestCommand?.Status)
          .IsEqualTo("succeeded");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Administrator_Queues_And_Completes_Manager_Recovery(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
            databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var code = await DashboardTestHelpers.CreateEnrollmentCodeAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "Recovery server",
          cancellationToken);
      var identity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-recovery",
          "Recovery Server",
          code.Code,
          cancellationToken);
      var observed = DashboardTestHelpers.CreateObservedState(
          "default",
          "https://github.com/example/project");
      var capability = new RecoveryOperatorCapability(
          [
              new RecoveryOperatorProfile(
                    "default",
                    11,
                    true,
                    observed.ManagerInstanceId,
                    observed.Generation,
                    observed.DesiredStateHash,
                    5,
                    true,
                    true,
                    false,
                    600,
                    1800),
            ]);
      await DashboardTestHelpers.SynchronizeRecoveryAsync(
          client,
          identity.Credential,
          "3.0.0",
          observed,
          capability,
          null,
          null,
          cancellationToken);

      var recoveryPath =
          $"/api/tenants/local/fleet/v1/nodes/{identity.NodeId:D}/profiles/default/manager-recovery";
      using var staleFence =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              recoveryPath,
              session.AntiforgeryToken,
              new RecoverManagerRequest(
                  "a-different-manager-instance",
                  observed.Generation,
                  observed.DesiredStateHash),
              cancellationToken);
      await Assert.That(staleFence.StatusCode)
          .IsEqualTo(HttpStatusCode.Conflict);

      using var queued = await DashboardTestHelpers.PostAuthenticatedAsync(
          client,
          recoveryPath,
          session.AntiforgeryToken,
          new RecoverManagerRequest(
              observed.ManagerInstanceId,
              observed.Generation,
              observed.DesiredStateHash),
          cancellationToken);
      await Assert.That(queued.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);

      using var overlapping =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              recoveryPath,
              session.AntiforgeryToken,
              new RecoverManagerRequest(
                  observed.ManagerInstanceId,
                  observed.Generation,
                  observed.DesiredStateHash),
              cancellationToken);
      await Assert.That(overlapping.StatusCode)
          .IsEqualTo(HttpStatusCode.Conflict);

      using var overlappingCapacity =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/local/fleet/v1/nodes/{identity.NodeId:D}/profiles/default/capacity-maximum",
              session.AntiforgeryToken,
              new SetCapacityMaximumRequest(10),
              cancellationToken);
      await Assert.That(overlappingCapacity.StatusCode)
          .IsEqualTo(HttpStatusCode.Conflict);

      var delivery = await DashboardTestHelpers.SynchronizeRecoveryAsync(
          client,
          identity.Credential,
          "3.0.0",
          observed,
          capability,
          null,
          null,
          cancellationToken);
      await Assert.That(delivery.RecoveryCommand).IsNotNull();
      await Assert.That(delivery.RecoveryCommand!.ExpectedManagerInstanceId)
          .IsEqualTo(observed.ManagerInstanceId);

      var afterClaim = await DashboardTestHelpers.SynchronizeRecoveryAsync(
          client,
          identity.Credential,
          "3.0.0",
          observed,
          capability,
          new RecoveryCommandProgress(
              delivery.RecoveryCommand.CommandId,
              "started",
              DateTimeOffset.UtcNow),
          null,
          cancellationToken);
      await Assert.That(afterClaim.RecoveryCommand)
          .IsNull()
          .Because("a started command is never offered again");

      var recovered = observed with
      {
        ManagerInstanceId = Guid.NewGuid().ToString("D"),
      };
      await DashboardTestHelpers.SynchronizeRecoveryAsync(
          client,
          identity.Credential,
          "3.0.0",
          recovered,
          capability,
          null,
          new RecoveryCommandOutcome(
              delivery.RecoveryCommand.CommandId,
              "succeeded",
              null,
              "Manager was restarted.",
              observed.ManagerInstanceId,
              recovered.ManagerInstanceId,
              DateTimeOffset.UtcNow),
          cancellationToken);

      var fleet = await client.GetFromJsonAsync<FleetResponse>(
          "/api/tenants/local/fleet/v1/nodes",
          cancellationToken);
      await Assert.That(fleet).IsNotNull();
      await Assert.That(fleet!.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].RecoveryControls).HasSingleItem();
      await Assert.That(
              fleet.Nodes[0].RecoveryControls[0].LatestCommand?.Status)
          .IsEqualTo("succeeded");
      await Assert.That(
              fleet.Nodes[0].RecoveryControls[0].LatestCommand?
                  .AfterManagerInstanceId)
          .IsEqualTo(recovered.ManagerInstanceId);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Administrator_Renames_Revoked_Node_Without_Changing_Identity(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var code = await DashboardTestHelpers.CreateEnrollmentCodeAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "Rename",
          cancellationToken);
      var identity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-rename",
          "Original server",
          code.Code,
          cancellationToken);
      using (var revoke = await
          DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/local/fleet/v1/nodes/{identity.NodeId:D}/revoke",
              session.AntiforgeryToken,
              null,
              cancellationToken))
      {
        revoke.EnsureSuccessStatusCode();
      }
      using var request = new HttpRequestMessage(
          HttpMethod.Put,
          $"/api/tenants/local/fleet/v1/nodes/{identity.NodeId:D}")
      {
        Content = JsonContent.Create(
            new RenameNodeRequest("  Renamed server  ")),
      };
      request.Headers.Add(
          DashboardTestHelpers.AntiforgeryHeader,
          session.AntiforgeryToken);

      using var response = await client.SendAsync(
          request,
          cancellationToken);
      var fleet = await client.GetFromJsonAsync<FleetResponse>(
          "/api/tenants/local/fleet/v1/nodes",
          cancellationToken);

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.NoContent);
      await Assert.That(fleet).IsNotNull();
      await Assert.That(fleet!.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].NodeId)
          .IsEqualTo(identity.NodeId);
      await Assert.That(fleet.Nodes[0].DisplayName)
          .IsEqualTo("Renamed server");
      await Assert.That(fleet.Nodes[0].IsRevoked)
          .IsTrue();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Enrollment_Code_Is_Consumed_Once(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var code = await DashboardTestHelpers.CreateEnrollmentCodeAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "Single use",
          cancellationToken);
      await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-one",
          "Server One",
          code.Code,
          cancellationToken);

      using var secondEnrollment = new HttpRequestMessage(
          HttpMethod.Post,
          "/api/connectors/v1/enroll")
      {
        Content = JsonContent.Create(
            new ConnectorEnrollmentRequest(
                "connector-two",
                "Server Two")),
      };
      secondEnrollment.Headers.Add(
          "X-PitCrew-Enrollment-Code",
          code.Code);
      using var response = await client.SendAsync(
          secondEnrollment,
          cancellationToken);

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Tenant_Routes_Isolate_Overlapping_Profile_Names(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      using (var createTenant = await
          DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              "/api/tenants",
              session.AntiforgeryToken,
              new CreateTenantRequest(
                  "secondary",
                  "Secondary"),
              cancellationToken))
      {
        createTenant.EnsureSuccessStatusCode();
      }
      var localCode = await
          DashboardTestHelpers.CreateEnrollmentCodeAsync(
              client,
              session.AntiforgeryToken,
              DashboardTestHelpers.TenantId,
              "Local",
              cancellationToken);
      var secondaryCode = await
          DashboardTestHelpers.CreateEnrollmentCodeAsync(
              client,
              session.AntiforgeryToken,
              "secondary",
              "Secondary",
              cancellationToken);
      var localIdentity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-local",
          "Local Server",
          localCode.Code,
          cancellationToken);
      var secondaryIdentity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-secondary",
          "Secondary Server",
          secondaryCode.Code,
          cancellationToken);
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          localIdentity.Credential,
          "2.0.0",
          DashboardTestHelpers.CreateObservedState(
              "default",
              "https://github.com/example/local"),
          cancellationToken);
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          secondaryIdentity.Credential,
          "2.0.0",
          DashboardTestHelpers.CreateObservedState(
              "default",
              "https://github.com/example/secondary"),
          cancellationToken);

      var localFleet = await client.GetFromJsonAsync<FleetResponse>(
          "/api/tenants/local/fleet/v1/nodes",
          cancellationToken);
      var secondaryFleet =
          await client.GetFromJsonAsync<FleetResponse>(
              "/api/tenants/secondary/fleet/v1/nodes",
              cancellationToken);

      await Assert.That(localFleet).IsNotNull();
      await Assert.That(secondaryFleet).IsNotNull();
      await Assert.That(localFleet!.Nodes).HasSingleItem();
      await Assert.That(secondaryFleet!.Nodes).HasSingleItem();
      await Assert.That(localFleet.Nodes[0].Profiles[0].ProfileId)
          .IsEqualTo("default");
      await Assert.That(secondaryFleet.Nodes[0].Profiles[0].ProfileId)
          .IsEqualTo("default");
      await Assert.That(
              localFleet.Nodes[0].Profiles[0].Slots[0].Repository)
          .IsEqualTo("https://github.com/example/local");
      await Assert.That(
              secondaryFleet.Nodes[0].Profiles[0].Slots[0].Repository)
          .IsEqualTo("https://github.com/example/secondary");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Credential_Rotation_Promotes_Acknowledged_Replacement(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var code = await DashboardTestHelpers.CreateEnrollmentCodeAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "Rotation",
          cancellationToken);
      var identity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-rotation",
          "Rotation Server",
          code.Code,
          cancellationToken);
      var state = DashboardTestHelpers.CreateObservedState(
          "default",
          "https://github.com/example/project");
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          identity.Credential,
          "2.0.0",
          state,
          cancellationToken);

      using (var rotationRequest = await
          DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/local/fleet/v1/nodes/{identity.NodeId:D}/credential-rotation",
              session.AntiforgeryToken,
              null,
              cancellationToken))
      {
        rotationRequest.EnsureSuccessStatusCode();
      }
      var rotation = await DashboardTestHelpers.SynchronizeAsync(
          client,
          identity.Credential,
          "2.0.0",
          state,
          cancellationToken);
      await Assert.That(rotation.CredentialRotation).IsNotNull();
      var replacement = rotation.CredentialRotation!.Credential;
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          replacement,
          "2.0.0",
          state,
          cancellationToken);

      using var oldCredentialResponse =
          await DashboardTestHelpers.SendSynchronizationAsync(
              client,
              identity.Credential,
              "2.0.0",
              state,
              cancellationToken);
      await Assert.That(oldCredentialResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Revoked_Connector_Reenrolls_With_Same_Node_Identity(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var firstCode =
          await DashboardTestHelpers.CreateEnrollmentCodeAsync(
              client,
              session.AntiforgeryToken,
              DashboardTestHelpers.TenantId,
              "Initial",
              cancellationToken);
      var firstIdentity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-reenrollment",
          "Re-enrollment Server",
          firstCode.Code,
          cancellationToken);
      var state = DashboardTestHelpers.CreateObservedState(
          "default",
          "https://github.com/example/project");
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          firstIdentity.Credential,
          "2.0.0",
          state,
          cancellationToken);
      using (var revoke = await
          DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/local/fleet/v1/nodes/{firstIdentity.NodeId:D}/revoke",
              session.AntiforgeryToken,
              null,
              cancellationToken))
      {
        revoke.EnsureSuccessStatusCode();
      }
      using (var revokedSync =
          await DashboardTestHelpers.SendSynchronizationAsync(
              client,
              firstIdentity.Credential,
              "2.0.0",
              state,
              cancellationToken))
      {
        await Assert.That(revokedSync.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
      }

      var replacementCode =
          await DashboardTestHelpers.CreateEnrollmentCodeAsync(
              client,
              session.AntiforgeryToken,
              DashboardTestHelpers.TenantId,
              "Re-enrollment",
              cancellationToken);
      var replacementIdentity =
          await DashboardTestHelpers.EnrollAsync(
              client,
              "connector-reenrollment",
              "Re-enrollment Server",
              replacementCode.Code,
              cancellationToken);
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          replacementIdentity.Credential,
          "2.0.0",
          state,
          cancellationToken);

      await Assert.That(replacementIdentity.NodeId)
          .IsEqualTo(firstIdentity.NodeId);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task GitHub_Mode_Rejects_Unauthenticated_Human_Apis(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          "123");
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient(
          new WebApplicationFactoryClientOptions
          {
            AllowAutoRedirect = false,
          });

      using var sessionResponse = await client.GetAsync(
          "/api/session",
          cancellationToken);
      using var fleetResponse = await client.GetAsync(
          "/api/tenants/local/fleet/v1/nodes",
          cancellationToken);
      using var recoveryResponse = await client.PostAsJsonAsync(
          $"/api/tenants/local/fleet/v1/nodes/{Guid.NewGuid():D}/profiles/default/manager-recovery",
          new RecoverManagerRequest(
              "manager-instance",
              1,
              null),
          cancellationToken);

      await Assert.That(sessionResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
      await Assert.That(fleetResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
      await Assert.That(recoveryResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Nested_Spa_Route_Returns_Index_Without_Shadowing_Apis(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      using var spa = new SpaTestContent();
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();

      using var nestedResponse = await client.GetAsync(
          "/tenants/local/nodes/node-1",
          cancellationToken);
      var nestedBody = await nestedResponse.Content.ReadAsStringAsync(
          cancellationToken);
      using var apiResponse = await client.GetAsync(
          "/api/session",
          cancellationToken);

      nestedResponse.EnsureSuccessStatusCode();
      apiResponse.EnsureSuccessStatusCode();
      await Assert.That(nestedBody)
          .IsEqualTo("<!doctype html><title>PitCrew SPA test</title>");
      await Assert.That(apiResponse.Content.Headers.ContentType?.MediaType)
          .IsEqualTo("application/json");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Missing_Static_Asset_Returns_NotFound(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      using var spa = new SpaTestContent();
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();

      using var response = await client.GetAsync(
          "/assets/missing.js",
          cancellationToken);

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.NotFound);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  [Arguments(
      "/tenants/local/nodes/node-1?tab=activity",
      "/tenants/local/nodes/node-1?tab=activity")]
  [Arguments("/tenants/local/fleet#capacity", "/tenants/local/fleet#capacity")]
  [Arguments("https://example.test/steal", "/")]
  [Arguments("//example.test/steal", "/")]
  [Arguments("/\\example.test/steal", "/")]
  public async Task Login_Uses_Only_Local_Return_Urls(
      string returnUrl,
      string expectedLocation,
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient(
          new WebApplicationFactoryClientOptions
          {
            AllowAutoRedirect = false,
          });
      var requestPath = $"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}";

      using var response = await client.GetAsync(
          requestPath,
          cancellationToken);

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.Redirect);
      await Assert.That(response.Headers.Location?.OriginalString)
          .IsEqualTo(expectedLocation);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Authenticated_Mutation_Requires_Antiforgery_Token(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      using var response = await client.PostAsJsonAsync(
          "/api/tenants",
          new CreateTenantRequest(
              "missing-token",
              "Missing token"),
          cancellationToken);

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Hosted_Ingress_Contract_Is_Anonymous_And_Versioned(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          "123");
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient(
          new WebApplicationFactoryClientOptions
          {
            AllowAutoRedirect = false,
          });
      using var response = await client.GetAsync(
          "/health/hosted-ingress/v1",
          cancellationToken);
      var responseBody = await response.Content.ReadAsStringAsync(
          cancellationToken);

      response.EnsureSuccessStatusCode();
      await Assert.That(response.Content.Headers.ContentType?.MediaType)
          .IsEqualTo("text/plain");
      await Assert.That(responseBody)
          .IsEqualTo("pitcrew-dashboard-hosted-ingress-v1");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Responses_Include_Provider_Neutral_Security_Headers(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      using var response = await client.GetAsync(
          "/api/session",
          cancellationToken);

      response.EnsureSuccessStatusCode();
      await Assert.That(
              response.Headers.GetValues(
                  "Content-Security-Policy").Single())
          .Contains("frame-ancestors 'none'");
      await Assert.That(
              response.Headers.GetValues(
                  "Permissions-Policy").Single())
          .IsEqualTo(
              "camera=(), geolocation=(), microphone=()");
      await Assert.That(
              response.Headers.GetValues(
                  "Referrer-Policy").Single())
          .IsEqualTo("no-referrer");
      await Assert.That(
              response.Headers.GetValues(
                  "X-Content-Type-Options").Single())
          .IsEqualTo("nosniff");
      await Assert.That(
              response.Headers.GetValues(
                  "X-Frame-Options").Single())
          .IsEqualTo("DENY");
      await Assert.That(
              response.Headers.Contains(
                  "Strict-Transport-Security"))
          .IsFalse();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Production_Https_Responses_Include_Hsts(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          "123",
          "Production");
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient(
          new WebApplicationFactoryClientOptions
          {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(
                "https://pitcrew.example.com",
                UriKind.Absolute),
          });
      using var response = await client.GetAsync(
          "/api/session",
          cancellationToken);

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
      await Assert.That(
              response.Headers.GetValues(
                  "Strict-Transport-Security").Single())
          .IsEqualTo("max-age=31536000");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }
}
