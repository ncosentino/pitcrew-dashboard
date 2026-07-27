using System.Text.Json;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

internal static class RecoveryTestData
{
  public const string DefaultProfileId = "default";

  public static readonly DateTimeOffset Now =
      new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

  public static string DesiredStateHash { get; } = new('b', 64);

  public static ConnectorOptions CreateRecoveryOptions(string root)
  {
    var options = CapacityTestData.CreateOperatorOptions(root);
    options.ManagerRecoveryEnabled = true;
    options.AllowedManagerRecoveryProfiles = [DefaultProfileId];
    options.RecoveryLedgerPath = Path.Combine(root, "recovery-ledger");
    return options;
  }

  public static async Task WriteObservedStateAsync(
      string root,
      string managerInstanceId,
      int generation,
      int managerContractVersion,
      string managerStatus,
      DateTimeOffset observedAt,
      CancellationToken cancellationToken)
  {
    await File.WriteAllTextAsync(
        Path.Combine(root, "Setup-Runner.ps1"),
        "# test setup",
        cancellationToken);
    var profileDirectory = Directory.CreateDirectory(Path.Combine(
        root,
        ".pitcrew-state",
        DefaultProfileId));
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory.FullName, "observed-state.json"),
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          managerContractVersion,
          profileId = DefaultProfileId,
          managerInstanceId,
          managerStatus,
          observedAt,
          scope = "repo",
          generation,
          desiredStateHash = DesiredStateHash,
          desiredStateStatus = "accepted",
          desiredSlots = 4,
          activeSlots = 4,
          drainingSlots = 0,
          slots = Array.Empty<object>(),
          resourceTelemetry = (object?)null,
          configuredSlots = (int?)null,
          autoscaling = (object?)null,
        }),
        cancellationToken);
  }

  public static Task WriteHealthyObservedStateAsync(
      string root,
      string managerInstanceId,
      CancellationToken cancellationToken) =>
      WriteObservedStateAsync(
          root,
          managerInstanceId,
          7,
          9,
          "running",
          Now,
          cancellationToken);

  public static void WriteShutdownRequest(string root) =>
      File.WriteAllText(
          Path.Combine(
              root,
              ".pitcrew-state",
              DefaultProfileId,
              "manager-shutdown.json"),
          "{}");

  public static RecoverManagerCommand CreateCommand(
      string managerInstanceId,
      int generation) =>
      new(
          Guid.NewGuid(),
          DefaultProfileId,
          managerInstanceId,
          generation,
          DesiredStateHash,
          Now,
          Now.AddMinutes(5));

  public static RecoverManagerCommand CreateFencedCommand() =>
      CreateCommand("manager-1", 7);
}
