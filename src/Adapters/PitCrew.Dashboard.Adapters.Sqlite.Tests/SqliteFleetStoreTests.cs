using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteFleetStoreTests
{
  [Test]
  public async Task Capacity_Command_Queues_Delivers_And_Completes_Idempotently(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-capacity-store-{Guid.NewGuid():N}.db");
    try
    {
      var now = new DateTimeOffset(
          2026,
          7,
          24,
          12,
          0,
          0,
          TimeSpan.Zero);
      var (connectionFactory, _, nodeId) =
          await CreateEnrolledStoreAsync(
              databasePath,
              now,
              cancellationToken);
      var store = new SqliteCapacityCommandStore(connectionFactory);
      var initialCapability = new CapacityOperatorCapability(
          [
              new CapacityOperatorProfile(
                  "default",
                  7,
                  30,
                  50),
          ]);
      var initialClaim = await store.ApplyConnectorSyncAsync(
          nodeId,
          initialCapability,
          null,
          now,
          now.AddMinutes(-2),
          cancellationToken);
      await Assert.That(initialClaim).IsNull();

      var queued = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          40,
          "1",
          now.AddSeconds(1),
          now.AddMinutes(10),
          cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(CapacityCommandQueueStatus.Queued);
      await Assert.That(queued.CommandId).IsNotNull();

      var conflict = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          45,
          "1",
          now.AddSeconds(2),
          now.AddMinutes(10),
          cancellationToken);
      await Assert.That(conflict.Status)
          .IsEqualTo(CapacityCommandQueueStatus.Conflict);

      var delivered = await store.ApplyConnectorSyncAsync(
          nodeId,
          initialCapability,
          null,
          now.AddSeconds(3),
          now.AddMinutes(-2),
          cancellationToken);
      await Assert.That(delivered).IsNotNull();
      await Assert.That(delivered!.CommandId)
          .IsEqualTo(queued.CommandId!.Value);
      await Assert.That(delivered.ExpectedGeneration).IsEqualTo(7);
      await Assert.That(delivered.Maximum).IsEqualTo(40);

      var completed = await store.ApplyConnectorSyncAsync(
          nodeId,
          new CapacityOperatorCapability(
              [
                  new CapacityOperatorProfile(
                      "default",
                      8,
                      40,
                      50),
              ]),
          null,
          now.AddMinutes(3),
          now.AddMinutes(1),
          cancellationToken);
      await Assert.That(completed).IsNull();

      var controls = await store.GetControlsAsync(
          "tenant",
          cancellationToken);
      await Assert.That(controls).HasSingleItem();
      await Assert.That(controls[0].Profiles).HasSingleItem();
      await Assert.That(controls[0].Profiles[0].CurrentMaximum)
          .IsEqualTo(40);
      await Assert.That(controls[0].Profiles[0].LatestCommand)
          .IsNotNull();
      await Assert.That(
              controls[0].Profiles[0].LatestCommand!.Status)
          .IsEqualTo("succeeded");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Observed_State_Round_Trips_And_Legacy_Payload_Remains_Readable(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-fleet-{Guid.NewGuid():N}.db");
    try
    {
      var observedAt = new DateTimeOffset(
          2026,
          7,
          19,
          18,
          30,
          0,
          TimeSpan.Zero);
      var (connectionFactory, store, nodeId) =
          await CreateEnrolledStoreAsync(
              databasePath,
              observedAt,
              cancellationToken);
      var expectedSlotResources = new ResourceUsage(
          1.75,
          805_306_368,
          37);
      var expectedTelemetry = new ManagerResourceTelemetry(
          observedAt,
          "available",
          new HostResourceCapacity(
              16,
              68_719_476_736),
          new ResourceUsage(
              0.5,
              201_326_592,
              11));
      var expectedAutoscaling = new ManagerAutoscalingState(
          "scale-set",
          "running",
          0,
          30,
          1,
          2,
          1,
          1,
          0,
          1,
          300,
          1,
          observedAt.AddMinutes(5),
          null);
      var profile = new ManagerObservedState(
          1,
          10,
          "default",
          "manager-instance",
          "running",
          observedAt,
          "repo",
          1,
          new string('a', 64),
          "accepted",
          1,
          1,
          0,
          [
              new ObservedSlotState(
                  "repo-example-000001",
                  "https://github.com/example/project",
                  true,
                  true,
                  "online",
                  0,
                  0,
                  observedAt,
                  expectedSlotResources,
                  "busy",
                  "scale-set-linux",
                  "connected"),
          ],
          expectedTelemetry,
          30,
          expectedAutoscaling,
          1);
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "2.0.0",
          observedAt,
          [profile],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);

      var fleet = await store.GetFleetAsync(
          "tenant",
          observedAt,
          TimeSpan.FromMinutes(1),
          cancellationToken);

      await Assert.That(fleet.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles[0].ResourceTelemetry)
          .IsEqualTo(expectedTelemetry);
      await Assert.That(fleet.Nodes[0].Profiles[0].ConfiguredSlots)
          .IsEqualTo(30);
      await Assert.That(fleet.Nodes[0].Profiles[0].Autoscaling)
          .IsEqualTo(expectedAutoscaling);
      await Assert.That(fleet.Nodes[0].Profiles[0].EligibleSlots)
          .IsEqualTo(1);
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots[0].Resources)
          .IsEqualTo(expectedSlotResources);
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots[0].Activity)
          .IsEqualTo("busy");
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots[0].Target)
          .IsEqualTo("scale-set-linux");
      await Assert.That(
              fleet.Nodes[0].Profiles[0].Slots[0].RegistrationStatus)
          .IsEqualTo("connected");

      var legacyProfile = profile with
      {
        ObservedAt = observedAt.AddSeconds(30),
        ManagerContractVersion = 7,
        Slots =
        [
            profile.Slots[0] with
            {
              Resources = null,
              Activity = null,
              Target = null,
              RegistrationStatus = null,
            },
        ],
        ResourceTelemetry = null,
        ConfiguredSlots = null,
        Autoscaling = null,
        EligibleSlots = null,
      };
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "2.0.0",
          legacyProfile.ObservedAt,
          [legacyProfile],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      var legacyPayload = JsonNode.Parse(
          JsonSerializer.Serialize(
              legacyProfile,
              PitCrewProtocolJsonContext.Default.ManagerObservedState))?
          .AsObject() ??
          throw new InvalidOperationException(
              "The legacy profile could not be represented as JSON.");
      legacyPayload.Remove("resourceTelemetry");
      legacyPayload.Remove("configuredSlots");
      legacyPayload.Remove("autoscaling");
      legacyPayload.Remove("eligibleSlots");
      foreach (var slot in legacyPayload["slots"]!.AsArray())
      {
        var slotObject = slot!.AsObject();
        slotObject.Remove("resources");
        slotObject.Remove("activity");
        slotObject.Remove("target");
        slotObject.Remove("registrationStatus");
      }
      await using (var connection = await connectionFactory.OpenAsync(
          cancellationToken))
      await using (var command = connection.CreateCommand())
      {
        command.CommandText =
            """
            UPDATE profiles
            SET payload_json = $payload
            WHERE node_id = $nodeId
              AND profile_id = 'default';
            """;
        command.Parameters.AddWithValue(
            "$payload",
            legacyPayload.ToJsonString());
        command.Parameters.AddWithValue(
            "$nodeId",
            nodeId.ToString("D"));
        var updatedRows = await command.ExecuteNonQueryAsync(
            cancellationToken);
        await Assert.That(updatedRows).IsEqualTo(1);
      }

      var legacyFleet = await store.GetFleetAsync(
          "tenant",
          legacyProfile.ObservedAt,
          TimeSpan.FromMinutes(1),
          cancellationToken);

      await Assert.That(legacyFleet.Nodes).HasSingleItem();
      await Assert.That(legacyFleet.Nodes[0].Profiles).HasSingleItem();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].ResourceTelemetry)
          .IsNull();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].ConfiguredSlots)
          .IsNull();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].Autoscaling)
          .IsNull();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].EligibleSlots)
          .IsNull();
      await Assert.That(legacyFleet.Nodes[0].Profiles[0].Slots)
          .HasSingleItem();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].Slots[0].Resources)
          .IsNull();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].Slots[0].Activity)
          .IsNull();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].Slots[0].Target)
          .IsNull();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].Slots[0].RegistrationStatus)
          .IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Display_Name_Migration_Preserves_Existing_Node(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-fleet-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      var nodeId = Guid.NewGuid();
      await CreateVersionThreeDatabaseAsync(
          connectionFactory,
          nodeId,
          cancellationToken);

      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      var beforeRename = await store.GetFleetAsync(
          "tenant",
          DateTimeOffset.UtcNow,
          TimeSpan.FromMinutes(1),
          cancellationToken);
      var renamed = await store.RenameNodeAsync(
          "tenant",
          nodeId,
          "Operator name",
          cancellationToken);
      var afterRename = await store.GetFleetAsync(
          "tenant",
          DateTimeOffset.UtcNow,
          TimeSpan.FromMinutes(1),
          cancellationToken);

      await Assert.That(beforeRename.Nodes).HasSingleItem();
      await Assert.That(beforeRename.Nodes[0].NodeId)
          .IsEqualTo(nodeId);
      await Assert.That(beforeRename.Nodes[0].DisplayName)
          .IsEqualTo("Connector name");
      await Assert.That(renamed)
          .IsEqualTo(NodeMutationStatus.Succeeded);
      await Assert.That(afterRename.Nodes[0].DisplayName)
          .IsEqualTo("Operator name");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Node_Rename_Persists_Across_Revocation_And_Reenrollment(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-fleet-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var now = DateTimeOffset.UtcNow;
      var owner = new DashboardUser(
          "1",
          "owner",
          "Owner",
          null);
      await new SqliteAccessStore(connectionFactory)
          .EnsureTenantOwnerAsync(
              "tenant",
              "Tenant",
              owner,
              now,
              cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      await CreateEnrollmentCodeAsync(
          store,
          "tenant",
          owner.GitHubUserId,
          "code-one",
          now,
          cancellationToken);
      var firstEnrollment = await store.RedeemEnrollmentCodeAsync(
          "code-one",
          "connector-instance",
          "Connector name",
          "credential-one",
          now,
          cancellationToken);
      var nodeId = firstEnrollment.NodeId ??
          throw new InvalidOperationException(
              "Initial enrollment did not return a node ID.");

      var renamed = await store.RenameNodeAsync(
          "tenant",
          nodeId,
          "Operator name",
          cancellationToken);
      var revoked = await store.RevokeNodeAsync(
          "tenant",
          nodeId,
          now.AddMinutes(1),
          cancellationToken);
      var renamedWhileRevoked = await store.RenameNodeAsync(
          "tenant",
          nodeId,
          "Renamed while revoked",
          cancellationToken);
      await CreateEnrollmentCodeAsync(
          store,
          "tenant",
          owner.GitHubUserId,
          "code-two",
          now.AddMinutes(2),
          cancellationToken);
      var secondEnrollment = await store.RedeemEnrollmentCodeAsync(
          "code-two",
          "connector-instance",
          "Updated connector name",
          "credential-two",
          now.AddMinutes(2),
          cancellationToken);
      var fleet = await store.GetFleetAsync(
          "tenant",
          now.AddMinutes(2),
          TimeSpan.FromMinutes(1),
          cancellationToken);
      var wrongTenant = await store.RenameNodeAsync(
          "other",
          nodeId,
          "Wrong tenant",
          cancellationToken);

      await Assert.That(renamed)
          .IsEqualTo(NodeMutationStatus.Succeeded);
      await Assert.That(revoked)
          .IsEqualTo(NodeMutationStatus.Succeeded);
      await Assert.That(renamedWhileRevoked)
          .IsEqualTo(NodeMutationStatus.Succeeded);
      await Assert.That(wrongTenant)
          .IsEqualTo(NodeMutationStatus.NotFound);
      await Assert.That(secondEnrollment.NodeId)
          .IsEqualTo(nodeId);
      await Assert.That(fleet.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].NodeId)
          .IsEqualTo(nodeId);
      await Assert.That(fleet.Nodes[0].DisplayName)
          .IsEqualTo("Renamed while revoked");
      await Assert.That(fleet.Nodes[0].IsRevoked)
          .IsFalse();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Contract_Eleven_Observed_State_Round_Trips(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-fleet-{Guid.NewGuid():N}.db");
    try
    {
      var observedAt = new DateTimeOffset(
          2026,
          7,
          26,
          12,
          0,
          0,
          TimeSpan.Zero);
      var (connectionFactory, store, nodeId) = await CreateEnrolledStoreAsync(
          databasePath,
          observedAt,
          cancellationToken);
      var expectedPolicy = new WorkerResourcePolicy(
          8_589_934_592,
          10_737_418_240,
          "2.5",
          1024);
      var expectedStatistics = new ScaleSetStatistics(
          observedAt.AddMinutes(-1),
          0,
          0,
          1,
          1,
          8,
          1,
          7);
      var expectedTarget = new AutoscalingTargetState(
          "repo:example/project",
          "https://github.com/example/project",
          8,
          1,
          1,
          0,
          1,
          0,
          expectedStatistics);
      var expectedResources = new ResourceUsage(
          1.25,
          1_073_741_824,
          48,
          1_048_576,
          0,
          536_870_912,
          null);
      var expectedLastExit = new WorkerLastExitDiagnostic(
          observedAt.AddMinutes(-5),
          "oom-killed",
          137,
          9,
          true,
          "docker-inspect");
      const string expectedImageId =
          "sha256:1111111111111111111111111111111111111111111111111111111111111111";
      var profile = new ManagerObservedState(
          1,
          11,
          "default",
          "manager-instance",
          "running",
          observedAt,
          "repo",
          1,
          new string('a', 64),
          "accepted",
          1,
          1,
          0,
          [
              new ObservedSlotState(
                  "repo-example-000001",
                  "https://github.com/example/project",
                  true,
                  true,
                  "online",
                  0,
                  0,
                  observedAt,
                  expectedResources,
                  "busy",
                  "repo:example/project",
                  "connected",
                  expectedImageId,
                  expectedLastExit),
          ],
          new ManagerResourceTelemetry(
              observedAt,
              "available",
              new HostResourceCapacity(
                  16,
                  68_719_476_736),
              new ResourceUsage(
                  0.5,
                  201_326_592,
                  11)),
          8,
          new ManagerAutoscalingState(
              "scale-set",
              "running",
              0,
              8,
              1,
              1,
              1,
              0,
              0,
              1,
              120,
              1,
              null,
              null,
              6,
              [expectedTarget]),
          1,
          expectedPolicy);
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "2.0.0",
          observedAt,
          [profile],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);

      var fleet = await store.GetFleetAsync(
          "tenant",
          observedAt,
          TimeSpan.FromMinutes(1),
          cancellationToken);

      await Assert.That(fleet.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles).HasSingleItem();
      var storedProfile = fleet.Nodes[0].Profiles[0];
      await Assert.That(storedProfile.ResourcePolicy)
          .IsEqualTo(expectedPolicy);
      await Assert.That(storedProfile.Autoscaling?.MaximumActiveWorkers)
          .IsEqualTo(6);
      await Assert.That(storedProfile.Autoscaling?.Targets)
          .IsNotNull();
      await Assert.That(storedProfile.Autoscaling!.Targets!)
          .HasSingleItem();
      await Assert.That(storedProfile.Autoscaling.Targets![0])
          .IsEqualTo(expectedTarget);
      await Assert.That(storedProfile.Slots).HasSingleItem();
      await Assert.That(storedProfile.Slots[0].ImageId)
          .IsEqualTo(expectedImageId);
      await Assert.That(storedProfile.Slots[0].LastExit)
          .IsEqualTo(expectedLastExit);
      await Assert.That(storedProfile.Slots[0].Resources)
          .IsEqualTo(expectedResources);
      await Assert.That(storedProfile.Slots[0].Resources?.NetworkTxBytes)
          .IsEqualTo(0);
      await Assert.That(storedProfile.Slots[0].Resources?.BlockWriteBytes)
          .IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Contract_Twelve_Diagnostics_Round_Trip_Without_Duplicating_Events(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-fleet-{Guid.NewGuid():N}.db");
    try
    {
      var observedAt = new DateTimeOffset(
          2026,
          7,
          26,
          12,
          0,
          0,
          TimeSpan.Zero);
      var (connectionFactory, store, nodeId) = await CreateEnrolledStoreAsync(
          databasePath,
          observedAt,
          cancellationToken);
      var shutdownEvent = new ManagerEvent(
          40,
          "manager-instance-0",
          observedAt.AddMinutes(-2),
          "recovery",
          "manager-shutdown",
          null,
          "succeeded",
          0,
          null,
          null,
          null,
          "none",
          null);
      var failureEvent = new ManagerEvent(
          41,
          "manager-instance-1",
          observedAt.AddMinutes(-1),
          "docker",
          "docker-run",
          "repo-example-000001",
          "retry-scheduled",
          1200,
          3,
          2,
          observedAt.AddSeconds(30),
          "docker-failed",
          "Docker refused to start the worker container.");
      var expectedHealth = new ManagerSubsystemHealth(
          new SubsystemHealthSummary(
              "degraded",
              observedAt,
              2,
              observedAt.AddSeconds(30),
              new SubsystemOperationEvidence(
                  "docker-ping",
                  observedAt.AddMinutes(-5),
                  4,
                  "none",
                  null),
              new SubsystemOperationEvidence(
                  "docker-run",
                  observedAt.AddMinutes(-1),
                  1200,
                  "docker-failed",
                  "Docker refused to start the worker container.")),
          new SubsystemHealthSummary(
              "unknown",
              observedAt,
              0,
              null,
              null,
              null));
      var expectedFixedDeficit = new CapacityDeficitEvidence(
          observedAt,
          "current",
          1,
          0,
          0,
          0,
          0,
          null,
          1,
          null,
          "docker-failed",
          "Docker refused to start the worker container.");
      var profile = new ManagerObservedState(
          1,
          12,
          "default",
          "manager-instance-1",
          "running",
          observedAt,
          "repo",
          1,
          null,
          "accepted",
          1,
          0,
          0,
          [],
          null,
          1,
          null,
          0,
          null,
          new ManagerOperationJournal(
              "truncated",
              32,
              41,
              9,
              [shutdownEvent, failureEvent]),
          expectedHealth,
          new ManagerCapacityEvidence(
              expectedFixedDeficit,
              []));
      var credentialUpdate = new ConnectorCredentialUpdate(
          ConnectorCredentialUpdateKind.None,
          string.Empty);

      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "2.0.0",
          observedAt,
          [profile],
          credentialUpdate,
          cancellationToken);
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "2.0.0",
          observedAt.AddSeconds(5),
          [profile],
          credentialUpdate,
          cancellationToken);

      var afterHeartbeat = await store.GetFleetAsync(
          "tenant",
          observedAt.AddSeconds(5),
          TimeSpan.FromMinutes(1),
          cancellationToken);
      var heartbeatJournal = RequireJournal(afterHeartbeat);

      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "2.0.0",
          observedAt.AddMinutes(1),
          [
              profile with
              {
                ManagerInstanceId = "manager-instance-2",
                ObservedAt = observedAt.AddMinutes(1),
                OperationJournal = new ManagerOperationJournal(
                    "truncated",
                    32,
                    42,
                    9,
                    [
                        shutdownEvent,
                        failureEvent,
                        failureEvent with
                        {
                          Sequence = 42,
                          ManagerInstanceId = "manager-instance-2",
                          Operation = "journal-restore",
                          Subsystem = "recovery",
                          Outcome = "recovered",
                          RetryAt = null,
                          Reason = "recovered",
                          Evidence = null,
                        },
                    ]),
              },
          ],
          credentialUpdate,
          cancellationToken);

      var afterRestart = await store.GetFleetAsync(
          "tenant",
          observedAt.AddMinutes(1),
          TimeSpan.FromMinutes(1),
          cancellationToken);
      var restartJournal = RequireJournal(afterRestart);
      var isolated = await store.GetFleetAsync(
          "other",
          observedAt.AddMinutes(1),
          TimeSpan.FromMinutes(1),
          cancellationToken);

      await Assert.That(heartbeatJournal.Status).IsEqualTo("truncated");
      await Assert.That(heartbeatJournal.DroppedEvents).IsEqualTo(9);
      await Assert.That(heartbeatJournal.Capacity).IsEqualTo(32);
      await Assert.That(heartbeatJournal.HighestSequence).IsEqualTo(41);
      await Assert.That(heartbeatJournal.Events.Count).IsEqualTo(2);
      await Assert.That(heartbeatJournal.Events[0]).IsEqualTo(shutdownEvent);
      await Assert.That(heartbeatJournal.Events[1]).IsEqualTo(failureEvent);
      await Assert.That(restartJournal.Events.Count).IsEqualTo(3);
      await Assert.That(restartJournal.Events
              .Select(managerEvent => managerEvent.Sequence)
              .Distinct()
              .Count())
          .IsEqualTo(3)
          .Because("a manager restart continues durable sequences without duplicating events");
      await Assert.That(afterRestart.Nodes[0].Profiles[0].SubsystemHealth)
          .IsEqualTo(expectedHealth);
      await Assert.That(afterRestart.Nodes[0].Profiles[0].CapacityEvidence?.Fixed)
          .IsEqualTo(expectedFixedDeficit);
      await Assert.That(afterRestart.Nodes[0].Profiles[0].CapacityEvidence?.Targets)
          .IsEmpty();
      await Assert.That(isolated.Nodes).IsEmpty();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static ManagerOperationJournal RequireJournal(FleetResponse fleet) =>
      fleet.Nodes[0].Profiles[0].OperationJournal ??
      throw new InvalidOperationException(
          "The stored contract-12 projection must include the operation journal.");

  private static async Task CreateEnrollmentCodeAsync(
      SqliteFleetStore store,
      string tenantId,
      string ownerGitHubUserId,
      string codeHash,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken) =>
      await store.CreateEnrollmentCodeAsync(
          Guid.NewGuid(),
          tenantId,
          codeHash,
          "Enrollment",
          ownerGitHubUserId,
          createdAt,
          createdAt.AddMinutes(10),
          cancellationToken);

  private static async Task<(
      SqliteConnectionFactory ConnectionFactory,
      SqliteFleetStore Store,
      Guid NodeId)> CreateEnrolledStoreAsync(
      string databasePath,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
        cancellationToken);
    var owner = new DashboardUser(
        "1",
        "owner",
        "Owner",
        null);
    await new SqliteAccessStore(connectionFactory).EnsureTenantOwnerAsync(
        "tenant",
        "Tenant",
        owner,
        now,
        cancellationToken);
    var store = new SqliteFleetStore(connectionFactory);
    const string codeHash = "code-hash";
    await CreateEnrollmentCodeAsync(
        store,
        "tenant",
        owner.GitHubUserId,
        codeHash,
        now,
        cancellationToken);
    var enrollment = await store.RedeemEnrollmentCodeAsync(
        codeHash,
        "connector-instance",
        "Connector name",
        "credential-hash",
        now,
        cancellationToken);
    var nodeId = enrollment.NodeId ??
        throw new InvalidOperationException(
            "Enrollment did not return a node ID.");
    return (connectionFactory, store, nodeId);
  }

  private static async Task CreateVersionThreeDatabaseAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using (var setupCommand = connection.CreateCommand())
    {
      setupCommand.CommandText =
          """
          CREATE TABLE schema_migrations (
              version INTEGER PRIMARY KEY,
              name TEXT NOT NULL,
              checksum TEXT NOT NULL,
              applied_at TEXT NOT NULL
          );
          """;
      await setupCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    foreach (var migration in SqliteMigrationCatalog.All
        .Where(candidate => candidate.Version <= 3))
    {
      await using var transaction = (SqliteTransaction)
          await connection.BeginTransactionAsync(cancellationToken);
      await using var migrationCommand = connection.CreateCommand();
      migrationCommand.Transaction = transaction;
      migrationCommand.CommandText = migration.Sql;
      await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

      await using var recordCommand = connection.CreateCommand();
      recordCommand.Transaction = transaction;
      recordCommand.CommandText =
          """
          INSERT INTO schema_migrations (
              version,
              name,
              checksum,
              applied_at)
          VALUES (
              $version,
              $name,
              $checksum,
              $appliedAt);
          """;
      recordCommand.Parameters.AddWithValue(
          "$version",
          migration.Version);
      recordCommand.Parameters.AddWithValue(
          "$name",
          migration.Name);
      recordCommand.Parameters.AddWithValue(
          "$checksum",
          migration.Checksum);
      recordCommand.Parameters.AddWithValue(
          "$appliedAt",
          DateTimeOffset.UtcNow.ToString(
              "O",
              CultureInfo.InvariantCulture));
      await recordCommand.ExecuteNonQueryAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    }

    await using var seedCommand = connection.CreateCommand();
    seedCommand.CommandText =
        """
        INSERT INTO tenants (
            tenant_id,
            display_name,
            created_at)
        VALUES (
            'tenant',
            'Tenant',
            '2026-07-19T00:00:00.0000000+00:00');

        INSERT INTO nodes (
            node_id,
            tenant_id,
            connector_instance_id,
            display_name,
            credential_hash,
            enrolled_at)
        VALUES (
            $nodeId,
            'tenant',
            'connector-instance',
            'Connector name',
            'credential-hash',
            '2026-07-19T00:00:00.0000000+00:00');
        """;
    seedCommand.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    await seedCommand.ExecuteNonQueryAsync(cancellationToken);
  }
}
