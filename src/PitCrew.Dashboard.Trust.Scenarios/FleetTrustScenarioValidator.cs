namespace PitCrew.Dashboard.Trust.Scenarios;

internal static class FleetTrustScenarioValidator
{
  private static readonly string[] _claimStates =
  [
      "authoritative",
      "partial",
      "unavailable",
  ];

  private static readonly string[] _freshnessStates =
  [
      "current",
      "partial",
      "retained",
      "stale",
      "unavailable",
  ];

  private static readonly string[] _authorities =
  [
      "credential-derived",
      "dashboard-accepted-sync",
      "dashboard-authorization",
      "dashboard-request",
      "local-broker",
      "local-broker-policy",
      "manager",
      "not-authoritative",
      "verified-node-result",
  ];

  private static readonly string[] _signalTruth =
  [
      "false",
      "true",
      "unknown",
  ];

  private static readonly Dictionary<int, (int Visible, bool Truncated)>
      _cardinalities = new()
      {
        [0] = (0, false),
        [1] = (1, false),
        [62] = (62, false),
        [200] = (200, false),
        [201] = (200, true),
      };

  internal static string? FindError(FleetTrustScenarioSet corpus)
  {
    if (corpus.SchemaVersion != 1)
    {
      return $"schemaVersion '{corpus.SchemaVersion}' is unsupported; expected 1.";
    }
    if (!HasRequiredStrings(corpus.CurrentBehavior))
    {
      return "currentBehavior must contain non-empty unique entries.";
    }

    return ValidateFleet(corpus.FleetEvidence) ??
        ValidateCardinalities(corpus.IncidentCardinalities) ??
        ValidateConditions(corpus.ConditionDecomposition) ??
        SupportDiagnosticScenarioValidator.FindError(
            corpus.SupportDiagnostics);
  }

  private static string? ValidateFleet(
      IReadOnlyList<FleetEvidenceScenario>? scenarios)
  {
    string[] expectedIds =
    [
      "fresh-contact-stale-manager",
      "identity-mismatch",
      "reporting-current-manager",
      "reporting-loss-retained-evidence",
      "true-resolution",
    ];
    var idError = ValidateIds(scenarios, expectedIds, "fleet scenario");
    if (idError is not null)
    {
      return idError;
    }

    foreach (var scenario in scenarios!)
    {
      if (!HasText(scenario.Description) ||
          !HasText(scenario.ExpectedInterpretation))
      {
        return $"fleet scenario '{scenario.Id}' has a missing required string.";
      }
      if (scenario.ConnectorReporting is not ("current" or "lost"))
      {
        return $"fleet scenario '{scenario.Id}' has invalid connectorReporting '{scenario.ConnectorReporting}'.";
      }
      var clockError = ValidateClocks(
          scenario.Clocks,
          $"fleet scenario '{scenario.Id}'");
      if (clockError is not null)
      {
        return clockError;
      }
      var claimError = ValidateClaims(
          scenario.Claims,
          $"fleet scenario '{scenario.Id}'");
      if (claimError is not null)
      {
        return claimError;
      }
    }

    var resolution = scenarios.Single(
        scenario => scenario.Id == "true-resolution");
    if (!resolution.TrueResolutionProven ||
        scenarios.Count(scenario => scenario.TrueResolutionProven) != 1)
    {
      return "fleet scenarios must prove true resolution exactly once.";
    }
    if (scenarios.Single(scenario =>
            scenario.Id == "reporting-loss-retained-evidence")
        .TrueResolutionProven)
    {
      return "reporting loss cannot prove true resolution.";
    }
    return ValidateFleetInvariants(scenarios);
  }

  private static string? ValidateFleetInvariants(
      IReadOnlyList<FleetEvidenceScenario> scenarios)
  {
    var current = scenarios.Single(
        scenario => scenario.Id == "reporting-current-manager");
    if (current.Claims.Any(claim =>
            claim.State != "authoritative" ||
            claim.Freshness != "current"))
    {
      return "reporting-current-manager must contain current authoritative claims.";
    }

    var reportingLoss = scenarios.Single(
        scenario => scenario.Id == "reporting-loss-retained-evidence");
    var lostConnector = Claim(reportingLoss, "connector-contact");
    var retainedManager = Claim(reportingLoss, "manager-observation");
    var retainedWorkload = Claim(reportingLoss, "workload-evidence");
    if (lostConnector?.State != "unavailable" ||
        retainedManager?.Freshness != "retained" ||
        retainedWorkload?.Freshness != "retained")
    {
      return "reporting-loss-retained-evidence must preserve retained manager and workload claims.";
    }

    var staleManager = scenarios.Single(
        scenario => scenario.Id == "fresh-contact-stale-manager");
    var freshConnector = Claim(staleManager, "connector-contact");
    var oldManager = Claim(staleManager, "manager-observation");
    var oldWorkload = Claim(staleManager, "workload-evidence");
    if (freshConnector?.Freshness != "current" ||
        oldManager?.Freshness != "stale" ||
        oldWorkload?.Freshness != "stale")
    {
      return "fresh-contact-stale-manager must keep connector and manager freshness independent.";
    }

    var mismatch = scenarios.Single(
        scenario => scenario.Id == "identity-mismatch");
    var connectorIdentity = Claim(mismatch, "connector-identity");
    var payloadIdentity = Claim(mismatch, "payload-identity");
    if (connectorIdentity?.Authority !=
            "credential-derived" ||
        payloadIdentity?.State != "unavailable")
    {
      return "identity-mismatch must retain credential-derived authority.";
    }

    var resolution = scenarios.Single(
        scenario => scenario.Id == "true-resolution");
    var condition = Claim(resolution, "condition-state");
    return condition?.State == "authoritative" &&
        condition.Freshness == "current" &&
        condition.Value == "cleared"
        ? null
        : "true-resolution requires fresh authoritative clearing evidence.";
  }

