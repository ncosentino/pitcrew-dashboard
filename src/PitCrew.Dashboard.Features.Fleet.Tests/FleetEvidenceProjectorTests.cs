using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class FleetEvidenceProjectorTests
{
  [Test]
  public async Task Fresh_Connector_Does_Not_Refresh_Stale_Measured_Zero(
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var managerObservedAt = new DateTimeOffset(
        2026,
        8,
        20,
        2,
        0,
        0,
        TimeSpan.Zero);
    var generatedAt = managerObservedAt.AddMinutes(10);
    var receivedAt = managerObservedAt.AddSeconds(2);
    var profile = new ManagerObservedState(
        1,
        21,
        "default",
        "manager-instance",
        "running",
        managerObservedAt,
        "repo",
        1,
        null,
        "accepted",
        0,
        0,
        0,
        [],
        null,
        0,
        null,
        0,
        SourceObservations: CreateSourceObservations(
            managerObservedAt,
            "manager-instance"));
    var node = new FleetNode(
        new Guid("11111111-1111-4111-8111-111111111111"),
        "Example node",
        "12.0.0",
        managerObservedAt.AddDays(-1),
        generatedAt.AddSeconds(-1),
        true,
        false,
        false,
        [profile],
        [],
        [])
    {
      ProfileInventory = new ConnectorProfileInventory(
          "complete",
          generatedAt.AddSeconds(-2),
          null),
      ProfileInventoryReceivedAt = generatedAt.AddSeconds(-1),
      ProfileEvidence =
      [
        new FleetProfileEvidence(
            "default",
            receivedAt,
            []),
      ],
    };

    var projected = FleetEvidenceProjector.ProjectNode(
        node,
        generatedAt,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(1));

    var connector = projected.EvidenceClaims.Single(
        claim => claim.Name == "connector-contact");
    var manager = projected.ProfileEvidence[0].Claims.Single(
        claim => claim.Name == "manager-observation");
    var workload = projected.ProfileEvidence[0].Claims.Single(
        claim => claim.Name == "workload-evidence");
    await Assert.That(connector.Freshness).IsEqualTo("current");
    await Assert.That(manager.Freshness).IsEqualTo("stale");
    await Assert.That(workload.Freshness).IsEqualTo("last-known");
    await Assert.That(workload.Retention).IsEqualTo("last-known");
    await Assert.That(workload.Value).IsEqualTo("0");
    await Assert.That(workload.SourceObservedAt)
        .IsEqualTo(managerObservedAt);
    await Assert.That(workload.DashboardReceivedAt)
        .IsEqualTo(receivedAt);
    await Assert.That(workload.EvaluatedAt)
        .IsEqualTo(generatedAt);

    var currentProjection = FleetEvidenceProjector.ProjectNode(
        node,
        managerObservedAt.AddSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(1));
    var currentWorkload =
        currentProjection.ProfileEvidence[0].Claims.Single(
            claim => claim.Name == "workload-evidence");
    await Assert.That(currentWorkload.Freshness)
        .IsEqualTo("measured-zero");
    await Assert.That(currentWorkload.Value).IsEqualTo("0");
  }

  [Test]
  public async Task Incident_Claims_Keep_Condition_And_Ownership_Orthogonal()
  {
    var observedAt = new DateTimeOffset(
        2026,
        8,
        20,
        3,
        0,
        0,
        TimeSpan.Zero);
    var projected = FleetEvidenceProjector.ProjectIncident(
        new AlertIncident(
            new Guid("22222222-2222-4222-8222-222222222222"),
            new Guid("11111111-1111-4111-8111-111111111111"),
            "default",
            "capacity-deficit",
            "critical",
            "acknowledged",
            "Capacity below target",
            "Accepted capacity evidence reports a deficit.",
            "capacity-deficit",
            null,
            "/incidents/22222222-2222-4222-8222-222222222222",
            observedAt,
            observedAt,
            observedAt,
            observedAt.AddSeconds(3),
            "123",
            null)
        {
          SourceObservedAt = observedAt,
          DashboardReceivedAt = observedAt.AddSeconds(1),
          EvaluatedAt = observedAt.AddSeconds(2),
          ConditionState = "confirmed",
          OperatorState = "acknowledged",
          CurrentSeverity = "critical",
          LastConfirmedSeverity = "critical",
          PeakSeverity = "critical",
        },
        observedAt.AddSeconds(4));

    var condition = projected.EvidenceClaims.Single(
        claim => claim.Name == "incident-condition");
    var ownership = projected.EvidenceClaims.Single(
        claim => claim.Name == "incident-ownership");
    await Assert.That(condition.Freshness).IsEqualTo("triggered");
    await Assert.That(condition.Value).IsEqualTo("confirmed");
    await Assert.That(ownership.Freshness).IsEqualTo("acknowledged");
    await Assert.That(ownership.Value).IsEqualTo("acknowledged");
    await Assert.That(condition.SourceObservedAt).IsEqualTo(observedAt);
    await Assert.That(ownership.SourceObservedAt)
        .IsEqualTo(observedAt.AddSeconds(3));
  }

  private static ManagerSourceObservations CreateSourceObservations(
      DateTimeOffset observedAt,
      string managerInstanceId) =>
      new(
          CreateSource(
              "local-runtime",
              observedAt,
              managerInstanceId),
          CreateSource(
              "github-scale-set",
              observedAt,
              managerInstanceId),
          CreateSource(
              "resource-telemetry",
              observedAt,
              managerInstanceId),
          CreateSource(
              "host-hardware",
              observedAt,
              managerInstanceId),
          CreateSource(
              "host-admission",
              observedAt,
              managerInstanceId),
          CreateSource(
              "subsystem-health",
              observedAt,
              managerInstanceId),
          CreateSource(
              "capacity",
              observedAt,
              managerInstanceId),
          CreateSource(
              "workload",
              observedAt,
              managerInstanceId));

  private static ManagerSourceObservation CreateSource(
      string source,
      DateTimeOffset observedAt,
      string managerInstanceId) =>
      new(
          "pitcrew-manager",
          source,
          managerInstanceId,
          observedAt,
          "complete",
          "live",
          null);
}
