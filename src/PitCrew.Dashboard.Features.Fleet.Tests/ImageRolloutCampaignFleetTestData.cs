using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

internal static class ImageRolloutCampaignFleetTestData
{
  public static readonly DateTimeOffset Now = new(
      2026,
      8,
      29,
      12,
      0,
      0,
      TimeSpan.Zero);

  public static ImageRolloutCandidateAuthority CreateCandidate(
      string? digest = null) =>
      new(
          Guid.Parse(
              "70000000-0000-4000-8000-000000000007",
              System.Globalization.CultureInfo.InvariantCulture),
          "ubuntu-runner",
          digest ??
              "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "linux/amd64");

  public static FleetNode CreateNode(
      Guid nodeId,
      string displayName,
      bool isOnline = true,
      bool isRevoked = false) =>
      new(
          nodeId,
          displayName,
          "0.14.0",
          Now.AddDays(-1),
          Now,
          isOnline,
          isRevoked,
          false,
          [],
          [],
          []);

  public static ImageRolloutControlState CreateControl(
      string profileId = "build",
      string architecture = "linux/amd64",
      string? currentDigest = null,
      IReadOnlyList<string>? allowedRecipeIds = null,
      bool rolloutAllowed = true,
      bool localSchemaSupported = true,
      string? localFailureCategory = null,
      bool operationActive = false,
      int observedStateAgeSeconds = 5,
      DateTimeOffset? capabilityObservedAt = null) =>
      new(
          profileId,
          architecture,
          "ghcr.io/example/runner:current",
          currentDigest ??
              "sha256:1111111111111111111111111111111111111111111111111111111111111111",
          "sha256:2222222222222222222222222222222222222222222222222222222222222222",
          new string('3', 64),
          new string('4', 64),
          new string('5', 64),
          new string('6', 64),
          7,
          new string('7', 64),
          allowedRecipeIds ?? ["ubuntu-runner"],
          rolloutAllowed,
          localSchemaSupported,
          localFailureCategory,
          operationActive,
          observedStateAgeSeconds,
          capabilityObservedAt ?? Now,
          120,
          "current",
          2,
          0,
          null,
          []);
}
