using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteRecoveryCommandStoreTests
{
  private static readonly DateTimeOffset Now = new(
      2026,
      7,
      26,
      12,
      0,
      0,
      TimeSpan.Zero);

  [Test]
  public async Task Recovery_Command_Is_Offered_Until_Claimed_And_Executes_At_Most_Once(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteRecoveryCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);

      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.Queued);

      var firstOffer = await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);
      await Assert.That(firstOffer).IsNotNull();
      await Assert.That(firstOffer!.CommandId)
          .IsEqualTo(queued.CommandId!.Value);
      await Assert.That(firstOffer.ExpectedManagerInstanceId)
          .IsEqualTo("manager-instance");

      var immediateReoffer = await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(2),
          cancellationToken);
      await Assert.That(immediateReoffer)
          .IsNull()
          .Because("delivery is only repeated after the redelivery window");

      var redelivered = await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          null,
          Now.AddSeconds(200),
          Now.AddSeconds(80),
          cancellationToken);
      await Assert.That(redelivered).IsNotNull();

      var claimed = await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          new RecoveryCommandProgress(
              queued.CommandId!.Value,
              "claimed",
              Now.AddSeconds(210)),
          null,
          Now.AddSeconds(211),
          Now.AddSeconds(150),
          cancellationToken);
      await Assert.That(claimed)
          .IsNull()
          .Because("a claimed command is never offered again");

      var started = await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          new RecoveryCommandProgress(
              queued.CommandId!.Value,
              "started",
              Now.AddSeconds(215)),
          null,
          Now.AddSeconds(216),
          Now.AddSeconds(150),
          cancellationToken);
      await Assert.That(started).IsNull();

      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new RecoveryCommandOutcome(
              queued.CommandId!.Value,
              "succeeded",
              null,
              "Manager was restarted.",
              "manager-instance",
              "manager-instance-2",
              Now.AddSeconds(220)),
          Now.AddSeconds(221),
          Now.AddSeconds(150),
          cancellationToken);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(controls).HasSingleItem();
      var command = controls[0].Profiles[0].LatestCommand;
      await Assert.That(command).IsNotNull();
      await Assert.That(command!.Status).IsEqualTo("succeeded");
      await Assert.That(command.RequestedByGitHubUserId).IsEqualTo("1");
      await Assert.That(command.AfterManagerInstanceId)
          .IsEqualTo("manager-instance-2");
      await Assert.That(command.StartedAt).IsNotNull();
      await Assert.That(command.FailureCategory).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Controls_Project_Newest_First_History_And_Freshness_Budget(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteRecoveryCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var first = await QueueAsync(store, nodeId, Now, cancellationToken);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new RecoveryCommandOutcome(
              first.CommandId!.Value,
              "rejected",
              "not-allowed",
              "Local policy rejected the command.",
              "manager-instance",
              "manager-instance",
              Now.AddSeconds(30)),
          Now.AddSeconds(31),
          Now.AddSeconds(-60),
          cancellationToken);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(300),
          cancellationToken);
      var second = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(300),
          cancellationToken);
      await Assert.That(second.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.Queued);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var profile = controls[0].Profiles[0];
      await Assert.That(profile.ObservedStateMaximumAgeSeconds).IsEqualTo(120);
      await Assert.That(profile.RecentCommands.Count).IsEqualTo(2);
      await Assert.That(profile.RecentCommands[0].CommandId)
          .IsEqualTo(second.CommandId!.Value);
      await Assert.That(profile.RecentCommands[1].CommandId)
          .IsEqualTo(first.CommandId!.Value);
      await Assert.That(profile.RecentCommands[1].Status)
          .IsEqualTo("rejected")
          .Because("terminal outcomes stay visible in the audit history");
      await Assert.That(profile.LatestCommand!.CommandId)
          .IsEqualTo(profile.RecentCommands[0].CommandId);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Terminal_Outcome_And_Audit_Actor_Are_Immutable(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteRecoveryCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new RecoveryCommandOutcome(
              queued.CommandId!.Value,
              "failed",
              "process-failure",
              "Recovery process exited with a failure.",
              "manager-instance",
              "manager-instance",
              Now.AddSeconds(30)),
          Now.AddSeconds(31),
          Now.AddSeconds(-60),
          cancellationToken);

      var laterOutcome = await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new RecoveryCommandOutcome(
              queued.CommandId!.Value,
              "succeeded",
              null,
              "Late duplicate outcome.",
              "manager-instance",
              "manager-instance-2",
              Now.AddSeconds(60)),
          Now.AddSeconds(61),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(laterOutcome).IsNull();

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(controls[0].Profiles[0].LatestCommand!.Status)
          .IsEqualTo("failed");

      await Assert.That(
              async () => await ExecuteAsync(
                  connectionFactory,
                  """
                  UPDATE recovery_commands
                  SET status = 'succeeded'
                  WHERE command_id = $commandId;
                  """,
                  queued.CommandId!.Value,
                  cancellationToken))
          .Throws<SqliteException>()
          .Because("terminal outcomes are immutable");

      await Assert.That(
              async () => await ExecuteAsync(
                  connectionFactory,
                  """
                  UPDATE recovery_commands
                  SET requested_by_github_user_id = '2'
                  WHERE command_id = $commandId;
                  """,
                  queued.CommandId!.Value,
                  cancellationToken))
          .Throws<SqliteException>()
          .Because("audit actors are immutable");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Capacity_And_Recovery_Operations_Are_Mutually_Exclusive(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var recoveryStore = new SqliteRecoveryCommandStore(connectionFactory);
      var capacityStore = new SqliteCapacityCommandStore(connectionFactory);
      await SynchronizeAsync(recoveryStore, nodeId, Now, cancellationToken);
      await capacityStore.ApplyConnectorSyncAsync(
          nodeId,
          new CapacityOperatorCapability(
              [
                  new CapacityOperatorProfile(
                      "default",
                      4,
                      10,
                      50),
              ]),
          null,
          Now,
          Now.AddSeconds(-60),
          cancellationToken);

      var queuedRecovery = await QueueAsync(
          recoveryStore,
          nodeId,
          Now,
          cancellationToken);
      await Assert.That(queuedRecovery.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.Queued);

      var blockedCapacity = await capacityStore.QueueAsync(
          "tenant",
          nodeId,
          "default",
          20,
          "1",
          Now.AddSeconds(1),
          Now.AddMinutes(10),
          cancellationToken);
      await Assert.That(blockedCapacity.Status)
          .IsEqualTo(CapacityCommandQueueStatus.Conflict);

      var blockedRecovery = await QueueAsync(
          recoveryStore,
          nodeId,
          Now.AddSeconds(120),
          cancellationToken);
      await Assert.That(blockedRecovery.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.Conflict);

      await recoveryStore.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new RecoveryCommandOutcome(
              queuedRecovery.CommandId!.Value,
              "rejected",
              "not-allowed",
              "Local policy rejected the command.",
              "manager-instance",
              "manager-instance",
              Now.AddSeconds(150)),
          Now.AddSeconds(151),
          Now.AddSeconds(60),
          cancellationToken);

      var allowedCapacity = await capacityStore.QueueAsync(
          "tenant",
          nodeId,
          "default",
          20,
          "1",
          Now.AddSeconds(160),
          Now.AddMinutes(20),
          cancellationToken);
      await Assert.That(allowedCapacity.Status)
          .IsEqualTo(CapacityCommandQueueStatus.Queued)
          .Because("the profile operation slot is released on terminal outcomes");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Unsupported_Disallowed_Stale_And_Repeated_Requests_Are_Rejected(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteRecoveryCommandStore(connectionFactory);

      var unsupported = await QueueAsync(
          store,
          nodeId,
          Now,
          cancellationToken);
      await Assert.That(unsupported.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.Unsupported);

      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability() with
          {
            Profiles =
            [
                CreateCapability().Profiles[0] with
                {
                  RecoveryAllowed = false,
                },
            ],
          },
          null,
          null,
          Now,
          Now.AddSeconds(-60),
          cancellationToken);
      var notAllowed = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);
      await Assert.That(notAllowed.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.NotAllowed);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(2),
          cancellationToken);
      var staleFence = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          new RecoveryCommandFences(
              "manager-instance",
              3,
              new string('a', 64)),
          "1",
          Now.AddSeconds(3),
          Now.AddMinutes(10),
          Now.AddSeconds(-117),
          Now.AddSeconds(-57),
          cancellationToken);
      await Assert.That(staleFence.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.StaleFence);

      var staleCapability = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(1000),
          cancellationToken);
      await Assert.That(staleCapability.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.StaleFence)
          .Because("queueing requires a fresh connector projection");

      var queued = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(4),
          cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.Queued);

      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new RecoveryCommandOutcome(
              queued.CommandId!.Value,
              "rejected",
              "stale-fence",
              "Fences changed locally.",
              "manager-instance",
              "manager-instance",
              Now.AddSeconds(5)),
          Now.AddSeconds(6),
          Now.AddSeconds(-60),
          cancellationToken);
      var rateLimited = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(7),
          cancellationToken);
      await Assert.That(rateLimited.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.RateLimited);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Expiry_Resolves_Queued_And_Started_Commands_Differently(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteRecoveryCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddMinutes(30),
          cancellationToken);
      var expiredControls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(
              expiredControls[0].Profiles[0].LatestCommand!.Status)
          .IsEqualTo("expired");
      await Assert.That(
              expiredControls[0].Profiles[0].LatestCommand!.FailureCategory)
          .IsEqualTo("expired");
      await Assert.That(expiredControls[0].Profiles[0].LatestCommand!.CommandId)
          .IsEqualTo(queued.CommandId!.Value);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddMinutes(31),
          cancellationToken);
      var second = await QueueAsync(
          store,
          nodeId,
          Now.AddMinutes(31),
          cancellationToken);
      await Assert.That(second.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.Queued);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          new RecoveryCommandProgress(
              second.CommandId!.Value,
              "started",
              Now.AddMinutes(32)),
          null,
          Now.AddMinutes(32),
          Now.AddMinutes(30),
          cancellationToken);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddMinutes(60),
          cancellationToken);
      var indeterminateControls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(
              indeterminateControls[0].Profiles[0].LatestCommand!.Status)
          .IsEqualTo("indeterminate")
          .Because("a started command never re-executes after expiry");
      await Assert.That(
              indeterminateControls[0].Profiles[0].LatestCommand!.FailureCategory)
          .IsEqualTo("timeout");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Existing_Capacity_Commands_Are_Migrated_Into_Operation_Slots(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      var (_, nodeId) = await CreateEnrolledNodeAsync(
          connectionFactory,
          cancellationToken);
      var capacityStore = new SqliteCapacityCommandStore(connectionFactory);
      await capacityStore.ApplyConnectorSyncAsync(
          nodeId,
          new CapacityOperatorCapability(
              [
                  new CapacityOperatorProfile(
                      "default",
                      4,
                      10,
                      50),
              ]),
          null,
          Now,
          Now.AddSeconds(-60),
          cancellationToken);
      var capacityCommand = await capacityStore.QueueAsync(
          "tenant",
          nodeId,
          "default",
          20,
          "1",
          Now,
          Now.AddMinutes(10),
          cancellationToken);
      await Assert.That(capacityCommand.Status)
          .IsEqualTo(CapacityCommandQueueStatus.Queued);

      await ExecuteRawAsync(
          connectionFactory,
          """
          DELETE FROM profile_active_operations;

          DELETE FROM schema_migrations
          WHERE version = 6;

          DROP TRIGGER trg_capacity_commands_require_operation_slot;
          DROP TRIGGER trg_recovery_commands_require_operation_slot;
          DROP TRIGGER trg_recovery_commands_insert_queued;
          DROP TRIGGER trg_recovery_commands_immutable;
          DROP TRIGGER trg_recovery_commands_transitions;
          DROP TRIGGER trg_recovery_commands_terminal_evidence;

          DROP TABLE recovery_commands;
          DROP TABLE profile_active_operations;
          """,
          cancellationToken);
      await ExecuteRawAsync(
          connectionFactory,
          """
          ALTER TABLE nodes DROP COLUMN recovery_capability_json;

          ALTER TABLE nodes DROP COLUMN recovery_capability_at;
          """,
          cancellationToken);

      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);

      var recoveryStore = new SqliteRecoveryCommandStore(connectionFactory);
      await SynchronizeAsync(
          recoveryStore,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);
      var blockedRecovery = await QueueAsync(
          recoveryStore,
          nodeId,
          Now.AddSeconds(2),
          cancellationToken);
      await Assert.That(blockedRecovery.Status)
          .IsEqualTo(RecoveryCommandQueueStatus.Conflict)
          .Because("active capacity commands are migrated into operation slots");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static string CreateDatabasePath() =>
      Path.Combine(
          Path.GetTempPath(),
          $"pitcrew-recovery-{Guid.NewGuid():N}.db");

  private static RecoveryOperatorCapability CreateCapability() =>
      new(
          [
              new RecoveryOperatorProfile(
                  "default",
                  11,
                  true,
                  "manager-instance",
                  4,
                  null,
                  5,
                  true,
                  true,
                  false,
                  600,
                  1800),
          ]);

  private static async Task<RecoveryCommandQueueResult> QueueAsync(
      SqliteRecoveryCommandStore store,
      Guid nodeId,
      DateTimeOffset requestedAt,
      CancellationToken cancellationToken) =>
      await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          new RecoveryCommandFences(
              "manager-instance",
              4,
              null),
          "1",
          requestedAt,
          requestedAt.AddMinutes(10),
          requestedAt.AddSeconds(-120),
          requestedAt.AddSeconds(-60),
          cancellationToken);

  private static async Task<RecoverManagerCommand?> SynchronizeAsync(
      SqliteRecoveryCommandStore store,
      Guid nodeId,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken) =>
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          null,
          receivedAt,
          receivedAt.AddSeconds(-120),
          cancellationToken);

  private static async Task ExecuteAsync(
      SqliteConnectionFactory connectionFactory,
      string sql,
      Guid commandId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$commandId", commandId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ExecuteRawAsync(
      SqliteConnectionFactory connectionFactory,
      string sql,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<(
      SqliteConnectionFactory ConnectionFactory,
      Guid NodeId)> CreateEnrolledNodeAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    return await CreateEnrolledNodeAsync(
        connectionFactory,
        cancellationToken);
  }

  private static async Task<(
      SqliteConnectionFactory ConnectionFactory,
      Guid NodeId)> CreateEnrolledNodeAsync(
      SqliteConnectionFactory connectionFactory,
      CancellationToken cancellationToken)
  {
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
        Now,
        cancellationToken);
    var store = new SqliteFleetStore(connectionFactory);
    const string codeHash = "recovery-code-hash";
    await store.CreateEnrollmentCodeAsync(
        Guid.NewGuid(),
        "tenant",
        codeHash,
        "Enrollment",
        owner.GitHubUserId,
        Now,
        Now.AddMinutes(10),
        cancellationToken);
    var enrollment = await store.RedeemEnrollmentCodeAsync(
        codeHash,
        "connector-instance",
        "Connector name",
        "credential-hash",
        Now,
        cancellationToken);
    var nodeId = enrollment.NodeId ??
        throw new InvalidOperationException(
            "Enrollment did not return a node ID.");
    return (connectionFactory, nodeId);
  }
}
