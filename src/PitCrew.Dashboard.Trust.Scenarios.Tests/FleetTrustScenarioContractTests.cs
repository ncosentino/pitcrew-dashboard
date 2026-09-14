using PitCrew.Dashboard.Trust.Scenarios;

namespace PitCrew.Dashboard.Trust.Scenarios.Tests;

public sealed class FleetTrustScenarioContractTests
{
  [Test]
  public async Task Corpus_Defines_Required_Trust_Failures_Without_Guessing()
  {
    var corpus = FleetTrustScenarioCorpus.Load();

    await Assert.That(corpus.SchemaVersion).IsEqualTo(1);
    await Assert.That(corpus.FleetEvidence.Select(scenario => scenario.Id))
        .IsEquivalentTo(
        [
            "fresh-contact-stale-manager",
            "identity-mismatch",
            "reporting-current-manager",
            "reporting-loss-retained-evidence",
            "true-resolution",
        ]);
    await Assert.That(corpus.SupportDiagnostics.Select(scenario => scenario.Id))
        .IsEquivalentTo(
        [
            "explicit-profile-success",
            "invalid-diagnostic-scope",
            "leave-return-exact-result",
            "omitted-profile-ambiguity",
            "partial-evidence",
            "policy-rejection",
            "timeout",
        ]);
    await Assert.That(corpus.FleetEvidence.Count(
        scenario => scenario.TrueResolutionProven))
        .IsEqualTo(1);
    await Assert.That(corpus.FleetEvidence.Single(
        scenario => scenario.TrueResolutionProven).Id)
        .IsEqualTo("true-resolution");
    await Assert.That(corpus.CurrentBehavior.Count).IsEqualTo(6);
    await Assert.That(corpus.FleetEvidence.Select(
        scenario => scenario.ExpectedInterpretation))
        .IsEquivalentTo(
        [
            "current-evidence",
            "identity-mismatch",
            "reporting-loss",
            "stale-manager-evidence",
            "true-resolution",
        ]);
    await Assert.That(corpus.FleetEvidence.SelectMany(
        scenario => scenario.Claims).Select(claim => claim.State)
        .Distinct(StringComparer.Ordinal))
        .IsEquivalentTo(
        [
            "authoritative",
            "unavailable",
        ]);
    await Assert.That(corpus.FleetEvidence.SelectMany(
        scenario => scenario.Claims).Select(claim => claim.Freshness)
        .Distinct(StringComparer.Ordinal))
        .IsEquivalentTo(
        [
            "current",
            "retained",
            "stale",
            "unavailable",
        ]);

    var reportingLoss = corpus.FleetEvidence.Single(
        scenario => scenario.Id == "reporting-loss-retained-evidence");
    await Assert.That(reportingLoss.ExpectedInterpretation)
        .IsEqualTo("reporting-loss");
    await Assert.That(reportingLoss.Claims.Single(
        claim => claim.Name == "manager-observation").Freshness)
        .IsEqualTo("retained");
    await Assert.That(reportingLoss.Claims.Single(
        claim => claim.Name == "workload-evidence").Freshness)
        .IsEqualTo("retained");
    await Assert.That(reportingLoss.TrueResolutionProven).IsFalse()
        .Because("retained evidence cannot prove recovery");

    var staleManager = corpus.FleetEvidence.Single(
        scenario => scenario.Id == "fresh-contact-stale-manager");
    var connectorClaim = staleManager.Claims.Single(
        claim => claim.Name == "connector-contact");
    var managerClaim = staleManager.Claims.Single(
        claim => claim.Name == "manager-observation");
    await Assert.That(connectorClaim.State).IsEqualTo("authoritative");
    await Assert.That(managerClaim.Freshness).IsEqualTo("stale");
    await Assert.That(connectorClaim.SourceObservedAt).IsNotNull();
    await Assert.That(managerClaim.SourceObservedAt).IsNotNull();
    await Assert.That(connectorClaim.SourceObservedAt.GetValueOrDefault())
        .IsGreaterThan(managerClaim.SourceObservedAt.GetValueOrDefault());

    var identityMismatch = corpus.FleetEvidence.Single(
        scenario => scenario.Id == "identity-mismatch");
    await Assert.That(identityMismatch.Claims.Single(
        claim => claim.Name == "connector-identity").Authority)
        .IsEqualTo("credential-derived");

    foreach (var scenario in corpus.FleetEvidence)
    {
      await Assert.That(scenario.Clocks.SourceObservedAt)
          .IsLessThan(scenario.Clocks.DashboardReceivedAt);
      await Assert.That(scenario.Clocks.DashboardReceivedAt)
          .IsLessThan(scenario.Clocks.EvaluatedAt);
      await Assert.That(scenario.Clocks.EvaluatedAt)
          .IsLessThan(scenario.Clocks.ResponseGeneratedAt);
    }
  }