  private static string? ValidateCardinalities(
      IReadOnlyList<IncidentCardinalityScenario>? scenarios)
  {
    if (scenarios is null || scenarios.Count != _cardinalities.Count)
    {
      return "incident cardinality scenarios must contain exactly five cases.";
    }
    var ids = new List<string>();
    var counts = new HashSet<int>();
    foreach (var scenario in scenarios)
    {
      if (scenario is null)
      {
        return "incident cardinality scenarios cannot contain null entries.";
      }
      if (!HasText(scenario.Id) ||
          ids.Contains(scenario.Id, StringComparer.Ordinal))
      {
        return "incident cardinality scenario IDs must be non-empty and unique.";
      }
      ids.Add(scenario.Id);
      var expectedCount = scenario.Id switch
      {
        "incidents-0" => 0,
        "incidents-1" => 1,
        "incidents-62" => 62,
        "incidents-200" => 200,
        "incidents-over-200" => 201,
        _ => -1,
      };
      if (scenario.IncidentCount != expectedCount)
      {
        return $"incident cardinality '{scenario.Id}' has the wrong count.";
      }
      if (!counts.Add(scenario.IncidentCount) ||
          !_cardinalities.TryGetValue(
              scenario.IncidentCount,
              out var expected))
      {
        return $"incident cardinality '{scenario.Id}' has unsupported count '{scenario.IncidentCount}'.";
      }
      if (scenario.QueryLimit != 200 ||
          scenario.ExpectedVisibleCount != expected.Visible ||
          scenario.ExpectedTruncated != expected.Truncated ||
          scenario.AuthoritativeTotalAvailable)
      {
        return $"incident cardinality '{scenario.Id}' does not match the exact 200-record boundary.";
      }
    }
    return counts.SetEquals(_cardinalities.Keys)
        ? null
        : "incident cardinalities must be exactly 0, 1, 62, 200, and 201.";
  }

  private static string? ValidateConditions(
      IReadOnlyList<ConditionScenario>? scenarios)
  {
    string[] expectedIds =
    [
      "independent-conditions",
      "repeated-persistent-condition",
    ];
    var idError = ValidateIds(
        scenarios,
        expectedIds,
        "condition scenario");
    if (idError is not null)
    {
      return idError;
    }

    var signalIds = new List<string>();
    foreach (var scenario in scenarios!)
    {
      if (scenario.Signals is null || scenario.Signals.Count == 0)
      {
        return $"condition scenario '{scenario.Id}' must contain signals.";
      }
      var conditionKeys = new List<string>();
      foreach (var signal in scenario.Signals)
      {
        if (signal is null)
        {
          return $"condition scenario '{scenario.Id}' cannot contain a null signal.";
        }
        if (!HasText(signal.SignalId) ||
            signalIds.Contains(signal.SignalId, StringComparer.Ordinal))
        {
          return $"condition scenario '{scenario.Id}' has a missing or duplicate signal ID.";
        }
        signalIds.Add(signal.SignalId);
        if (!HasText(signal.ConditionKey))
        {
          return $"signal '{signal.SignalId}' has a missing condition key.";
        }
        if (!conditionKeys.Contains(
            signal.ConditionKey,
            StringComparer.Ordinal))
        {
          conditionKeys.Add(signal.ConditionKey);
        }
        if (!_signalTruth.Contains(signal.Truth, StringComparer.Ordinal))
        {
          return $"signal '{signal.SignalId}' has invalid truth '{signal.Truth}'.";
        }
        if (signal.ObservedAt == default ||
            signal.EvaluatedAt == default ||
            signal.ObservedAt >= signal.EvaluatedAt)
        {
          return $"signal '{signal.SignalId}' has invalid clock ordering.";
        }
      }
      if (scenario.ExpectedConditionCount != conditionKeys.Count ||
          scenario.ExpectedIncidentCount != conditionKeys.Count)
      {
        return $"condition scenario '{scenario.Id}' does not preserve one incident per original condition.";
      }
      if (scenario.Id == "repeated-persistent-condition" &&
          (conditionKeys.Count != 1 || scenario.Signals.Count < 2))
      {
        return "repeated persistent signals must share one condition key.";
      }
      if (scenario.Id == "independent-conditions" &&
          conditionKeys.Count != scenario.Signals.Count)
      {
        return "independent condition signals must use distinct condition keys.";
      }
    }
    return null;
  }

