using System.Text.Json;

namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// Deterministic offline observed-state writers used by rollout resolver
/// tests to exercise the closed failure branches
/// (<c>stale-observed-state</c>, <c>unsupported-architecture</c>, missing
/// convergence evidence, contradictory update targets, and missing/
/// mismatched observed <c>managerContractVersion</c>).
/// </summary>
/// <remarks>
/// Kept in a partial companion of <see cref="ImageRolloutTestData"/> so all
/// call sites remain stable while the fixture surface stays under the
/// repository matched-context file ceilings.
/// </remarks>
internal static partial class ImageRolloutTestData
{
  /// <summary>
  /// Overwrites the observed-state.json for the default profile so tests can
  /// exercise the non-null current/stale worker convergence branches
  /// (current, rolling, degraded) that WriteHealthyStateAsync does not cover
  /// on its own.
  /// </summary>
  public static async Task WriteObservedWorkersAsync(
      string root,
      int? currentWorkers,
      int? staleWorkers,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId,
      DateTimeOffset? observedAt = null,
      string status = "rolling",
      string architecture = "amd64",
      string? targetImage = null,
      string? targetImageId = null,
      string? targetRevision = null,
      int managerContractVersion = 17)
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    var payload = new Dictionary<string, object?>
    {
      ["schemaVersion"] = 1,
      ["managerContractVersion"] = managerContractVersion,
      ["profileId"] = profileId,
      ["scope"] = "repo",
      ["generation"] = 7,
      ["desiredStateHash"] = DesiredStateHash,
      ["desiredStateStatus"] = "accepted",
      ["host"] = new
      {
        hardware = new
        {
          architecture,
        },
      },
    };
    if (observedAt is not null)
    {
      payload["observedAt"] = observedAt.Value;
    }
    var update = new Dictionary<string, object?>
    {
      ["status"] = status,
      ["targetImage"] = targetImage ?? "ghcr.io/example/runner:main",
      ["targetImageId"] = targetImageId ?? CurrentLocalImageId,
      ["targetRevision"] = targetRevision ?? CurrentWorkerRevision,
      ["lastError"] = null,
    };
    if (currentWorkers is not null)
    {
      update["currentWorkers"] = currentWorkers.Value;
    }
    if (staleWorkers is not null)
    {
      update["staleWorkers"] = staleWorkers.Value;
    }
    payload["update"] = update;
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "observed-state.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }

