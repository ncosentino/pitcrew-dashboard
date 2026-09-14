using System.Text.Json;

namespace PitCrew.Dashboard.Trust.Scenarios;

internal static class SupportDiagnosticScenarioValidator
{
  private static readonly string[] _diagnosticModes =
  [
      "CapacityMismatch",
      "ConnectorOffline",
      "Full",
      "HostPressure",
      "JobNotAssigned",
  ];

  internal static string? FindError(
      IReadOnlyList<SupportDiagnosticScenario>? scenarios)
  {
    string[] expectedIds =
    [
      "explicit-profile-success",
      "invalid-diagnostic-scope",
      "leave-return-exact-result",
      "omitted-profile-ambiguity",
      "partial-evidence",
      "policy-rejection",
      "timeout",
    ];
    var idError = FleetTrustScenarioValidator.ValidateIds(
        scenarios,
        expectedIds,
        "support scenario");
    if (idError is not null)
    {
      return idError;
    }

    var sessionIds = new HashSet<Guid>();
    foreach (var scenario in scenarios!)
    {
      if (scenario.SessionId == Guid.Empty ||
          !sessionIds.Add(scenario.SessionId))
      {
        return $"support scenario '{scenario.Id}' has an empty or duplicate session ID.";
      }
      var expectedMode = scenario.Id switch
      {
        "explicit-profile-success" => "ConnectorOffline",
        "omitted-profile-ambiguity" => "ConnectorOffline",
        "policy-rejection" => "Full",
        "timeout" => "HostPressure",
        "partial-evidence" => "Full",
        "invalid-diagnostic-scope" => "JobNotAssigned",
        "leave-return-exact-result" => "CapacityMismatch",
        _ => null,
      };
      if (!_diagnosticModes.Contains(
              scenario.DiagnosticMode,
              StringComparer.Ordinal) ||
          !string.Equals(
              scenario.DiagnosticMode,
              expectedMode,
              StringComparison.Ordinal) ||
          !FleetTrustScenarioValidator.HasText(
              scenario.ExpectedInterpretation))
      {
        return $"support scenario '{scenario.Id}' has an invalid mode or interpretation.";
      }
      var expectedProfile = scenario.Id switch
      {
        "omitted-profile-ambiguity" => null,
        "invalid-diagnostic-scope" => "restricted",
        _ => "default",
      };
      if (expectedProfile is null)
      {
        if (scenario.ProfileId is not null)
        {
          return "omitted-profile-ambiguity must omit profileId.";
        }
      }
      else if (!string.Equals(
          scenario.ProfileId,
          expectedProfile,
          StringComparison.Ordinal))
      {
        return $"support scenario '{scenario.Id}' has an invalid profileId.";
      }
      var clockError = FleetTrustScenarioValidator.ValidateClocks(
          scenario.Clocks,
          $"support scenario '{scenario.Id}'");
      if (clockError is not null)
      {
        return clockError;
      }
      var claimError = FleetTrustScenarioValidator.ValidateClaims(
          scenario.Claims,
          $"support scenario '{scenario.Id}'");
      if (claimError is not null)
      {
        return claimError;
      }
      var outcomeError = ValidateOutcome(scenario);
      if (outcomeError is not null)
      {
        return outcomeError;
      }
    }
    return null;
  }

  private static string? ValidateOutcome(
      SupportDiagnosticScenario scenario)
  {
    var expectedOutcome = scenario.Id switch
    {
      "explicit-profile-success" => "completed",
      "omitted-profile-ambiguity" => "completed",
      "policy-rejection" => "rejected",
      "timeout" => "timed-out",
      "partial-evidence" => "partial",
      "invalid-diagnostic-scope" => "forbidden",
      "leave-return-exact-result" => "completed",
      _ => null,
    };
    if (!string.Equals(
        scenario.Outcome,
        expectedOutcome,
        StringComparison.Ordinal))
    {
      return $"support scenario '{scenario.Id}' has invalid outcome '{scenario.Outcome}'.";
    }
    var hasResult = scenario.Outcome is "completed" or "partial";
    var expectedStage = scenario.Outcome switch
    {
      "completed" or "partial" => "none",
      "rejected" or "timed-out" => "local-broker",
      "forbidden" => "dashboard-authorization",
      _ => null,
    };
    if (expectedStage is null ||
        !string.Equals(
            scenario.FailureStage,
            expectedStage,
            StringComparison.Ordinal))
    {
      return $"support scenario '{scenario.Id}' has an inconsistent outcome or failure stage.";
    }
    if (hasResult != (scenario.Result is not null))
    {
      return $"support scenario '{scenario.Id}' has inconsistent result availability.";
    }
    if (hasResult && scenario.RejectionDisposition is not null ||
        !hasResult &&
        !FleetTrustScenarioValidator.HasText(
            scenario.RejectionDisposition))
    {
      return $"support scenario '{scenario.Id}' has inconsistent rejection data.";
    }
    if (scenario.Result is not null)
    {
      var resultError = ValidateResult(scenario, scenario.Result);
      if (resultError is not null)
      {
        return resultError;
      }
    }

    var expectedReturnContext = scenario.Id == "leave-return-exact-result"
        ? "fresh-tenant-session-read"
        : "not-applicable";
    if (!string.Equals(
        scenario.ReturnContext,
        expectedReturnContext,
        StringComparison.Ordinal))
    {
      return $"support scenario '{scenario.Id}' has inconsistent return context.";
    }
    if (scenario.Id == "policy-rejection" &&
        scenario.RejectionDisposition != "broker-evidence-access-denied" ||
        scenario.Id == "timeout" &&
        scenario.RejectionDisposition != "broker-timeout" ||
        scenario.Id == "invalid-diagnostic-scope" &&
        scenario.RejectionDisposition != "diagnostic-scope-forbidden")
    {
      return $"support scenario '{scenario.Id}' has an invalid rejection disposition.";
    }
    return null;
  }

  private static string? ValidateResult(
      SupportDiagnosticScenario scenario,
      SupportResultScenario result)
  {
    if (!FleetTrustScenarioValidator.HasText(result.ReportJson) ||
        result.ReportJson.Length > 16_384 ||
        !FleetTrustScenarioValidator.HasText(result.Markdown) ||
        result.Markdown.Length > 4_096)
    {
      return $"support scenario '{scenario.Id}' has missing or oversized result content.";
    }
    try
    {
      using var document = JsonDocument.Parse(result.ReportJson);
      if (document.RootElement.ValueKind != JsonValueKind.Object ||
          !document.RootElement.TryGetProperty(
              "schemaVersion",
              out var schemaVersion) ||
          schemaVersion.ValueKind != JsonValueKind.Number ||
          !schemaVersion.TryGetInt32(out var version) ||
          version != 1 ||
          !document.RootElement.TryGetProperty(
              "profile",
              out var profile) ||
          profile.ValueKind != JsonValueKind.String ||
          !FleetTrustScenarioValidator.HasText(profile.GetString()))
      {
        return $"support scenario '{scenario.Id}' result report has an invalid identity shape.";
      }
      return scenario.ProfileId is null ||
          string.Equals(
              profile.GetString(),
              scenario.ProfileId,
              StringComparison.Ordinal)
          ? null
          : $"support scenario '{scenario.Id}' result profile does not match the session.";
    }
    catch (JsonException)
    {
      return $"support scenario '{scenario.Id}' result report is malformed JSON.";
    }
  }
}
