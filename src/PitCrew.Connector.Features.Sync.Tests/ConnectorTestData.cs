using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

internal static class ConnectorTestData
{
  public static ManagerObservedState CreateObservedState(
      string profileId,
      DateTimeOffset observedAt) =>
      new(
          1,
          5,
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
                    observedAt),
          ]);

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
