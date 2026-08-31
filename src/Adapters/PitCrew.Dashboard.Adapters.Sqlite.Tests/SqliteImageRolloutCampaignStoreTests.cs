using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Kernel.ImageRollouts;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteImageRolloutCampaignStoreTests
{
  [Test]
  public async Task Create_Configure_Approve_And_Claim_Preserve_Frozen_Authority(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = ImageRolloutCampaignTestData.ParseGuid(
          "90000000-0000-4000-8000-000000000009");
      var plan = ImageRolloutCampaignTestData.CreatePlan(campaignId);

      var created = await store.CreateAsync(plan, 100, cancellationToken);
      var replay = await store.CreateAsync(plan, 100, cancellationToken);
      var configured = await store.ConfigureAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignConfiguration(
              plan.Targets[2].TargetId,
              1,
              0,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "campaign-configure-1",
          new string('e', 64),
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          cancellationToken);
      var approved = await store.ApproveWaveAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignWaveApproval(
              0,
              1,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "campaign-approve-1",
          new string('f', 64),
          ImageRolloutCampaignTestData.Now.AddMinutes(2),
          cancellationToken);
      var claims = await store.ClaimDueTargetsAsync(
          "worker-1",
          ImageRolloutCampaignTestData.Now.AddMinutes(3),
          ImageRolloutCampaignTestData.Now.AddMinutes(4),
          10,
          2,
          1,
          cancellationToken);

      await Assert.That(created.Outcome)
          .IsEqualTo(ImageRolloutCampaignMutationOutcome.Succeeded);
      await Assert.That(created.Campaign).IsNotNull();
      await Assert.That(created.Campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.Draft);
      await Assert.That(created.Campaign.Targets).Count().IsEqualTo(4);
      await Assert.That(created.Campaign.Targets.Count(
              static target =>
                  target.Status == ImageRolloutCampaignTargetStatus.Excluded))
          .IsEqualTo(1);
      await Assert.That(replay.Outcome)
          .IsEqualTo(ImageRolloutCampaignMutationOutcome.IdempotentReplay);
      await Assert.That(replay.Campaign!.CampaignId).IsEqualTo(campaignId);
      await Assert.That(configured.Campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.AwaitingApproval);
      await Assert.That(configured.Campaign.Revision).IsEqualTo(1);
      await Assert.That(configured.Campaign.Waves).Count().IsEqualTo(3);
      await Assert.That(configured.Campaign.Waves.Select(
              static wave => wave.TargetCount))
          .IsEquivalentTo([1, 1, 1]);
      await Assert.That(configured.Campaign.Targets.Single(
              static target => target.IsCanary).TargetId)
          .IsEqualTo(plan.Targets[2].TargetId);
      await Assert.That(approved.Campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.Running);
      await Assert.That(approved.Campaign.Revision).IsEqualTo(2);
      await Assert.That(claims).HasSingleItem();
      await Assert.That(claims[0].TargetId)
          .IsEqualTo(plan.Targets[2].TargetId);
      await Assert.That(claims[0].Candidate.TargetDigest)
          .IsEqualTo(ImageRolloutCampaignTestData.TargetDigest);
      await Assert.That(claims[0].IdempotencyKey)
          .IsEqualTo($"campaign:{campaignId:D}:{plan.Targets[2].TargetId:D}");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Queue_Rejection_Blocks_Canary_And_Campaign(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();
      var target = ImageRolloutCampaignTestData.CreateTargets()[0];
      var plan = ImageRolloutCampaignTestData.CreatePlan(
          campaignId,
          [target]);
      await store.CreateAsync(plan, 100, cancellationToken);
      await store.ConfigureAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignConfiguration(
              null,
              10,
              0,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "configure-blocked",
          new string('1', 64),
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          cancellationToken);
      await store.ApproveWaveAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignWaveApproval(
              0,
              1,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "approve-blocked",
          new string('2', 64),
          ImageRolloutCampaignTestData.Now.AddMinutes(2),
          cancellationToken);
      var claim = (await store.ClaimDueTargetsAsync(
          "worker-blocked",
          ImageRolloutCampaignTestData.Now.AddMinutes(3),
          ImageRolloutCampaignTestData.Now.AddMinutes(4),
          1,
          1,
          1,
          cancellationToken)).Single();

      await store.CompleteDispatchAsync(
          campaignId,
          claim.TargetId,
          "worker-blocked",
          new ImageRolloutCommandQueueResult(
              ImageRolloutCommandQueueStatus.StaleFence,
              null),
          ImageRolloutCampaignTestData.Now.AddMinutes(3),
          cancellationToken);
      await store.ReconcileAsync(
          ImageRolloutCampaignTestData.Now.AddMinutes(4),
          120,
          100,
          cancellationToken);
      var campaign = await store.GetOrNullAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          cancellationToken);

      await Assert.That(campaign).IsNotNull();
      await Assert.That(campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.Blocked);
      await Assert.That(campaign.Waves).HasSingleItem();
      await Assert.That(campaign.Waves[0].Status)
          .IsEqualTo(ImageRolloutCampaignWaveStatus.Blocked);
      await Assert.That(campaign.Targets[0].Status)
          .IsEqualTo(ImageRolloutCampaignTargetStatus.Blocked);
      await Assert.That(campaign.Targets[0].FailureCategory)
          .IsEqualTo("stale-fence");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Reconcile_Requires_Zero_Stale_Workers_For_Campaign_Completion(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
        var connectionFactory =
            await ImageRolloutCampaignTestData.CreateDatabaseAsync(
                databasePath,
                cancellationToken);
        var nodeId = await ImageRolloutCampaignTestData.EnrollNodeAsync(
            connectionFactory,
            cancellationToken);
        var commandStore = new SqliteImageRolloutCommandStore(connectionFactory);
        var campaignStore = new SqliteImageRolloutCampaignStore(
            connectionFactory);
        await commandStore.ApplyConnectorSyncAsync(
            nodeId,
            ImageRolloutCampaignTestData.CreateCapability(),
            null,
            null,
            ImageRolloutCampaignTestData.Now,
            ImageRolloutCampaignTestData.Now.AddSeconds(-120),
            cancellationToken);
        var campaignId = Guid.NewGuid();
        var target = ImageRolloutCampaignTestData.CreateEligibleTarget(
            Guid.NewGuid(),
            nodeId,
            "Alpha",
            "build");
        await campaignStore.CreateAsync(
            ImageRolloutCampaignTestData.CreatePlan(
                campaignId,
                [target]),
            100,
            cancellationToken);
        await campaignStore.ConfigureAsync(
            ImageRolloutCampaignTestData.TenantId,
            campaignId,
            new ImageRolloutCampaignConfiguration(
                null,
                10,
                0,
                ImageRolloutCampaignTestData.TargetSetHash),
            ImageRolloutCampaignTestData.ActorId,
            "reconcile-configure",
            new string('1', 64),
            ImageRolloutCampaignTestData.Now.AddMinutes(1),
            cancellationToken);
        await campaignStore.ApproveWaveAsync(
            ImageRolloutCampaignTestData.TenantId,
            campaignId,
            new ImageRolloutCampaignWaveApproval(
                0,
                1,
                ImageRolloutCampaignTestData.TargetSetHash),
            ImageRolloutCampaignTestData.ActorId,
            "reconcile-approve",
            new string('2', 64),
            ImageRolloutCampaignTestData.Now.AddMinutes(2),
            cancellationToken);
        var claim = (await campaignStore.ClaimDueTargetsAsync(
            "reconcile-worker",
            ImageRolloutCampaignTestData.Now.AddMinutes(3),
            ImageRolloutCampaignTestData.Now.AddMinutes(4),
            1,
            1,
            1,
            cancellationToken)).Single();
        var queued = await commandStore.QueueAsync(
            ImageRolloutCampaignTestData.TenantId,
            claim.NodeId,
            claim.ProfileId,
            claim.Candidate,
            claim.Fences,
            claim.ApprovedByGitHubUserId,
            claim.IdempotencyKey,
            new string('3', 64),
            ImageRolloutCampaignTestData.Now.AddMinutes(3),
            ImageRolloutCampaignTestData.Now.AddMinutes(13),
            ImageRolloutCampaignTestData.Now.AddMinutes(-10),
            ImageRolloutCampaignTestData.Now.AddMinutes(-10),
            cancellationToken);
        await campaignStore.CompleteDispatchAsync(
            campaignId,
            claim.TargetId,
            "reconcile-worker",
            queued,
            ImageRolloutCampaignTestData.Now.AddMinutes(3),
            cancellationToken);
        var paused = await campaignStore.PauseAsync(
            ImageRolloutCampaignTestData.TenantId,
            campaignId,
            new ImageRolloutCampaignMutationFence(
                2,
                ImageRolloutCampaignTestData.TargetSetHash),
            ImageRolloutCampaignTestData.ActorId,
            "reconcile-pause",
            new string('4', 64),
            ImageRolloutCampaignTestData.Now.AddMinutes(4),
            cancellationToken);
        await commandStore.ApplyConnectorSyncAsync(
            nodeId,
            ImageRolloutCampaignTestData.CreateCapability(),
            null,
            new ImageRolloutCommandOutcome(
                queued.CommandId!.Value,
                "succeeded",
                null,
                "Applied with one stale worker.",
                ImageRolloutCampaignTestData.TargetDigest,
                ImageRolloutCampaignTestData.TargetWorkerRevision,
                "rolling",
                1,
                1,
                null,
                ImageRolloutCampaignTestData.Now.AddMinutes(5)),
            ImageRolloutCampaignTestData.Now.AddMinutes(5),
            ImageRolloutCampaignTestData.Now.AddMinutes(3),
            cancellationToken);
        await campaignStore.ReconcileAsync(
            ImageRolloutCampaignTestData.Now.AddMinutes(6),
            120,
            100,
            cancellationToken);
        var rolling = await campaignStore.GetOrNullAsync(
            ImageRolloutCampaignTestData.TenantId,
            campaignId,
            cancellationToken);

        await commandStore.ApplyConnectorSyncAsync(
            nodeId,
            ImageRolloutCampaignTestData.CreateCapability(
                ImageRolloutCampaignTestData.TargetDigest,
                ImageRolloutCampaignTestData.TargetDigest,
                ImageRolloutCampaignTestData.TargetWorkerRevision,
                observedStateAgeSeconds: 70),
            null,
            null,
            ImageRolloutCampaignTestData.Now.AddMinutes(7),
            ImageRolloutCampaignTestData.Now.AddMinutes(5),
            cancellationToken);
        await campaignStore.ReconcileAsync(
            ImageRolloutCampaignTestData.Now.AddMinutes(8),
            120,
            100,
            cancellationToken);
        var staleCombinedAge = await campaignStore.GetOrNullAsync(
            ImageRolloutCampaignTestData.TenantId,
            campaignId,
            cancellationToken);
        await commandStore.ApplyConnectorSyncAsync(
            nodeId,
            ImageRolloutCampaignTestData.CreateCapability(
                ImageRolloutCampaignTestData.TargetDigest,
                ImageRolloutCampaignTestData.TargetDigest,
                ImageRolloutCampaignTestData.TargetWorkerRevision),
            null,
            null,
            ImageRolloutCampaignTestData.Now.AddMinutes(9),
            ImageRolloutCampaignTestData.Now.AddMinutes(7),
            cancellationToken);
        await campaignStore.ReconcileAsync(
            ImageRolloutCampaignTestData.Now.AddMinutes(10),
            120,
            100,
            cancellationToken);
        var complete = await campaignStore.GetOrNullAsync(
            ImageRolloutCampaignTestData.TenantId,
            campaignId,
            cancellationToken);

        await Assert.That(rolling).IsNotNull();
        await Assert.That(paused.Campaign).IsNotNull();
        await Assert.That(paused.Campaign!.Status)
            .IsEqualTo(ImageRolloutCampaignStatus.Paused);
        await Assert.That(rolling!.Status)
            .IsEqualTo(ImageRolloutCampaignStatus.Paused);
        await Assert.That(rolling.Targets[0].Status)
            .IsEqualTo(ImageRolloutCampaignTargetStatus.Rolling);
        await Assert.That(rolling.Targets[0].StaleWorkers).IsEqualTo(1);
        await Assert.That(staleCombinedAge).IsNotNull();
        await Assert.That(staleCombinedAge!.Status)
            .IsEqualTo(ImageRolloutCampaignStatus.Paused);
        await Assert.That(staleCombinedAge.Targets[0].Status)
            .IsEqualTo(ImageRolloutCampaignTargetStatus.Rolling);
        await Assert.That(complete).IsNotNull();
        await Assert.That(complete!.Status)
            .IsEqualTo(ImageRolloutCampaignStatus.Complete);
        await Assert.That(complete.Waves[0].Status)
            .IsEqualTo(ImageRolloutCampaignWaveStatus.Complete);
        await Assert.That(complete.Targets[0].Status)
            .IsEqualTo(ImageRolloutCampaignTargetStatus.Complete);
        await Assert.That(complete.Targets[0].StaleWorkers).IsEqualTo(0);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Cancel_Stops_Undispatched_Targets_And_Is_Idempotent(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();
      var plan = ImageRolloutCampaignTestData.CreatePlan(campaignId);
      await store.CreateAsync(plan, 100, cancellationToken);
      var cancelled = await store.CancelAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignMutationFence(
              0,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "cancel-draft-1",
          new string('3', 64),
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          cancellationToken);
      var replay = await store.CancelAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignMutationFence(
              0,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "cancel-draft-1",
          new string('3', 64),
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          cancellationToken);

      await Assert.That(cancelled.Campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.Cancelled);
      await Assert.That(cancelled.Campaign.Targets.Count(
              static target =>
                  target.Status == ImageRolloutCampaignTargetStatus.Cancelled))
          .IsEqualTo(3);
      await Assert.That(cancelled.Campaign.Targets.Count(
              static target =>
                  target.Status == ImageRolloutCampaignTargetStatus.Excluded))
          .IsEqualTo(1);
      await Assert.That(replay.Outcome)
          .IsEqualTo(ImageRolloutCampaignMutationOutcome.IdempotentReplay);
      await Assert.That(replay.Campaign!.CampaignId).IsEqualTo(campaignId);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Create_Rejects_Target_Overflow_Without_Partial_Persistence(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();

      var result = await store.CreateAsync(
          ImageRolloutCampaignTestData.CreatePlan(campaignId),
          2,
          cancellationToken);
      var stored = await store.GetOrNullAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          cancellationToken);

      await Assert.That(result.Outcome)
          .IsEqualTo(
              ImageRolloutCampaignMutationOutcome.TargetLimitExceeded);
      await Assert.That(stored).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Empty_Fleet_Is_Blocked_Without_Fabricating_An_Eligible_Target(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();

      var created = await store.CreateAsync(
          ImageRolloutCampaignTestData.CreatePlan(
              campaignId,
              []),
          100,
          cancellationToken);
      var summaries = await store.ListAsync(
          ImageRolloutCampaignTestData.TenantId,
          10,
          cancellationToken);

      await Assert.That(created.Campaign).IsNotNull();
      await Assert.That(created.Campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.Blocked);
      await Assert.That(created.Campaign.Targets).IsEmpty();
      await Assert.That(summaries).HasSingleItem();
      await Assert.That(summaries[0].EligibleTargetCount).IsEqualTo(0);
      await Assert.That(summaries[0].ExcludedTargetCount).IsEqualTo(0);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Pause_Rejects_An_Active_Lease_But_Allows_An_Expired_Lease(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();
      await store.CreateAsync(
          ImageRolloutCampaignTestData.CreatePlan(
              campaignId,
              [ImageRolloutCampaignTestData.CreateTargets()[0]]),
          100,
          cancellationToken);
      await store.ConfigureAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignConfiguration(
              null,
              10,
              0,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "pause-configure",
          new string('4', 64),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);
      await store.ApproveWaveAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignWaveApproval(
              0,
              1,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "pause-approve",
          new string('5', 64),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);
      var claims = await store.ClaimDueTargetsAsync(
          "pause-worker",
          ImageRolloutCampaignTestData.Now,
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          1,
          1,
          1,
          cancellationToken);

      var paused = await store.PauseAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignMutationFence(
              2,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "pause-leased",
          new string('6', 64),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);
      var pausedAfterExpiry = await store.PauseAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignMutationFence(
              2,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "pause-expired-lease",
          new string('7', 64),
          ImageRolloutCampaignTestData.Now.AddMinutes(2),
          cancellationToken);
      var campaign = await store.GetOrNullAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          cancellationToken);

      await Assert.That(claims).HasSingleItem();
      await Assert.That(paused.Outcome)
          .IsEqualTo(ImageRolloutCampaignMutationOutcome.InvalidState);
      await Assert.That(pausedAfterExpiry.Outcome)
          .IsEqualTo(ImageRolloutCampaignMutationOutcome.Succeeded);
      await Assert.That(campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.Paused);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Adverse_Wave_Target_Blocks_Undispatched_Peers(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();
      var targets = ImageRolloutCampaignTestData.CreateTargets()
          .Where(static target => target.ExclusionCategory is null)
          .ToArray();
      await store.CreateAsync(
          ImageRolloutCampaignTestData.CreatePlan(
              campaignId,
              targets),
          100,
          cancellationToken);
      await store.ConfigureAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignConfiguration(
              targets[0].TargetId,
              2,
              0,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "wave-configure",
          new string('7', 64),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);
      await store.ApproveWaveAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignWaveApproval(
              0,
              1,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "canary-approve",
          new string('8', 64),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);
      await using (var connection = await connectionFactory.OpenAsync(
              cancellationToken))
      await using (var command = connection.CreateCommand())
      {
        command.CommandText =
            """
            UPDATE image_rollout_campaign_targets
            SET status = 'complete',
                command_id = $commandId,
                target_worker_revision = $targetWorkerRevision,
                manager_convergence_status = 'current',
                current_workers = 2,
                stale_workers = 0,
                completed_at = $completedAt
            WHERE campaign_id = $campaignId
              AND wave_number = 0;

            UPDATE image_rollout_campaign_waves
            SET status = 'complete',
                completed_at = $completedAt
            WHERE campaign_id = $campaignId
              AND wave_number = 0;

            UPDATE image_rollout_campaigns
            SET status = 'awaiting-approval'
            WHERE campaign_id = $campaignId;
            """;
        command.Parameters.AddWithValue(
            "$commandId",
            Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue(
            "$completedAt",
            ImageRolloutCampaignTestData.Now.ToString("O"));
        command.Parameters.AddWithValue(
            "$targetWorkerRevision",
            ImageRolloutCampaignTestData.TargetWorkerRevision);
        command.Parameters.AddWithValue(
            "$campaignId",
            campaignId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
      }
      await store.ApproveWaveAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignWaveApproval(
              1,
              2,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "wave-approve",
          new string('9', 64),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);
      var claim = (await store.ClaimDueTargetsAsync(
          "wave-worker",
          ImageRolloutCampaignTestData.Now,
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          1,
          1,
          1,
          cancellationToken)).Single();
      await store.CompleteDispatchAsync(
          campaignId,
          claim.TargetId,
          "wave-worker",
          new ImageRolloutCommandQueueResult(
              ImageRolloutCommandQueueStatus.StaleFence,
              null),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);
      await store.ReconcileAsync(
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          120,
          100,
          cancellationToken);
      var laterClaims = await store.ClaimDueTargetsAsync(
          "wave-worker",
          ImageRolloutCampaignTestData.Now.AddMinutes(2),
          ImageRolloutCampaignTestData.Now.AddMinutes(3),
          1,
          1,
          1,
          cancellationToken);
      var campaign = await store.GetOrNullAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          cancellationToken);

      await Assert.That(laterClaims).IsEmpty();
      await Assert.That(campaign).IsNotNull();
      await Assert.That(campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.Partial);
      await Assert.That(campaign.Waves.Single(
              static wave => wave.WaveNumber == 1).Status)
          .IsEqualTo(ImageRolloutCampaignWaveStatus.Blocked);
      var waveTargets = campaign.Targets
          .Where(static target => target.WaveNumber == 1)
          .ToArray();
      await Assert.That(waveTargets).Count().IsEqualTo(2);
      await Assert.That(waveTargets.Count(
              static target =>
                  target.Status == ImageRolloutCampaignTargetStatus.Blocked))
          .IsEqualTo(2);
      await Assert.That(waveTargets.Select(
              static target => target.FailureCategory ?? string.Empty))
          .IsEquivalentTo(["stale-fence", "wave-blocked"]);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Active_Lease_Consumes_The_Campaign_Concurrency_Limit(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();
      await PrepareApprovedLaterWaveAsync(
          connectionFactory,
          store,
          campaignId,
          "campaign-limit",
          cancellationToken);

      var first = await store.ClaimDueTargetsAsync(
          "campaign-limit-worker-1",
          ImageRolloutCampaignTestData.Now.AddMinutes(3),
          ImageRolloutCampaignTestData.Now.AddMinutes(4),
          1,
          1,
          10,
          cancellationToken);
      var second = await store.ClaimDueTargetsAsync(
          "campaign-limit-worker-2",
          ImageRolloutCampaignTestData.Now.AddMinutes(3),
          ImageRolloutCampaignTestData.Now.AddMinutes(4),
          1,
          1,
          10,
          cancellationToken);

      await Assert.That(first).HasSingleItem();
      await Assert.That(second).IsEmpty();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Saturated_Large_Campaign_Does_Not_Starve_A_Later_Campaign(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var saturatedCampaignId = ImageRolloutCampaignTestData.ParseGuid(
          "10000000-0000-4000-8000-000000000001");
      var laterCampaignId = ImageRolloutCampaignTestData.ParseGuid(
          "f0000000-0000-4000-8000-000000000001");
      var saturatedTargets = Enumerable.Range(0, 22)
          .Select(index =>
              ImageRolloutCampaignTestData.CreateEligibleTarget(
                  Guid.NewGuid(),
                  Guid.NewGuid(),
                  $"Saturated node {index}",
                  $"profile-{index}"))
          .ToArray();
      await PrepareApprovedLaterWaveAsync(
          connectionFactory,
          store,
          saturatedCampaignId,
          saturatedTargets,
          "saturated",
          cancellationToken);
      var saturatedClaim = await store.ClaimDueTargetsAsync(
          "saturated-worker",
          ImageRolloutCampaignTestData.Now.AddMinutes(3),
          ImageRolloutCampaignTestData.Now.AddMinutes(4),
          1,
          1,
          1,
          cancellationToken);
      await PrepareApprovedCampaignAsync(
          store,
          laterCampaignId,
          [
              ImageRolloutCampaignTestData.CreateEligibleTarget(
                  Guid.NewGuid(),
                  Guid.NewGuid(),
                  "Later node",
                  "build"),
          ],
          "later",
          cancellationToken);

      var laterClaim = await store.ClaimDueTargetsAsync(
          "later-worker",
          ImageRolloutCampaignTestData.Now.AddMinutes(3),
          ImageRolloutCampaignTestData.Now.AddMinutes(4),
          1,
          1,
          1,
          cancellationToken);

      await Assert.That(saturatedClaim).HasSingleItem();
      await Assert.That(laterClaim).HasSingleItem();
      await Assert.That(laterClaim[0].CampaignId)
          .IsEqualTo(laterCampaignId);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Active_Lease_Consumes_The_Node_Concurrency_Limit(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var nodeId = Guid.NewGuid();
      await PrepareApprovedCampaignAsync(
          store,
          Guid.NewGuid(),
          [
              ImageRolloutCampaignTestData.CreateEligibleTarget(
                  Guid.NewGuid(),
                  nodeId,
                  "Shared node",
                  "build"),
          ],
          "node-limit-a",
          cancellationToken);
      await PrepareApprovedCampaignAsync(
          store,
          Guid.NewGuid(),
          [
              ImageRolloutCampaignTestData.CreateEligibleTarget(
                  Guid.NewGuid(),
                  nodeId,
                  "Shared node",
                  "deploy"),
          ],
          "node-limit-b",
          cancellationToken);

      var first = await store.ClaimDueTargetsAsync(
          "node-limit-worker-1",
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          ImageRolloutCampaignTestData.Now.AddMinutes(2),
          1,
          10,
          1,
          cancellationToken);
      var second = await store.ClaimDueTargetsAsync(
          "node-limit-worker-2",
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          ImageRolloutCampaignTestData.Now.AddMinutes(2),
          1,
          10,
          1,
          cancellationToken);

      await Assert.That(first).HasSingleItem();
      await Assert.That(second).IsEmpty();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Cancel_Allows_An_Expired_Dispatch_Lease(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();
      await PrepareApprovedCampaignAsync(
          store,
          campaignId,
          [ImageRolloutCampaignTestData.CreateTargets()[0]],
          "cancel-expired",
          cancellationToken);
      var claim = await store.ClaimDueTargetsAsync(
          "cancel-expired-worker",
          ImageRolloutCampaignTestData.Now,
          ImageRolloutCampaignTestData.Now.AddMinutes(1),
          1,
          1,
          1,
          cancellationToken);

      var cancelled = await store.CancelAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignMutationFence(
              2,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "cancel-after-expiry",
          new string('f', 64),
          ImageRolloutCampaignTestData.Now.AddMinutes(2),
          cancellationToken);

      await Assert.That(claim).HasSingleItem();
      await Assert.That(cancelled.Outcome)
          .IsEqualTo(ImageRolloutCampaignMutationOutcome.Succeeded);
      await Assert.That(cancelled.Campaign!.Status)
          .IsEqualTo(ImageRolloutCampaignStatus.Cancelled);
      await Assert.That(cancelled.Campaign.Targets[0].Status)
          .IsEqualTo(ImageRolloutCampaignTargetStatus.Cancelled);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Expired_Dispatch_Lease_Reuses_The_Same_Target_Request_Key(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();
      await store.CreateAsync(
          ImageRolloutCampaignTestData.CreatePlan(
              campaignId,
              [ImageRolloutCampaignTestData.CreateTargets()[0]]),
          100,
          cancellationToken);
      await store.ConfigureAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignConfiguration(
              null,
              10,
              0,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "lease-configure",
          new string('a', 64),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);
      await store.ApproveWaveAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          new ImageRolloutCampaignWaveApproval(
              0,
              1,
              ImageRolloutCampaignTestData.TargetSetHash),
          ImageRolloutCampaignTestData.ActorId,
          "lease-approve",
          new string('b', 64),
          ImageRolloutCampaignTestData.Now,
          cancellationToken);

      var first = (await store.ClaimDueTargetsAsync(
          "first-worker",
          ImageRolloutCampaignTestData.Now,
          ImageRolloutCampaignTestData.Now.AddSeconds(30),
          1,
          1,
          1,
          cancellationToken)).Single();
      var replay = (await store.ClaimDueTargetsAsync(
          "second-worker",
          ImageRolloutCampaignTestData.Now.AddSeconds(31),
          ImageRolloutCampaignTestData.Now.AddSeconds(61),
          1,
          1,
          1,
          cancellationToken)).Single();
      var commandId = Guid.NewGuid();
      await store.CompleteDispatchAsync(
          campaignId,
          replay.TargetId,
          "second-worker",
          new ImageRolloutCommandQueueResult(
              ImageRolloutCommandQueueStatus.IdempotentReplay,
              commandId),
          ImageRolloutCampaignTestData.Now.AddSeconds(31),
          cancellationToken);
      var campaign = await store.GetOrNullAsync(
          ImageRolloutCampaignTestData.TenantId,
          campaignId,
          cancellationToken);

      await Assert.That(replay.TargetId).IsEqualTo(first.TargetId);
      await Assert.That(replay.IdempotencyKey)
          .IsEqualTo(first.IdempotencyKey);
      await Assert.That(campaign!.Targets[0].CommandId)
          .IsEqualTo(commandId);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Reads_Are_Tenant_Scoped(
      CancellationToken cancellationToken)
  {
    var databasePath = ImageRolloutCampaignTestData.CreateDatabasePath();
    try
    {
      var connectionFactory =
          await ImageRolloutCampaignTestData.CreateDatabaseAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteImageRolloutCampaignStore(connectionFactory);
      var campaignId = Guid.NewGuid();
      await store.CreateAsync(
          ImageRolloutCampaignTestData.CreatePlan(campaignId),
          100,
          cancellationToken);

      var foreign = await store.GetOrNullAsync(
          "other-tenant",
          campaignId,
          cancellationToken);
      var foreignList = await store.ListAsync(
          "other-tenant",
          10,
          cancellationToken);

      await Assert.That(foreign).IsNull();
      await Assert.That(foreignList).IsEmpty();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static async Task PrepareApprovedLaterWaveAsync(
      SqliteConnectionFactory connectionFactory,
      SqliteImageRolloutCampaignStore store,
      Guid campaignId,
      string keyPrefix,
      CancellationToken cancellationToken)
  {
    var targets = ImageRolloutCampaignTestData.CreateTargets()
        .Where(static target => target.ExclusionCategory is null)
        .ToArray();
    await PrepareApprovedLaterWaveAsync(
        connectionFactory,
        store,
        campaignId,
        targets,
        keyPrefix,
        cancellationToken);
  }

  private static async Task PrepareApprovedLaterWaveAsync(
      SqliteConnectionFactory connectionFactory,
      SqliteImageRolloutCampaignStore store,
      Guid campaignId,
      IReadOnlyList<ImageRolloutCampaignPlannedTarget> targets,
      string keyPrefix,
      CancellationToken cancellationToken)
  {
    await PrepareApprovedCampaignAsync(
        store,
        campaignId,
        targets,
        keyPrefix,
        cancellationToken);
    await using (var connection = await connectionFactory.OpenAsync(
            cancellationToken))
    await using (var command = connection.CreateCommand())
    {
      command.CommandText =
          """
          UPDATE image_rollout_campaign_targets
          SET status = 'complete',
              command_id = $commandId,
              target_worker_revision = $targetWorkerRevision,
              manager_convergence_status = 'current',
              current_workers = 2,
              stale_workers = 0,
              completed_at = $completedAt
          WHERE campaign_id = $campaignId
            AND wave_number = 0;

          UPDATE image_rollout_campaign_waves
          SET status = 'complete',
              completed_at = $completedAt
          WHERE campaign_id = $campaignId
            AND wave_number = 0;

          UPDATE image_rollout_campaigns
          SET status = 'awaiting-approval'
          WHERE campaign_id = $campaignId;
          """;
      command.Parameters.AddWithValue(
          "$commandId",
          Guid.NewGuid().ToString("D"));
      command.Parameters.AddWithValue(
          "$targetWorkerRevision",
          ImageRolloutCampaignTestData.TargetWorkerRevision);
      command.Parameters.AddWithValue(
          "$completedAt",
          ImageRolloutCampaignTestData.Now.ToString("O"));
      command.Parameters.AddWithValue(
          "$campaignId",
          campaignId.ToString("D"));
      await command.ExecuteNonQueryAsync(cancellationToken);
    }
    await store.ApproveWaveAsync(
        ImageRolloutCampaignTestData.TenantId,
        campaignId,
        new ImageRolloutCampaignWaveApproval(
            1,
            2,
            ImageRolloutCampaignTestData.TargetSetHash),
        ImageRolloutCampaignTestData.ActorId,
        $"{keyPrefix}-wave",
        new string('e', 64),
        ImageRolloutCampaignTestData.Now.AddMinutes(2),
        cancellationToken);
  }

  private static async Task PrepareApprovedCampaignAsync(
      SqliteImageRolloutCampaignStore store,
      Guid campaignId,
      IReadOnlyList<ImageRolloutCampaignPlannedTarget> targets,
      string keyPrefix,
      CancellationToken cancellationToken)
  {
    await store.CreateAsync(
        ImageRolloutCampaignTestData.CreatePlan(
            campaignId,
            targets,
            $"{keyPrefix}-create"),
        100,
        cancellationToken);
    await store.ConfigureAsync(
        ImageRolloutCampaignTestData.TenantId,
        campaignId,
        new ImageRolloutCampaignConfiguration(
            targets.Count == 1 ? null : targets[0].TargetId,
            Math.Max(1, targets.Count - 1),
            0,
            ImageRolloutCampaignTestData.TargetSetHash),
        ImageRolloutCampaignTestData.ActorId,
        $"{keyPrefix}-configure",
        new string('c', 64),
        ImageRolloutCampaignTestData.Now,
        cancellationToken);
    await store.ApproveWaveAsync(
        ImageRolloutCampaignTestData.TenantId,
        campaignId,
        new ImageRolloutCampaignWaveApproval(
            0,
            1,
            ImageRolloutCampaignTestData.TargetSetHash),
        ImageRolloutCampaignTestData.ActorId,
        $"{keyPrefix}-canary",
        new string('d', 64),
        ImageRolloutCampaignTestData.Now.AddMinutes(1),
        cancellationToken);
  }
}
