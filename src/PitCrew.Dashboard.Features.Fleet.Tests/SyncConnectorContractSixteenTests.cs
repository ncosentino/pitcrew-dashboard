using System.Globalization;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractSixteenTests
{
  [Test]
  public async Task Validates_Available_Partial_And_Unavailable_Host_Pressure()
  {
    var profile = CreateProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsTrue();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              ResourceTelemetry = profile.ResourceTelemetry! with
              {
                HostPressure = profile.ResourceTelemetry.HostPressure! with
                {
                  Status = "unavailable",
                },
              },
            }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              ManagerContractVersion = 15,
            }))
        .IsFalse();
  }

  [Test]
  public async Task Detects_Sustained_Node_Pressure_From_Deduplicated_Samples()
  {
    var profile = CreateProfile();
    var node = new AlertNodeEvidence(
        "local",
        Guid.Parse(
            "11111111-1111-1111-1111-111111111111",
            CultureInfo.InvariantCulture),
        "Zephyr",
        profile.ObservedAt.AddDays(-1),
        profile.ObservedAt,
        false,
        [])
    {
      RecentHostPressureSamples =
      [
          Sample(profile.ObservedAt.AddSeconds(-45)),
          Sample(profile.ObservedAt.AddSeconds(-30)),
          Sample(profile.ObservedAt.AddSeconds(-15)),
          Sample(profile.ObservedAt),
      ],
    };

    var evaluation = AlertRuleEvaluator.Evaluate(
        new AlertEvidenceSnapshot([node]),
        new FleetDashboardOptions(),
        profile.ObservedAt);

    await Assert.That(evaluation.Candidates.Select(candidate => candidate.Kind))
        .IsEquivalentTo([
            "host-cpu-pressure",
            "host-memory-pressure",
            "host-io-pressure",
        ]);
  }

  [Test]
  public async Task Host_Load_Uses_Configured_Cpu_Threshold()
  {
    var profile = CreateProfile();
    var samples = new[]
    {
        profile.ObservedAt.AddSeconds(-45),
        profile.ObservedAt.AddSeconds(-30),
        profile.ObservedAt.AddSeconds(-15),
        profile.ObservedAt,
    }.Select(observedAt => new AlertHostPressureSample(
        observedAt,
        "partial",
        16,
        null,
        12,
        null,
        null,
        null,
        null,
        null)).ToArray();
    var node = Node(profile, samples);
    var options = new FleetDashboardOptions
    {
      AlertCpuPressurePercent = 70,
    };

    var evaluation = AlertRuleEvaluator.Evaluate(
        new AlertEvidenceSnapshot([node]),
        options,
        profile.ObservedAt);

    await Assert.That(evaluation.Candidates.Count(candidate =>
        candidate.Kind == "host-cpu-pressure")).IsEqualTo(1);
  }

  [Test]
  public async Task Insufficient_Host_Samples_Suppress_Specific_Diagnoses()
  {
    var profile = CreateProfile();
    var node = Node(
        profile,
        [Sample(profile.ObservedAt)]);

    var evaluation = AlertRuleEvaluator.Evaluate(
        new AlertEvidenceSnapshot([node]),
        new FleetDashboardOptions(),
        profile.ObservedAt);

    await Assert.That(evaluation.Candidates).IsEmpty();
    await Assert.That(evaluation.Suppressions.Count(suppression =>
        suppression.Kind is "host-cpu-pressure" or
            "host-memory-pressure" or
            "host-io-pressure")).IsEqualTo(3);
  }

  internal static ManagerObservedState CreateProfile()
  {
    var baseline = SyncConnectorContractFifteenTests.CreateProfile();
    return baseline with
    {
      ManagerContractVersion = 16,
      ResourceTelemetry = new ManagerResourceTelemetry(
          baseline.ObservedAt,
          "unavailable",
          null,
          null,
          new HostPressureTelemetry(
              "available",
              "docker-host",
              97.5,
              18,
              12,
              8,
              34359738368,
              2147483648,
              1073741824,
              35,
              5,
              25,
              3,
              42,
              18)),
    };
  }

  private static AlertHostPressureSample Sample(DateTimeOffset observedAt) =>
      new(
          observedAt,
          "available",
          16,
          97.5,
          18,
          34359738368,
          2147483648,
          35,
          25,
          42);

  private static AlertNodeEvidence Node(
      ManagerObservedState profile,
      IReadOnlyList<AlertHostPressureSample> samples) =>
      new(
          "local",
          Guid.Parse(
              "11111111-1111-1111-1111-111111111111",
              CultureInfo.InvariantCulture),
          "Zephyr",
          profile.ObservedAt.AddDays(-1),
          profile.ObservedAt,
          false,
          [])
      {
        RecentHostPressureSamples = samples,
      };
}