  /// <summary>
  /// Overwrites the observed-state.json for the default profile with an
  /// invalid or missing observedAt so tests can prove the resolver fails
  /// closed rather than encoding an unknown observation as fresh state.
  /// </summary>
  public static async Task WriteObservedWithMissingObservedAtAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId,
      string? malformedObservedAt = null,
      string architecture = "amd64")
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    var payload = new Dictionary<string, object?>
    {
      ["schemaVersion"] = 1,
      ["managerContractVersion"] = 17,
      ["profileId"] = profileId,
      ["scope"] = "repo",
      ["generation"] = 7,
      ["desiredStateHash"] = DesiredStateHash,
      ["desiredStateStatus"] = "accepted",
      ["host"] = new
      {
        hardware = new
        {
          architecture,
        },
      },
      ["update"] = new
      {
        status = "current",
        targetImage = "ghcr.io/example/runner:main",
        targetImageId = CurrentLocalImageId,
        targetRevision = CurrentWorkerRevision,
        currentWorkers = DefaultRepositoryWorkers,
        staleWorkers = 0,
        lastError = (string?)null,
      },
    };
    if (malformedObservedAt is not null)
    {
      payload["observedAt"] = malformedObservedAt;
    }
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "observed-state.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }

  /// <summary>
  /// Overwrites the observed-state.json for the default profile with no
  /// <c>host.hardware.architecture</c> so tests can prove the resolver
  /// fails closed rather than fabricating <c>linux/amd64</c>.
  /// </summary>
  public static async Task WriteObservedWithMissingArchitectureAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId)
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    var payload = new
    {
      schemaVersion = 1,
      managerContractVersion = 17,
      profileId,
      observedAt = Now,
      scope = "repo",
      generation = 7,
      desiredStateHash = DesiredStateHash,
      desiredStateStatus = "accepted",
      update = new
      {
        status = "current",
        targetImage = "ghcr.io/example/runner:main",
        targetImageId = CurrentLocalImageId,
        targetRevision = CurrentWorkerRevision,
        currentWorkers = DefaultRepositoryWorkers,
        staleWorkers = 0,
        lastError = (string?)null,
      },
    };
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "observed-state.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }

  /// <summary>
  /// Overwrites observed-state.json with no <c>update</c> object at all
  /// so tests can prove the resolver treats the observation as stale
  /// rather than degraded fallback.
  /// </summary>
  public static async Task WriteObservedWithMissingUpdateAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId)
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    // Deliberately omit the "update" property. The resolver must treat
    // this as stale-observed-state; without an update it cannot verify
    // convergence.
    var payload = new
    {
      schemaVersion = 1,
      managerContractVersion = 17,
      profileId,
      observedAt = Now,
      scope = "repo",
      generation = 7,
      desiredStateHash = DesiredStateHash,
      desiredStateStatus = "accepted",
      host = new
      {
        hardware = new
        {
          architecture = "amd64",
        },
      },
    };
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "observed-state.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }

  /// <summary>
  /// Overwrites observed-state.json with a <c>rolling</c> update whose
  /// target identity contradicts the applied configuration so tests can
  /// prove the resolver validates the update target for every status
  /// (not only <c>current</c>).
  /// </summary>
  public static async Task WriteObservedWithMismatchedRollingTargetAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId)
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    var payload = new
    {
      schemaVersion = 1,
      managerContractVersion = 17,
      profileId,
      observedAt = Now,
      scope = "repo",
      generation = 7,
      desiredStateHash = DesiredStateHash,
      desiredStateStatus = "accepted",
      host = new
      {
        hardware = new
        {
          architecture = "amd64",
        },
      },
      // status is 'rolling' but every identity field contradicts the
      // applied static configuration. The resolver must not treat this
      // as fresh convergence evidence.
      update = new
      {
        status = "rolling",
        targetImage = "ghcr.io/example/runner:UNRELATED",
        targetImageId = "sha256:" + new string('e', 64),
        targetRevision = "UNRELATED-revision",
        currentWorkers = 1,
        staleWorkers = 0,
        lastError = (string?)null,
      },
    };
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "observed-state.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }

  /// <summary>
  /// Overwrites observed-state.json with the observed root
  /// <c>managerContractVersion</c> property entirely missing so tests can
  /// prove the resolver fails closed rather than accepting an observation
  /// whose emitting manager version cannot be verified.
  /// </summary>
  public static async Task
      WriteObservedWithMissingManagerContractVersionAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId)
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    // Deliberately omit "managerContractVersion" so tests can prove the
    // resolver treats the observation as stale rather than accepting an
    // unverified acknowledgement.
    var payload = new
    {
      schemaVersion = 1,
      profileId,
      observedAt = Now,
      scope = "repo",
      generation = 7,
      desiredStateHash = DesiredStateHash,
      desiredStateStatus = "accepted",
      host = new
      {
        hardware = new
        {
          architecture = "amd64",
        },
      },
      update = new
      {
        status = "current",
        targetImage = "ghcr.io/example/runner:main",
        targetImageId = CurrentLocalImageId,
        targetRevision = CurrentWorkerRevision,
        currentWorkers = DefaultRepositoryWorkers,
        staleWorkers = 0,
        lastError = (string?)null,
      },
    };
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "observed-state.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }

  /// <summary>
  /// Overwrites observed-state.json with an observed
  /// <c>managerContractVersion</c> that does not equal the applied static
  /// configuration's version so tests can prove the resolver fails closed
  /// rather than accepting a divergent acknowledgement.
  /// </summary>
  public static async Task
      WriteObservedWithMismatchedManagerContractVersionAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId,
      int observedManagerContractVersion = 18)
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    var payload = new
    {
      schemaVersion = 1,
      managerContractVersion = observedManagerContractVersion,
      profileId,
      observedAt = Now,
      scope = "repo",
      generation = 7,
      desiredStateHash = DesiredStateHash,
      desiredStateStatus = "accepted",
      host = new
      {
        hardware = new
        {
          architecture = "amd64",
        },
      },
      update = new
      {
        status = "current",
        targetImage = "ghcr.io/example/runner:main",
        targetImageId = CurrentLocalImageId,
        targetRevision = CurrentWorkerRevision,
        currentWorkers = DefaultRepositoryWorkers,
        staleWorkers = 0,
        lastError = (string?)null,
      },
    };
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "observed-state.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }
}
