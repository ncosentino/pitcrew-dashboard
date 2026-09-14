using System.Text.Json;
using System.Text.Json.Nodes;

namespace PitCrew.Dashboard.Trust.Scenarios.Tests;

public sealed class FleetTrustScenarioInvalidCorpusTests
{
  [Test]
  public async Task Malformed_Json_Is_Normalized_To_Invalid_Operation()
  {
    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load("{]"))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.InnerException).IsTypeOf<JsonException>();
  }

  [Test]
  public async Task Unknown_Fields_Are_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(
        root => root["unknownField"] = true);

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("unknownField");
  }

  [Test]
  public async Task Missing_Required_Fields_Are_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(
        root => root.Remove("schemaVersion"));

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("schemaVersion");
  }

  [Test]
  public async Task Unsupported_Schema_Versions_Are_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(
        root => root["schemaVersion"] = 2);

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("unsupported");
  }

  [Test]
  public async Task Empty_Required_Collections_Are_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(
        root => root["currentBehavior"] = new JsonArray());

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("currentBehavior");
  }

  [Test]
  public async Task Duplicate_Scenario_Ids_Are_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(root =>
    {
      var scenarios = root["fleetEvidence"]?.AsArray()
          ?? throw new InvalidOperationException("Missing fleet scenarios.");
      scenarios[1]!["id"] = scenarios[0]!["id"]!.GetValue<string>();
    });

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("duplicate");
  }

  [Test]
  public async Task Duplicate_Nested_Signal_Ids_Are_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(root =>
    {
      var signals = root["conditionDecomposition"]?.AsArray()[0]?["signals"]?
          .AsArray() ?? throw new InvalidOperationException(
              "Missing condition signals.");
      signals[1]!["signalId"] = signals[0]!["signalId"]!.GetValue<string>();
    });

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("signal ID");
  }

  [Test]
  public async Task Invalid_Vocabulary_Is_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(root =>
    {
      var scenario = root["fleetEvidence"]?.AsArray()[0]
          ?? throw new InvalidOperationException("Missing fleet scenario.");
      scenario["claims"]!.AsArray()[0]!["state"] = "invented";
    });

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("state");
  }

  [Test]
  public async Task Unavailable_Claims_Cannot_Carry_Values()
  {
    var json = FleetTrustScenarioTestJson.Mutate(root =>
    {
      var scenario = root["fleetEvidence"]?.AsArray()[1]
          ?? throw new InvalidOperationException("Missing fleet scenario.");
      scenario["claims"]!.AsArray()[0]!["value"] = "invented-zero";
    });

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("unavailable");
  }

  [Test]
  public async Task Invalid_Clock_Order_Is_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(root =>
    {
      var clocks = root["fleetEvidence"]?.AsArray()[0]?["clocks"]
          ?? throw new InvalidOperationException("Missing fleet clocks.");
      clocks["responseGeneratedAt"] = "2026-08-19T00:00:00+00:00";
    });

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("clock");
  }

  [Test]
  public async Task Invalid_Cardinality_Relationships_Are_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(root =>
    {
      var scenario = root["incidentCardinalities"]?.AsArray()[4]
          ?? throw new InvalidOperationException(
              "Missing incident cardinality scenario.");
      scenario["expectedVisibleCount"] = 199;
    });

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("cardinality");
  }

  [Test]
  public async Task Inconsistent_Support_Data_Is_Rejected()
  {
    var json = FleetTrustScenarioTestJson.Mutate(root =>
    {
      var scenario = root["supportDiagnostics"]?.AsArray()[0]
          ?? throw new InvalidOperationException(
              "Missing support scenario.");
      scenario["rejectionDisposition"] = "broker-timeout";
    });

    var exception = await Assert.That(
        () => FleetTrustScenarioTestJson.Load(json))
        .Throws<InvalidOperationException>();

    await Assert.That(exception!.Message).Contains("support");
  }

  [Test]
  public async Task Inconsistent_Support_Invariants_Are_Rejected()
  {
    var mutations = new Action<JsonObject>[]
    {
      root => SupportScenario(root)["sessionId"] =
          "00000000-0000-0000-0000-000000000000",
      root => SupportScenario(root)["profileId"] = null,
      root => SupportScenario(root)["result"] = null,
      root => SupportScenario(root)["failureStage"] = "local-broker",
      root => SupportScenario(root)["returnContext"] =
          "fresh-tenant-session-read",
    };
    foreach (var mutation in mutations)
    {
      var json = FleetTrustScenarioTestJson.Mutate(mutation);

      _ = await Assert.That(
          () => FleetTrustScenarioTestJson.Load(json))
          .Throws<InvalidOperationException>();
    }
  }

  private static JsonNode SupportScenario(JsonObject root) =>
      root["supportDiagnostics"]?.AsArray()[0]
      ?? throw new InvalidOperationException("Missing support scenario.");
}