  [Test]
  public async Task Incident_Cardinality_Cases_Preserve_Original_Decomposition()
  {
    var corpus = FleetTrustScenarioCorpus.Load();
    var expected = new Dictionary<string, (
        int Count,
        int Visible,
        bool Truncated)>(StringComparer.Ordinal)
    {
      ["incidents-0"] = (0, 0, false),
      ["incidents-1"] = (1, 1, false),
      ["incidents-62"] = (62, 62, false),
      ["incidents-200"] = (200, 200, false),
      ["incidents-over-200"] = (201, 200, true),
    };

    await Assert.That(corpus.IncidentCardinalities.Select(
        scenario => scenario.IncidentCount))
        .IsEquivalentTo([0, 1, 62, 200, 201]);
    foreach (var scenario in corpus.IncidentCardinalities)
    {
      var incidents = FleetTrustScenarioCorpus.CreateIncidents(scenario);
      var expectedCase = expected[scenario.Id];

      await Assert.That(scenario.IncidentCount)
          .IsEqualTo(expectedCase.Count);
      await Assert.That(incidents.Count).IsEqualTo(expectedCase.Count);
      await Assert.That(incidents.Select(incident => incident.ConditionKey)
          .Distinct(StringComparer.Ordinal)
          .Count())
          .IsEqualTo(incidents.Count);
      await Assert.That(scenario.QueryLimit).IsEqualTo(200);
      await Assert.That(scenario.ExpectedVisibleCount)
          .IsEqualTo(expectedCase.Visible);
      await Assert.That(scenario.ExpectedTruncated)
          .IsEqualTo(expectedCase.Truncated);
      await Assert.That(scenario.AuthoritativeTotalAvailable).IsFalse()
          .Because("the current incident page exposes truncation but no total");
    }
  }

  [Test]
  public async Task Repeated_And_Independent_Signals_Remain_Distinguishable()
  {
    var corpus = FleetTrustScenarioCorpus.Load();
    var persistent = corpus.ConditionDecomposition.Single(
        scenario => scenario.Id == "repeated-persistent-condition");
    var independent = corpus.ConditionDecomposition.Single(
        scenario => scenario.Id == "independent-conditions");

    await Assert.That(persistent.Signals.Select(signal => signal.ConditionKey)
        .Distinct(StringComparer.Ordinal)
        .Count())
        .IsEqualTo(persistent.ExpectedConditionCount);
    await Assert.That(persistent.ExpectedIncidentCount).IsEqualTo(1);
    await Assert.That(independent.Signals.Select(signal => signal.ConditionKey)
        .Distinct(StringComparer.Ordinal)
        .Count())
        .IsEqualTo(independent.ExpectedConditionCount);
    await Assert.That(independent.ExpectedIncidentCount).IsEqualTo(3);
    await Assert.That(corpus.ConditionDecomposition.SelectMany(
        scenario => scenario.Signals).Count(
            signal => signal.Truth == "unknown"))
        .IsEqualTo(1);
  }

  [Test]
  public async Task Support_Scenarios_Preserve_Result_And_Failure_Boundaries()
  {
    var corpus = FleetTrustScenarioCorpus.Load();
    var scenario = corpus.SupportDiagnostics.Single(
        candidate => candidate.Id == "leave-return-exact-result");

    await Assert.That(scenario.Outcome).IsEqualTo("completed");
    await Assert.That(scenario.Result).IsNotNull();
    await Assert.That(scenario.ReturnContext)
        .IsEqualTo("fresh-tenant-session-read");
    await Assert.That(corpus.SupportDiagnostics.Select(
        diagnostic => diagnostic.ExpectedInterpretation))
        .IsEquivalentTo(
        [
            "exact-result-retrieval",
            "explicit-profile-success",
            "invalid-diagnostic-scope",
            "omitted-profile-ambiguous",
            "partial-evidence",
            "policy-rejection",
            "timeout",
        ]);

    var explicitProfile = corpus.SupportDiagnostics.Single(
        candidate => candidate.Id == "explicit-profile-success");
    await Assert.That(explicitProfile.ProfileId).IsEqualTo("default");
    await Assert.That(explicitProfile.Claims.Single(
        claim => claim.Name == "diagnostic-report").State)
        .IsEqualTo("authoritative");

    var omittedProfile = corpus.SupportDiagnostics.Single(
        candidate => candidate.Id == "omitted-profile-ambiguity");
    await Assert.That(omittedProfile.ProfileId).IsNull();
    await Assert.That(omittedProfile.ExpectedInterpretation)
        .IsEqualTo("omitted-profile-ambiguous");

    var policyRejection = corpus.SupportDiagnostics.Single(
        candidate => candidate.Id == "policy-rejection");
    await Assert.That(policyRejection.RejectionDisposition)
        .IsEqualTo("broker-evidence-access-denied");

    var timeout = corpus.SupportDiagnostics.Single(
        candidate => candidate.Id == "timeout");
    await Assert.That(timeout.RejectionDisposition)
        .IsEqualTo("broker-timeout");

    var partial = corpus.SupportDiagnostics.Single(
        candidate => candidate.Id == "partial-evidence");
    await Assert.That(partial.Claims.Count(
        claim => claim.State == "partial")).IsEqualTo(1);
    await Assert.That(partial.Claims.Count(
        claim => claim.State == "unavailable")).IsEqualTo(1);

    var invalidScope = corpus.SupportDiagnostics.Single(
        candidate => candidate.Id == "invalid-diagnostic-scope");
    await Assert.That(invalidScope.Outcome).IsEqualTo("forbidden");
    await Assert.That(invalidScope.Result).IsNull();

    foreach (var diagnostic in corpus.SupportDiagnostics)
    {
      await Assert.That(diagnostic.Clocks.SourceObservedAt)
          .IsLessThan(diagnostic.Clocks.DashboardReceivedAt);
      await Assert.That(diagnostic.Clocks.DashboardReceivedAt)
          .IsLessThan(diagnostic.Clocks.EvaluatedAt);
      await Assert.That(diagnostic.Clocks.EvaluatedAt)
          .IsLessThan(diagnostic.Clocks.ResponseGeneratedAt);
    }
  }
}
