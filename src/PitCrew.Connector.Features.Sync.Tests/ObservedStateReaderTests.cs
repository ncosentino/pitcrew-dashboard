using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ObservedStateReaderTests
{
  [Test]
  public async Task ReadAsync_Retains_Last_Good_State_And_Detects_Removal(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var profileDirectory = Directory.CreateDirectory(
          Path.Combine(root, "default"));
      var observedStatePath = Path.Combine(
          profileDirectory.FullName,
          "observed-state.json");
      var observedState = ConnectorTestData.CreateObservedState(
          "default",
          DateTimeOffset.UtcNow);
      var serializedObservedState = JsonSerializer.Serialize(
          observedState,
          PitCrewProtocolJsonContext.Default.ManagerObservedState);
      using var observedStateJson = JsonDocument.Parse(
          serializedObservedState);
      var resourceTelemetry = observedStateJson.RootElement.GetProperty(
          "resourceTelemetry");
      var autoscaling = observedStateJson.RootElement.GetProperty(
          "autoscaling");
      var managerResources = resourceTelemetry.GetProperty("manager");
      var serializedSlot = observedStateJson.RootElement
          .GetProperty("slots")[0];
      var slotResources = serializedSlot.GetProperty("resources");
      await Assert.That(
              observedStateJson.RootElement
                  .GetProperty("configuredSlots")
                  .GetInt32())
          .IsEqualTo(30);
      await Assert.That(
              observedStateJson.RootElement
                  .GetProperty("eligibleSlots")
                  .GetInt32())
          .IsEqualTo(1);
      await Assert.That(autoscaling.GetProperty("mode").GetString())
          .IsEqualTo("scale-set");
      await Assert.That(autoscaling.GetProperty("maximumSlots").GetInt32())
          .IsEqualTo(30);
      await Assert.That(serializedSlot.GetProperty("activity").GetString())
          .IsEqualTo("busy");
      await Assert.That(serializedSlot.GetProperty("target").GetString())
          .IsEqualTo("scale-set-linux");
      await Assert.That(
              serializedSlot.GetProperty("registrationStatus").GetString())
          .IsEqualTo("connected");
      await Assert.That(
              managerResources.GetProperty("cpuCores").GetDouble())
          .IsEqualTo(0.25);
      await Assert.That(
              managerResources.GetProperty(
                  "memoryWorkingSetBytes").GetInt64())
          .IsEqualTo(134_217_728);
      await Assert.That(managerResources.GetProperty("pids").GetInt32())
          .IsEqualTo(9);
      await Assert.That(slotResources.GetProperty("cpuCores").GetDouble())
          .IsEqualTo(0.75);
      await File.WriteAllTextAsync(
          observedStatePath,
          serializedObservedState,
          cancellationToken);
      var options = Options.Create(
          ConnectorTestData.CreateOptions(
              root,
              Path.Combine(root, "identity.json")));
      var reader = new ObservedStateReader(
          options,
          NullLogger<ObservedStateReader>.Instance);

      var initial = await reader.ReadAsync(cancellationToken);
      await Assert.That(initial.IsComplete).IsTrue();
      await Assert.That(initial.Profiles).HasSingleItem();
      await Assert.That(initial.AggregateHash).IsNotEmpty();
      await Assert.That(initial.Profiles[0].ResourceTelemetry)
          .IsEqualTo(observedState.ResourceTelemetry);
      await Assert.That(initial.Profiles[0].ConfiguredSlots)
          .IsEqualTo(observedState.ConfiguredSlots);
      await Assert.That(initial.Profiles[0].Autoscaling)
          .IsEqualTo(observedState.Autoscaling);
      await Assert.That(initial.Profiles[0].EligibleSlots)
          .IsEqualTo(observedState.EligibleSlots);
      await Assert.That(initial.Profiles[0].Slots[0].Resources)
          .IsEqualTo(observedState.Slots[0].Resources);
      await Assert.That(initial.Profiles[0].Slots[0].Activity)
          .IsEqualTo(observedState.Slots[0].Activity);
      await Assert.That(initial.Profiles[0].Slots[0].Target)
          .IsEqualTo(observedState.Slots[0].Target);
      await Assert.That(initial.Profiles[0].Slots[0].RegistrationStatus)
          .IsEqualTo(observedState.Slots[0].RegistrationStatus);

      await File.WriteAllTextAsync(
          observedStatePath,
          "{",
          cancellationToken);
      var retained = await reader.ReadAsync(cancellationToken);
      await Assert.That(retained.IsComplete).IsFalse();
      await Assert.That(retained.AggregateHash)
          .IsNotEqualTo(initial.AggregateHash);
      await Assert.That(retained.Profiles[0])
          .IsEqualTo(initial.Profiles[0]);
      await Assert.That(retained.Inventory?.Coverage)
          .IsEqualTo("partial");
      await Assert.That(retained.Inventory?.UnavailableReason)
          .IsEqualTo(
              ConnectorHealthFailureCategories.ProfileStateInvalid);

      Directory.Delete(profileDirectory.FullName, true);
      var removed = await reader.ReadAsync(cancellationToken);
      await Assert.That(removed.IsComplete).IsTrue();
      await Assert.That(removed.Profiles).IsEmpty();
      await Assert.That(removed.AggregateHash)
          .IsNotEqualTo(initial.AggregateHash);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadAsync_Missing_Profile_State_After_Restart_Is_Unavailable(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var profileDirectory = Directory.CreateDirectory(
          Path.Combine(root, "default"));
      var observedStatePath = Path.Combine(
          profileDirectory.FullName,
          "observed-state.json");
      var observedState = ConnectorTestData.CreateObservedState(
          "default",
          new DateTimeOffset(
              2026,
              8,
              20,
              2,
              0,
              0,
              TimeSpan.Zero));
      await File.WriteAllTextAsync(
          observedStatePath,
          JsonSerializer.Serialize(
              observedState,
              PitCrewProtocolJsonContext.Default.ManagerObservedState),
          cancellationToken);
      var options = Options.Create(
          ConnectorTestData.CreateOptions(
              root,
              Path.Combine(root, "identity.json")));
      var initialReader = new ObservedStateReader(
          options,
          NullLogger<ObservedStateReader>.Instance);

      var initial = await initialReader.ReadAsync(cancellationToken);
      await Assert.That(initial.IsComplete).IsTrue();
      await Assert.That(initial.Profiles).HasSingleItem();

      var restartedReader = new ObservedStateReader(
          options,
          NullLogger<ObservedStateReader>.Instance);
      File.Delete(observedStatePath);

      var missing = await restartedReader.ReadAsync(cancellationToken);

      await Assert.That(missing.IsComplete).IsFalse()
          .Because("an expected profile without observed state is not a complete inventory");
      await Assert.That(missing.Profiles).IsEmpty();
      await Assert.That(missing.Failure?.Category)
          .IsEqualTo(
              ConnectorHealthFailureCategories.ProfileStateUnreadable);
      await Assert.That(missing.Failure?.Detail)
          .IsEqualTo("Profile observed state could not be read.");
      await Assert.That(missing.Inventory?.Coverage)
          .IsEqualTo("unavailable");
      await Assert.That(missing.Inventory?.UnavailableReason)
          .IsEqualTo(
              ConnectorHealthFailureCategories.ProfileStateUnreadable);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadAsync_Accepts_Legacy_Payload_Without_Additive_Fields(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var profileDirectory = Directory.CreateDirectory(
          Path.Combine(root, "legacy"));
      var observedState = ConnectorTestData.CreateObservedState(
          "legacy",
          DateTimeOffset.UtcNow);
      var payload = JsonNode.Parse(
          JsonSerializer.Serialize(
              observedState,
              PitCrewProtocolJsonContext.Default.ManagerObservedState))?
          .AsObject() ??
          throw new InvalidOperationException(
              "The observed-state payload could not be represented as JSON.");
      payload["managerContractVersion"] = 6;
      payload.Remove("resourceTelemetry");
      payload.Remove("configuredSlots");
      payload.Remove("autoscaling");
      payload.Remove("eligibleSlots");
      foreach (var slot in payload["slots"]!.AsArray())
      {
        var slotObject = slot!.AsObject();
        slotObject.Remove("resources");
        slotObject.Remove("activity");
        slotObject.Remove("target");
        slotObject.Remove("registrationStatus");
      }
      await File.WriteAllTextAsync(
          Path.Combine(
              profileDirectory.FullName,
              "observed-state.json"),
          payload.ToJsonString(),
          cancellationToken);
      var reader = new ObservedStateReader(
          Options.Create(
              ConnectorTestData.CreateOptions(
                  root,
                  Path.Combine(root, "identity.json"))),
          NullLogger<ObservedStateReader>.Instance);

      var result = await reader.ReadAsync(cancellationToken);

      await Assert.That(result.IsComplete)
          .IsTrue()
          .Because("legacy observed-state payloads remain compatible");
      await Assert.That(result.Profiles).HasSingleItem();
      await Assert.That(result.Profiles[0].ResourceTelemetry).IsNull();
      await Assert.That(result.Profiles[0].ConfiguredSlots).IsNull();
      await Assert.That(result.Profiles[0].Autoscaling).IsNull();
      await Assert.That(result.Profiles[0].EligibleSlots).IsNull();
      await Assert.That(result.Profiles[0].Slots).HasSingleItem();
      await Assert.That(result.Profiles[0].Slots[0].Resources).IsNull();
      await Assert.That(result.Profiles[0].Slots[0].Activity).IsNull();
      await Assert.That(result.Profiles[0].Slots[0].Target).IsNull();
      await Assert.That(result.Profiles[0].Slots[0].RegistrationStatus)
          .IsNull();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  [Arguments("empty-slot")]
  [Arguments("incomplete-manager")]
  [Arguments("incomplete-autoscaling")]
  [Arguments("invalid-scale-down-at")]
  [Arguments("missing-current-job")]
  [Arguments("missing-host-pressure")]
  [Arguments("missing-host-admission")]
  [Arguments("null-host-admission")]
  [Arguments("incomplete-host-admission")]
  [Arguments("incomplete-contract-nineteen-accounting")]
  [Arguments("incomplete-contract-twenty-one-sources")]
  public async Task ReadAsync_Rejects_Incomplete_Or_Invalid_Additive_Objects(
      string scenario,
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var profileDirectory = Directory.CreateDirectory(
          Path.Combine(root, "default"));
      var observedState = ConnectorTestData.CreateObservedState(
          "default",
          DateTimeOffset.UtcNow);
      var payload = JsonNode.Parse(
          JsonSerializer.Serialize(
              observedState,
              PitCrewProtocolJsonContext.Default.ManagerObservedState))?
          .AsObject() ??
          throw new InvalidOperationException(
              "The observed-state payload could not be represented as JSON.");
      switch (scenario)
      {
        case "empty-slot":
          payload["slots"]![0]!["resources"] = new JsonObject();
          break;
        case "incomplete-manager":
          payload["resourceTelemetry"]!["manager"]!
              .AsObject()
              .Remove("pids");
          break;
        case "incomplete-autoscaling":
          payload["autoscaling"]!
              .AsObject()
              .Remove("scaleSetCount");
          break;
        case "invalid-scale-down-at":
          payload["autoscaling"]!["scaleDownAt"] = "not-a-date";
          break;
        case "missing-current-job":
          payload["managerContractVersion"] = 15;
          payload["slots"]![0]!["runnerNameHash"] = new string('a', 64);
          payload["slots"]![0]!.AsObject().Remove("currentJob");
          break;
        case "missing-host-pressure":
          payload["managerContractVersion"] = 16;
          payload["slots"]![0]!["runnerNameHash"] = new string('a', 64);
          payload["slots"]![0]!["currentJob"] = null;
          payload["resourceTelemetry"]!.AsObject().Remove("hostPressure");
          break;
        case "missing-host-admission":
          payload["managerContractVersion"] = 18;
          payload["slots"]![0]!["runnerNameHash"] = new string('a', 64);
          payload["slots"]![0]!["currentJob"] = null;
          payload["hostAdmission"] = ConnectorTestData.CreateHostAdmissionPayload();
          payload.AsObject().Remove("hostAdmission");
          break;
        case "null-host-admission":
          payload["managerContractVersion"] = 18;
          payload["slots"]![0]!["runnerNameHash"] = new string('a', 64);
          payload["slots"]![0]!["currentJob"] = null;
          payload["hostAdmission"] = null;
          break;
        case "incomplete-host-admission":
          payload["managerContractVersion"] = 18;
          payload["slots"]![0]!["runnerNameHash"] = new string('a', 64);
          payload["slots"]![0]!["currentJob"] = null;
          payload["hostAdmission"] = ConnectorTestData.CreateHostAdmissionPayload();
          payload["hostAdmission"]!.AsObject().Remove("availableUnits");
          break;
        case "incomplete-contract-nineteen-accounting":
          payload["managerContractVersion"] = 19;
          payload["slots"]![0]!["runnerNameHash"] = new string('a', 64);
          payload["slots"]![0]!["currentJob"] = null;
          payload["hostAdmission"] = ConnectorTestData.CreateHostAdmissionPayload();
          var accounting = payload["hostAdmission"]!["accounting"]!.AsObject();
          accounting["allocatableUnits"] = 4;
          accounting["allocatableWorkers"] = 2;
          accounting["theoreticalMaximumUnits"] = 10;
          accounting["theoreticalMaximumWorkers"] = 5;
          accounting["withholdingReason"] = null;
          accounting.Remove("allocatableWorkers");
          break;
        case "incomplete-contract-twenty-one-sources":
          payload["managerContractVersion"] = 21;
          var sourceObservations = new JsonObject();
          foreach (var source in new[]
          {
            "localRuntime",
            "githubScaleSet",
            "resourceTelemetry",
            "hostHardware",
            "hostAdmission",
            "subsystemHealth",
            "capacity",
          })
          {
            sourceObservations[source] = new JsonObject
            {
              ["authority"] = "pitcrew-manager",
              ["source"] = "local-runtime",
              ["sourceIdentity"] = observedState.ManagerInstanceId,
              ["observedAt"] = observedState.ObservedAt,
              ["coverage"] = "complete",
              ["retention"] = "live",
              ["reason"] = null,
            };
          }
          payload["sourceObservations"] = sourceObservations;
          break;
        default:
          throw new ArgumentOutOfRangeException(
              nameof(scenario),
              scenario,
              "Unknown incomplete-resource scenario.");
      }
      await File.WriteAllTextAsync(
          Path.Combine(
              profileDirectory.FullName,
              "observed-state.json"),
          payload.ToJsonString(),
          cancellationToken);
      var reader = new ObservedStateReader(
          Options.Create(
              ConnectorTestData.CreateOptions(
                  root,
                  Path.Combine(root, "identity.json"))),
          NullLogger<ObservedStateReader>.Instance);

      var result = await reader.ReadAsync(cancellationToken);

      await Assert.That(result.IsComplete)
          .IsFalse()
          .Because("present additive objects must contain complete, valid values");
      await Assert.That(result.Profiles).IsEmpty();
      await Assert.That(result.Failure).IsNotNull();
      await Assert.That(result.Failure!.Category)
          .IsEqualTo(
              ConnectorHealthFailureCategories.ProfileStateInvalid);
      await Assert.That(result.Failure.ProfileId)
          .IsEqualTo("default");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadAsync_Rejects_Oversized_First_Snapshot(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var profileDirectory = Directory.CreateDirectory(
          Path.Combine(root, "default"));
      await File.WriteAllTextAsync(
          Path.Combine(
              profileDirectory.FullName,
              "observed-state.json"),
          new string('x', 2048),
          cancellationToken);
      var connectorOptions = ConnectorTestData.CreateOptions(
          root,
          Path.Combine(root, "identity.json"));
      connectorOptions.MaximumObservedStateBytes = 1024;
      var reader = new ObservedStateReader(
          Options.Create(connectorOptions),
          NullLogger<ObservedStateReader>.Instance);

      var result = await reader.ReadAsync(cancellationToken);

      await Assert.That(result.IsComplete).IsFalse();
      await Assert.That(result.Profiles).IsEmpty();
      await Assert.That(result.Failure).IsNotNull();
      await Assert.That(result.Failure!.Category)
          .IsEqualTo(
              ConnectorHealthFailureCategories.ProfileStateInvalid);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadAsync_Reports_Missing_State_Root_Without_Exposing_Path(
      CancellationToken cancellationToken)
  {
    var root = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-missing-state-{Guid.NewGuid():N}");
    var reader = new ObservedStateReader(
        Options.Create(
            ConnectorTestData.CreateOptions(
                root,
                Path.Combine(
                    Path.GetTempPath(),
                    "identity.json"))),
        NullLogger<ObservedStateReader>.Instance);

    var result = await reader.ReadAsync(cancellationToken);

    await Assert.That(result.IsComplete)
        .IsFalse()
        .Because("a missing state root prevents a complete connector snapshot");
    await Assert.That(result.Failure).IsNotNull();
    await Assert.That(result.Failure!.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.StateRootMissing);
    await Assert.That(result.Failure.Detail)
        .IsEqualTo("PitCrew state root is unavailable.");
    await Assert.That(result.Failure.Detail)
        .DoesNotContain(root);
    await Assert.That(result.Inventory).IsNotNull();
    await Assert.That(result.Inventory!.Coverage)
        .IsEqualTo("unavailable");
    await Assert.That(result.Inventory.UnavailableReason)
        .IsEqualTo(
            ConnectorHealthFailureCategories.StateRootMissing);
  }

  [Test]
  public async Task ReadAsync_Does_Not_Treat_Unreadable_State_As_Deleted(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var profileDirectory = Directory.CreateDirectory(
          Path.Combine(root, "default"));
      Directory.CreateDirectory(
          Path.Combine(
              profileDirectory.FullName,
              "observed-state.json"));
      var reader = new ObservedStateReader(
          Options.Create(
              ConnectorTestData.CreateOptions(
                  root,
                  Path.Combine(root, "identity.json"))),
          NullLogger<ObservedStateReader>.Instance);

      var result = await reader.ReadAsync(cancellationToken);

      await Assert.That(result.IsComplete)
          .IsFalse()
          .Because("an unreadable snapshot is not evidence of profile deletion");
      await Assert.That(result.Profiles).IsEmpty();
      await Assert.That(result.Failure).IsNotNull();
      await Assert.That(result.Failure!.Category)
          .IsEqualTo(
              ConnectorHealthFailureCategories.ProfileStateUnreadable);
      await Assert.That(result.Failure.ProfileId)
          .IsEqualTo("default");
      await Assert.That(result.Inventory).IsNotNull();
      await Assert.That(result.Inventory!.Coverage)
          .IsEqualTo("unavailable");
      await Assert.That(result.Inventory.UnavailableReason)
          .IsEqualTo(
              ConnectorHealthFailureCategories.ProfileStateUnreadable);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-connector-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
  }

}
