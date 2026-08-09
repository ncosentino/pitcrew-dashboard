using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

internal sealed record ObservedStateReadResult(
    bool IsComplete,
    string AggregateHash,
    IReadOnlyList<ManagerObservedState> Profiles,
    ConnectorHealthFailure? Failure = null);

internal sealed partial class ObservedStateReader(
    IOptions<ConnectorOptions> _options,
    ILogger<ObservedStateReader> _logger)
{
  private readonly Dictionary<string, CachedObservedState> _lastGood =
      new(StringComparer.OrdinalIgnoreCase);

  public async Task<ObservedStateReadResult> ReadAsync(
      CancellationToken cancellationToken)
  {
    var stateRoot = Path.GetFullPath(_options.Value.StateRoot);
    if (!Directory.Exists(stateRoot))
    {
      LogMissingStateRoot(stateRoot);
      return new ObservedStateReadResult(
          false,
          string.Empty,
          [],
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.StateRootMissing,
              "PitCrew state root is unavailable."));
    }

    string[] profileDirectories;
    try
    {
      profileDirectories = Directory
          .GetDirectories(stateRoot)
          .Order(StringComparer.OrdinalIgnoreCase)
          .ToArray();
    }
    catch (IOException exception)
    {
      LogUnreadableStateRoot(stateRoot, exception.Message);
      return new ObservedStateReadResult(
          false,
          string.Empty,
          [],
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.StateRootUnreadable,
              "PitCrew state root could not be enumerated."));
    }
    catch (UnauthorizedAccessException exception)
    {
      LogUnreadableStateRoot(stateRoot, exception.Message);
      return new ObservedStateReadResult(
          false,
          string.Empty,
          [],
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.StateRootUnreadable,
              "PitCrew state root could not be enumerated."));
    }

    var activeDirectories = profileDirectories.ToHashSet(
        StringComparer.OrdinalIgnoreCase);
    foreach (var cachedPath in _lastGood.Keys
        .Where(path => !activeDirectories.Contains(path))
        .ToArray())
    {
      _lastGood.Remove(cachedPath);
    }

    var snapshots = new List<CachedObservedState>();
    var complete = true;
    ConnectorHealthFailure? failure = null;
    foreach (var profileDirectory in profileDirectories)
    {
      try
      {
        if ((File.GetAttributes(profileDirectory) &
            FileAttributes.ReparsePoint) != 0)
        {
          LogSkippedLinkedProfileDirectory(profileDirectory);
          continue;
        }
      }
      catch (IOException exception)
      {
        LogUnreadableStateRoot(
            profileDirectory,
            exception.Message);
        complete = false;
        failure ??= CreateProfileFailure(
            profileDirectory,
            ConnectorHealthFailureCategories.ProfileDirectoryUnreadable,
            "Profile state directory could not be inspected.");
        continue;
      }
      catch (UnauthorizedAccessException exception)
      {
        LogUnreadableStateRoot(
            profileDirectory,
            exception.Message);
        complete = false;
        failure ??= CreateProfileFailure(
            profileDirectory,
            ConnectorHealthFailureCategories.ProfileDirectoryUnreadable,
            "Profile state directory could not be inspected.");
        continue;
      }

      var observedStatePath = Path.Combine(
          profileDirectory,
          "observed-state.json");
      try
      {
        File.GetAttributes(observedStatePath);
      }
      catch (FileNotFoundException)
      {
        AddCachedProfileIfPresent(
            profileDirectory,
            snapshots);
        continue;
      }
      catch (DirectoryNotFoundException)
      {
        AddCachedProfileIfPresent(
            profileDirectory,
            snapshots);
        continue;
      }
      catch (IOException exception)
      {
        LogUnreadableObservedState(
            observedStatePath,
            exception.Message);
        if (!AddCachedProfileOrMarkIncomplete(
            profileDirectory,
            snapshots,
            ref complete))
        {
          failure ??= CreateProfileFailure(
              profileDirectory,
              ConnectorHealthFailureCategories.ProfileStateUnreadable,
              "Profile observed state could not be read.");
        }
        continue;
      }
      catch (UnauthorizedAccessException exception)
      {
        LogUnreadableObservedState(
            observedStatePath,
            exception.Message);
        if (!AddCachedProfileOrMarkIncomplete(
            profileDirectory,
            snapshots,
            ref complete))
        {
          failure ??= CreateProfileFailure(
              profileDirectory,
              ConnectorHealthFailureCategories.ProfileStateUnreadable,
              "Profile observed state could not be read.");
        }
        continue;
      }

      try
      {
        var bytes = await ReadBoundedAsync(
            observedStatePath,
            _options.Value.MaximumObservedStateBytes,
            cancellationToken);
        if (!HasRequiredContractProperties(bytes))
        {
          LogInvalidObservedState(observedStatePath);
          if (!AddCachedProfileOrMarkIncomplete(
              profileDirectory,
              snapshots,
              ref complete))
          {
            failure ??= CreateProfileFailure(
                profileDirectory,
                ConnectorHealthFailureCategories.ProfileStateInvalid,
                "Profile observed state is invalid.");
          }
          continue;
        }
        var profile = JsonSerializer.Deserialize(
            bytes,
            PitCrewProtocolJsonContext.Default.ManagerObservedState);
        if (profile is null ||
            profile.SchemaVersion != 1 ||
            !string.Equals(
                profile.ProfileId,
                Path.GetFileName(profileDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
          LogInvalidObservedState(observedStatePath);
          if (!AddCachedProfileOrMarkIncomplete(
              profileDirectory,
              snapshots,
              ref complete))
          {
            failure ??= CreateProfileFailure(
                profileDirectory,
                ConnectorHealthFailureCategories.ProfileStateInvalid,
                "Profile observed state is invalid.");
          }
          continue;
        }

        var snapshot = new CachedObservedState(
            profile,
            SHA256.HashData(bytes));
        _lastGood[profileDirectory] = snapshot;
        snapshots.Add(snapshot);
      }
      catch (JsonException exception)
      {
        LogUnreadableObservedState(
            observedStatePath,
            exception.Message);
        if (!AddCachedProfileOrMarkIncomplete(
            profileDirectory,
            snapshots,
            ref complete))
        {
          failure ??= CreateProfileFailure(
              profileDirectory,
              ConnectorHealthFailureCategories.ProfileStateInvalid,
              "Profile observed state is invalid.");
        }
      }
      catch (InvalidDataException exception)
      {
        LogUnreadableObservedState(
            observedStatePath,
            exception.Message);
        if (!AddCachedProfileOrMarkIncomplete(
            profileDirectory,
            snapshots,
            ref complete))
        {
          failure ??= CreateProfileFailure(
              profileDirectory,
              ConnectorHealthFailureCategories.ProfileStateInvalid,
              "Profile observed state is invalid.");
        }
      }
      catch (IOException exception)
      {
        LogUnreadableObservedState(
            observedStatePath,
            exception.Message);
        if (!AddCachedProfileOrMarkIncomplete(
            profileDirectory,
            snapshots,
            ref complete))
        {
          failure ??= CreateProfileFailure(
              profileDirectory,
              ConnectorHealthFailureCategories.ProfileStateUnreadable,
              "Profile observed state could not be read.");
        }
      }
      catch (UnauthorizedAccessException exception)
      {
        LogUnreadableObservedState(
            observedStatePath,
            exception.Message);
        if (!AddCachedProfileOrMarkIncomplete(
            profileDirectory,
            snapshots,
            ref complete))
        {
          failure ??= CreateProfileFailure(
              profileDirectory,
              ConnectorHealthFailureCategories.ProfileStateUnreadable,
              "Profile observed state could not be read.");
        }
      }
    }

    var sortedSnapshots = snapshots
        .OrderBy(
            snapshot => snapshot.Profile.ProfileId,
            StringComparer.OrdinalIgnoreCase)
        .ToArray();
    using var aggregateHash = IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256);
    Span<byte> separator = stackalloc byte[1];
    foreach (var snapshot in sortedSnapshots)
    {
      aggregateHash.AppendData(
          Encoding.UTF8.GetBytes(snapshot.Profile.ProfileId));
      aggregateHash.AppendData(separator);
      aggregateHash.AppendData(snapshot.ContentHash);
    }

    return new ObservedStateReadResult(
        complete,
        Convert.ToHexString(aggregateHash.GetHashAndReset()),
        sortedSnapshots.Select(snapshot => snapshot.Profile).ToArray(),
        failure);
  }

  private static async Task<byte[]> ReadBoundedAsync(
      string path,
      int maximumBytes,
      CancellationToken cancellationToken)
  {
    await using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    if (stream.Length <= 0 || stream.Length > maximumBytes)
    {
      throw new InvalidDataException(
          $"Observed state is {stream.Length} bytes; expected between 1 and {maximumBytes}.");
    }

    var bytes = new byte[(int)stream.Length];
    await stream.ReadExactlyAsync(bytes, cancellationToken);
    return bytes;
  }

  private static bool HasRequiredContractProperties(
      ReadOnlyMemory<byte> bytes)
  {
    using var document = JsonDocument.Parse(bytes);
    var root = document.RootElement;
    if (!root.TryGetProperty(
            "managerContractVersion",
            out var contractElement) ||
        !contractElement.TryGetInt32(out var contractVersion))
    {
      return false;
    }
    if (contractVersion >= 15)
    {
      if (!root.TryGetProperty("slots", out var slots) ||
          slots.ValueKind != JsonValueKind.Array)
      {
        return false;
      }
      using var slotEnumerator = slots.EnumerateArray();
      while (slotEnumerator.MoveNext())
      {
        var slot = slotEnumerator.Current;
        if (slot.ValueKind != JsonValueKind.Object ||
            !slot.TryGetProperty("currentJob", out _))
        {
          return false;
        }
      }
    }
    if (contractVersion >= 16 &&
        (!root.TryGetProperty(
            "resourceTelemetry",
            out var telemetry) ||
         telemetry.ValueKind != JsonValueKind.Object ||
         !telemetry.TryGetProperty("hostPressure", out _)))
    {
      return false;
    }
    if (contractVersion >= 18 &&
        (!root.TryGetProperty(
            "hostAdmission",
            out var hostAdmission) ||
         hostAdmission.ValueKind != JsonValueKind.Object))
    {
      return false;
    }
    return true;
  }

  private bool AddCachedProfileOrMarkIncomplete(
      string profileDirectory,
      ICollection<CachedObservedState> snapshots,
      ref bool complete)
  {
    if (_lastGood.TryGetValue(
        profileDirectory,
        out var cached))
    {
      snapshots.Add(cached);
      return true;
    }

    complete = false;
    return false;
  }

  private void AddCachedProfileIfPresent(
      string profileDirectory,
      ICollection<CachedObservedState> snapshots)
  {
    if (_lastGood.TryGetValue(
        profileDirectory,
        out var cached))
    {
      snapshots.Add(cached);
    }
  }

  private static ConnectorHealthFailure CreateProfileFailure(
      string profileDirectory,
      string category,
      string detail) =>
      new(
          category,
          detail,
          Path.GetFileName(profileDirectory));

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Pitcrew state root {StateRoot} does not exist.")]
  private partial void LogMissingStateRoot(string stateRoot);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Pitcrew state root {StateRoot} could not be enumerated: {Reason}")]
  private partial void LogUnreadableStateRoot(
      string stateRoot,
      string reason);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Skipped linked profile directory {ProfileDirectory}.")]
  private partial void LogSkippedLinkedProfileDirectory(
      string profileDirectory);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Observed state at {ObservedStatePath} does not satisfy the expected profile contract.")]
  private partial void LogInvalidObservedState(string observedStatePath);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Observed state at {ObservedStatePath} could not be read: {Reason}")]
  private partial void LogUnreadableObservedState(
      string observedStatePath,
      string reason);

  private sealed record CachedObservedState(
      ManagerObservedState Profile,
      byte[] ContentHash);
}
