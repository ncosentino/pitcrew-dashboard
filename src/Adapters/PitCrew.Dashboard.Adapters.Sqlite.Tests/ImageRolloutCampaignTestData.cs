using System.Globalization;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

internal static class ImageRolloutCampaignTestData
{
  public static readonly DateTimeOffset Now = new(
      2026,
      8,
      29,
      12,
      0,
      0,
      TimeSpan.Zero);

  public const string TenantId = "tenant";
  public const string ActorId = "1";
  public const string RecipeId = "ubuntu-runner";
  public const string TargetDigest =
      "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
  public const string TargetWorkerRevision =
      "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
  public const string CurrentWorkerRevision =
      "3333333333333333333333333333333333333333333333333333333333333333";
  public const string TargetSetHash =
      "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
  public const string Signature =
      "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

  public static async Task<SqliteConnectionFactory> CreateDatabaseAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
        cancellationToken);
    await new SqliteAccessStore(connectionFactory).EnsureTenantOwnerAsync(
        TenantId,
        "Tenant",
        new DashboardUser(ActorId, "owner", "Owner", null),
        Now,
        cancellationToken);
    await new SqliteAccessStore(connectionFactory).EnsureTenantOwnerAsync(
        "other-tenant",
        "Other Tenant",
        new DashboardUser("2", "other", "Other", null),
        Now,
        cancellationToken);
    return connectionFactory;
  }

  public static ImageRolloutCampaignPlan CreatePlan(
      Guid campaignId,
      IReadOnlyList<ImageRolloutCampaignPlannedTarget>? targets = null,
      string idempotencyKey = "campaign-create-1",
      string signature = Signature) =>
      new(
          campaignId,
          TenantId,
          ImageRolloutCampaignKind.Forward,
          null,
          CreateCandidate(),
          TargetSetHash,
          ActorId,
          Now,
          targets ?? CreateTargets(),
          idempotencyKey,
          signature);

  public static IReadOnlyList<ImageRolloutCampaignPlannedTarget> CreateTargets()
  {
    var firstNodeId = ParseGuid("10000000-0000-4000-8000-000000000001");
    var secondNodeId = ParseGuid("20000000-0000-4000-8000-000000000002");
    return
    [
        CreateEligibleTarget(
            ParseGuid("11000000-0000-4000-8000-000000000001"),
            firstNodeId,
            "Alpha",
            "build"),
        CreateEligibleTarget(
            ParseGuid("12000000-0000-4000-8000-000000000002"),
            firstNodeId,
            "Alpha",
            "deploy"),
        CreateEligibleTarget(
            ParseGuid("21000000-0000-4000-8000-000000000003"),
            secondNodeId,
            "Beta",
            "build"),
        new ImageRolloutCampaignPlannedTarget(
            ParseGuid("31000000-0000-4000-8000-000000000004"),
            ParseGuid("30000000-0000-4000-8000-000000000003"),
            "Gamma",
            "build",
            null,
            null,
            "node-offline"),
    ];
  }

  public static ImageRolloutCampaignPlannedTarget CreateEligibleTarget(
      Guid targetId,
      Guid nodeId,
      string nodeDisplayName,
      string profileId) =>
      new(
          targetId,
          nodeId,
          nodeDisplayName,
          profileId,
          CreateCandidate(),
          CreateFences(profileId),
          null);

  public static ImageRolloutCandidateAuthority CreateCandidate() =>
      new(
          ParseGuid("70000000-0000-4000-8000-000000000007"),
          RecipeId,
          TargetDigest,
          "linux/amd64");

  public static ImageRolloutCommandFences CreateFences(string profileId) =>
      new(
          $"ghcr.io/example/{profileId}:current",
          $"sha256:{new string('1', 64)}",
          $"sha256:{new string('2', 64)}",
          CurrentWorkerRevision,
          new string('4', 64),
          new string('5', 64),
          new string('6', 64),
          7,
          new string('7', 64));

  public static ImageRolloutOperatorCapability CreateCapability(
      string? currentDigest = null,
      string? currentLocalImageId = null,
      string? currentWorkerRevision = null,
      int currentWorkers = 2,
      int staleWorkers = 0,
      int observedStateAgeSeconds = 5,
      string? managerConvergenceStatus = null) =>
      new(
          [
              new ImageRolloutOperatorProfile(
                  "build",
                  "linux/amd64",
                  "ghcr.io/example/build:current",
                  currentDigest ??
                      $"sha256:{new string('1', 64)}",
                  currentLocalImageId ??
                      $"sha256:{new string('2', 64)}",
                  currentWorkerRevision ?? CurrentWorkerRevision,
                  new string('4', 64),
                  new string('5', 64),
                  new string('6', 64),
                  7,
                  new string('7', 64),
                  [RecipeId],
                  true,
                  true,
                  null,
                  false,
                  observedStateAgeSeconds,
                  600,
                  1800,
                  managerConvergenceStatus ??
                      (staleWorkers == 0 ? "current" : "rolling"),
                  currentWorkers,
                  staleWorkers),
          ]);

  public static async Task<Guid> EnrollNodeAsync(
      SqliteConnectionFactory connectionFactory,
      CancellationToken cancellationToken)
  {
    var fleetStore = new SqliteFleetStore(connectionFactory);
    const string codeHash = "campaign-test-code-hash";
    await fleetStore.CreateEnrollmentCodeAsync(
        Guid.NewGuid(),
        TenantId,
        codeHash,
        "Campaign test",
        ActorId,
        Now,
        Now.AddMinutes(10),
        cancellationToken);
    var enrollment = await fleetStore.RedeemEnrollmentCodeAsync(
        codeHash,
        "campaign-test",
        "Alpha",
        "campaign-test-credential-hash",
        Now,
        cancellationToken);
    return enrollment.NodeId ??
        throw new InvalidOperationException("Campaign test node enrollment failed.");
  }

  public static string CreateDatabasePath() =>
      Path.Combine(
          Path.GetTempPath(),
          $"pitcrew-image-campaign-{Guid.NewGuid():N}.db");

  public static Guid ParseGuid(string value) =>
      Guid.Parse(value, CultureInfo.InvariantCulture);
}