  internal static string? ValidateClaims(
      IReadOnlyList<EvidenceClaim>? claims,
      string context)
  {
    if (claims is null || claims.Count == 0)
    {
      return $"{context} must contain claims.";
    }
    var names = new List<string>();
    foreach (var claim in claims)
    {
      if (claim is null)
      {
        return $"{context} cannot contain a null claim.";
      }
      if (!HasText(claim.Name) ||
          names.Contains(claim.Name, StringComparer.Ordinal))
      {
        return $"{context} has a missing or duplicate claim name.";
      }
      names.Add(claim.Name);
      if (!_claimStates.Contains(claim.State, StringComparer.Ordinal))
      {
        return $"{context} claim '{claim.Name}' has invalid state '{claim.State}'.";
      }
      if (!_authorities.Contains(claim.Authority, StringComparer.Ordinal))
      {
        return $"{context} claim '{claim.Name}' has invalid authority '{claim.Authority}'.";
      }
      if (!_freshnessStates.Contains(
          claim.Freshness,
          StringComparer.Ordinal))
      {
        return $"{context} claim '{claim.Name}' has invalid freshness '{claim.Freshness}'.";
      }
      var unavailable = claim.State == "unavailable";
      if (unavailable == HasText(claim.Value) ||
          unavailable != (claim.Freshness == "unavailable"))
      {
        return $"{context} claim '{claim.Name}' has inconsistent unavailable state and value.";
      }
      if ((claim.State == "partial") !=
          (claim.Freshness == "partial") ||
          claim.State == "authoritative" &&
          claim.Freshness is "partial" or "unavailable")
      {
        return $"{context} claim '{claim.Name}' has inconsistent state and freshness.";
      }
      if (!unavailable &&
          (claim.SourceObservedAt is null ||
           claim.DashboardReceivedAt is null))
      {
        return $"{context} claim '{claim.Name}' is missing source or receipt time.";
      }
      if (claim.SourceObservedAt is not null &&
          claim.DashboardReceivedAt is not null &&
          claim.SourceObservedAt > claim.DashboardReceivedAt)
      {
        return $"{context} claim '{claim.Name}' has invalid source/receipt ordering.";
      }
    }
    return null;
  }

  internal static string? ValidateClocks(
      ScenarioClocks? clocks,
      string context)
  {
    if (clocks is null ||
        clocks.SourceObservedAt == default ||
        clocks.DashboardReceivedAt == default ||
        clocks.EvaluatedAt == default ||
        clocks.ResponseGeneratedAt == default ||
        clocks.SourceObservedAt >= clocks.DashboardReceivedAt ||
        clocks.DashboardReceivedAt >= clocks.EvaluatedAt ||
        clocks.EvaluatedAt >= clocks.ResponseGeneratedAt)
    {
      return $"{context} has invalid four-clock ordering.";
    }
    return null;
  }

  internal static string? ValidateIds<T>(
      IReadOnlyList<T>? scenarios,
      IReadOnlyList<string> expectedIds,
      string context)
      where T : class
  {
    if (scenarios is null || scenarios.Count != expectedIds.Count)
    {
      return $"{context} collection has an invalid count.";
    }
    var ids = new List<string>();
    foreach (var scenario in scenarios)
    {
      var id = scenario switch
      {
        FleetEvidenceScenario fleet => fleet.Id,
        ConditionScenario condition => condition.Id,
        SupportDiagnosticScenario support => support.Id,
        _ => null,
      };
      if (!HasText(id) ||
          ids.Contains(id, StringComparer.Ordinal))
      {
        return $"{context} IDs must be non-empty and unique; duplicate '{id}'.";
      }
      ids.Add(id!);
    }
    return ids.Count == expectedIds.Count &&
        expectedIds.All(expected =>
            ids.Contains(expected, StringComparer.Ordinal))
        ? null
        : $"{context} IDs do not match the closed version 1 corpus.";
  }

  private static bool HasRequiredStrings(
      IReadOnlyList<string>? values) =>
      values is not null &&
      values.Count > 0 &&
      values.All(HasText) &&
      values.Distinct(StringComparer.Ordinal).Count() == values.Count;

  internal static bool HasText(string? value) =>
      !string.IsNullOrWhiteSpace(value) && value.Length <= 512;

  private static EvidenceClaim? Claim(
      FleetEvidenceScenario scenario,
      string name) =>
      scenario.Claims.SingleOrDefault(claim =>
          string.Equals(claim.Name, name, StringComparison.Ordinal));
}
