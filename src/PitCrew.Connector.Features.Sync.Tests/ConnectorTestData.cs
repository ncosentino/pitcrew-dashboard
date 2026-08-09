using System.Text.Json.Nodes;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

internal static class ConnectorTestData
{
  public static JsonObject CreateHostAdmissionPayload() =>
      new()
      {
        ["status"] = "available",
        ["namespace"] = "primary",
        ["epoch"] = 3,
        ["decisionSequence"] = 42,
        ["capacityUnits"] = 12,
        ["safetyMarginUnits"] = 2,
        ["effectiveTotalUnits"] = 10,
        ["availableUnits"] = 4,
        ["hostPolicyFingerprint"] = new string('a', 64),
        ["accounting"] = new JsonObject
        {
          ["unitCost"] = 2,
          ["reservedUnits"] = 4,
          ["borrowable"] = false,
          ["profilePolicyFingerprint"] = new string('b', 64),
          ["activeUnits"] = 2,
          ["provisionalUnits"] = 0,
          ["heldUnits"] = 2,
          ["borrowedUnits"] = 0,
          ["pendingUnits"] = 4,
          ["withheldUnits"] = 4,
        },
        ["lastDecision"] = null,
      };

  public static ManagerObservedState CreateObservedState(
      string profileId,
      DateTimeOffset observedAt) =>
      new(
          1,
          10,
          profileId,
          "manager-instance",
          "running",
          observedAt,
          "repo",
          1,
          new string('a', 64),
          "accepted",
          1,
          1,
          0,
          [
              new ObservedSlotState(
                    "repo-example-000001",
                    "https://github.com/example/project",
                    true,
                    true,
                    "online",
                    0,
                    0,
                    observedAt,
                    new ResourceUsage(
                        0.75,
                        536_870_912,
                        42),
                    "busy",
                    "scale-set-linux",
                    "connected"),
          ],
          new ManagerResourceTelemetry(
              observedAt,
              "available",
              new HostResourceCapacity(
                  8,
                  17_179_869_184),
              new ResourceUsage(
                  0.25,
                  134_217_728,
                  9)),
          30,
          new ManagerAutoscalingState(
              "scale-set",
              "running",
              0,
              30,
              1,
              2,
              1,
              1,
              0,
              1,
              300,
              1,
              observedAt.AddMinutes(5),
              null),
          1);

  public static ConnectorOptions CreateOptions(
      string stateRoot,
      string identityPath) =>
      new()
      {
        DashboardUrl = "https://dashboard.example",
        EnrollmentCode = "test-enrollment-code",
        DisplayName = "Test Server",
        StateRoot = stateRoot,
        IdentityPath = identityPath,
      };
}
