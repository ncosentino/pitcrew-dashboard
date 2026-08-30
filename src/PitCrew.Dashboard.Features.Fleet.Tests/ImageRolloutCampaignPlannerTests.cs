using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class ImageRolloutCampaignPlannerTests
{
  [Test]
  public async Task Forward_Plan_Freezes_Eligible_And_Excluded_Targets_Deterministically()
  {
    var candidate = ImageRolloutCampaignFleetTestData.CreateCandidate();
    var eligibleNodeId = Guid.NewGuid();
    var offlineNodeId = Guid.NewGuid();
    var architectureNodeId = Guid.NewGuid();
    var currentNodeId = Guid.NewGuid();
    var policyNodeId = Guid.NewGuid();
    var fleet = new FleetResponse(
        ImageRolloutCampaignFleetTestData.Now,
        [
            ImageRolloutCampaignFleetTestData.CreateNode(
                eligibleNodeId,
                "Eligible"),
            ImageRolloutCampaignFleetTestData.CreateNode(
                offlineNodeId,
                "Offline",
                isOnline: false),
            ImageRolloutCampaignFleetTestData.CreateNode(
                architectureNodeId,
                "Arm"),
            ImageRolloutCampaignFleetTestData.CreateNode(
                currentNodeId,
                "Current"),
            ImageRolloutCampaignFleetTestData.CreateNode(
                policyNodeId,
                "Policy"),
        ]);
    var controls = new[]
    {
      new NodeImageRolloutControls(
          eligibleNodeId,
          [ImageRolloutCampaignFleetTestData.CreateControl()]),
      new NodeImageRolloutControls(
          offlineNodeId,
          [ImageRolloutCampaignFleetTestData.CreateControl()]),
      new NodeImageRolloutControls(
          architectureNodeId,
          [
              ImageRolloutCampaignFleetTestData.CreateControl(
                  architecture: "linux/arm64"),
          ]),
      new NodeImageRolloutControls(
          currentNodeId,
          [
              ImageRolloutCampaignFleetTestData.CreateControl(
                  currentDigest: candidate.TargetDigest),
          ]),
      new NodeImageRolloutControls(
          policyNodeId,
          [
              ImageRolloutCampaignFleetTestData.CreateControl(
                  rolloutAllowed: false,
                  localFailureCategory: "registry-not-allowed"),
          ]),
    };

    var first = ImageRolloutCampaignPlanner.CreateForwardTargets(
        fleet,
        controls,
        candidate,
        ImageRolloutCampaignFleetTestData.Now,
        120);
    var second = ImageRolloutCampaignPlanner.CreateForwardTargets(
        fleet,
        controls,
        candidate,
        ImageRolloutCampaignFleetTestData.Now,
        120);
    var firstHash = ImageRolloutCampaignSignatures.ComputeTargetSetHash(
        ImageRolloutCampaignKind.Forward,
        null,
        candidate,
        first);
    var secondHash = ImageRolloutCampaignSignatures.ComputeTargetSetHash(
        ImageRolloutCampaignKind.Forward,
        null,
        candidate,
        second);

    await Assert.That(first).Count().IsEqualTo(5);
    await Assert.That(first.Single(
            target => target.NodeId == eligibleNodeId).ExclusionCategory)
        .IsNull();
    await Assert.That(first.Single(
            target => target.NodeId == offlineNodeId).ExclusionCategory)
        .IsEqualTo("node-offline");
    await Assert.That(first.Single(
            target => target.NodeId == architectureNodeId).ExclusionCategory)
        .IsEqualTo("unsupported-architecture");
    await Assert.That(first.Single(
            target => target.NodeId == currentNodeId).ExclusionCategory)
        .IsEqualTo("already-current");
    await Assert.That(first.Single(
            target => target.NodeId == policyNodeId).ExclusionCategory)
        .IsEqualTo("registry-not-allowed");
    await Assert.That(firstHash).IsEqualTo(secondHash);
    await Assert.That(string.Join(
            ",",
            first.Select(static target => target.TargetId)))
        .IsNotEqualTo(string.Join(
            ",",
            second.Select(static target => target.TargetId)));
  }

  [Test]
  public async Task Rollback_Plan_Requires_Current_Source_And_Prior_Authority()
  {
    var nodeId = Guid.NewGuid();
    var sourceCandidate = ImageRolloutCampaignFleetTestData.CreateCandidate();
    var targetRevision = new string('8', 64);
    var priorDigest =
        "sha256:9999999999999999999999999999999999999999999999999999999999999999";
    var source = new ImageRolloutCampaignState(
        Guid.NewGuid(),
        "tenant",
        ImageRolloutCampaignKind.Forward,
        null,
        sourceCandidate,
        new string('a', 64),
        ImageRolloutCampaignStatus.Complete,
        2,
        10,
        "1",
        ImageRolloutCampaignFleetTestData.Now.AddHours(-1),
        "1",
        ImageRolloutCampaignFleetTestData.Now.AddMinutes(-50),
        null,
        null,
        ImageRolloutCampaignFleetTestData.Now.AddMinutes(-10),
        [
            new ImageRolloutCampaignTargetState(
                Guid.NewGuid(),
                nodeId,
                "Alpha",
                "build",
                sourceCandidate,
                null,
                null,
                ImageRolloutCampaignTargetStatus.Complete,
                0,
                true,
                Guid.NewGuid(),
                null,
                null,
                targetRevision,
                "current",
                2,
                0,
                null,
                null,
                ImageRolloutCampaignFleetTestData.Now.AddMinutes(-10),
                Guid.NewGuid(),
                "previous-recipe",
                "ghcr.io/example/previous:current",
                priorDigest,
                new string('9', 64)),
            new ImageRolloutCampaignTargetState(
                Guid.NewGuid(),
                nodeId,
                "Alpha",
                "deploy",
                sourceCandidate,
                null,
                null,
                ImageRolloutCampaignTargetStatus.Complete,
                1,
                false,
                Guid.NewGuid(),
                null,
                null,
                targetRevision,
                "current",
                2,
                0,
                null,
                null,
                ImageRolloutCampaignFleetTestData.Now.AddMinutes(-10),
                null,
                null,
                null,
                null,
                null),
        ],
        []);
    var fleet = new FleetResponse(
        ImageRolloutCampaignFleetTestData.Now,
        [
            ImageRolloutCampaignFleetTestData.CreateNode(
                nodeId,
                "Alpha"),
        ]);
    var controls = new[]
    {
      new NodeImageRolloutControls(
          nodeId,
          [
              ImageRolloutCampaignFleetTestData.CreateControl(
                  profileId: "build",
                  currentDigest: sourceCandidate.TargetDigest,
                  allowedRecipeIds:
                      ["ubuntu-runner", "previous-recipe"]) with
              {
                CurrentWorkerRevision = targetRevision,
              },
              ImageRolloutCampaignFleetTestData.CreateControl(
                  profileId: "deploy",
                  currentDigest: sourceCandidate.TargetDigest) with
              {
                CurrentWorkerRevision = targetRevision,
              },
          ]),
    };

    var targets = ImageRolloutCampaignPlanner.CreateRollbackTargets(
        source,
        fleet,
        controls,
        ImageRolloutCampaignFleetTestData.Now,
        120);

    await Assert.That(targets).Count().IsEqualTo(2);
    var eligible = targets.Single(target => target.ProfileId == "build");
    await Assert.That(eligible.ExclusionCategory).IsNull();
    await Assert.That(eligible.Candidate).IsNotNull();
    await Assert.That(eligible.Candidate!.TargetDigest)
        .IsEqualTo(priorDigest);
    await Assert.That(targets.Single(
            target => target.ProfileId == "deploy").ExclusionCategory)
        .IsEqualTo("rollback-authority-unavailable");
  }
}
