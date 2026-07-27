using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Serializes every locally executed profile operation, including PitCrew's own
/// setup, refresh, teardown, capacity, and recovery invocations.
/// </summary>
[DoNotAutoRegister]
internal sealed partial class LocalProfileOperationGate(
    IOptions<ConnectorOptions> _options,
    ILogger<LocalProfileOperationGate> _logger)
{
  private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
  private readonly Lock _sync = new();

  /// <summary>
  /// Acquires the local operation slot for one profile.
  /// </summary>
  /// <param name="profileId">Locally resolved profile identifier.</param>
  /// <returns>The lease, or <see langword="null"/> when an operation is already active.</returns>
  public LocalProfileOperationLease? AcquireOrNull(string profileId)
  {
    lock (_sync)
    {
      if (!_held.Add(profileId))
      {
        return null;
      }
    }
    if (IsExternalOperationActive(profileId))
    {
      Release(profileId);
      return null;
    }
    return new LocalProfileOperationLease(this, profileId);
  }

  /// <summary>
  /// Gets whether any local operation currently owns the profile.
  /// </summary>
  /// <param name="profileId">Locally resolved profile identifier.</param>
  /// <returns><see langword="true"/> when an operation is active.</returns>
  public bool IsActive(string profileId)
  {
    lock (_sync)
    {
      if (_held.Contains(profileId))
      {
        return true;
      }
    }
    return IsExternalOperationActive(profileId);
  }

  internal void Release(string profileId)
  {
    lock (_sync)
    {
      _held.Remove(profileId);
    }
  }

  private bool IsExternalOperationActive(string profileId)
  {
    var lockPath = Path.Combine(
        Path.GetFullPath(_options.Value.StateRoot),
        profileId,
        "setup.lock");
    try
    {
      using var stream = new FileStream(
          lockPath,
          FileMode.Open,
          FileAccess.ReadWrite,
          FileShare.None);
      return false;
    }
    catch (FileNotFoundException)
    {
      return false;
    }
    catch (DirectoryNotFoundException)
    {
      return false;
    }
    catch (IOException)
    {
      return true;
    }
    catch (UnauthorizedAccessException exception)
    {
      LogUnreadableLock(lockPath, exception.Message);
      return true;
    }
  }

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "PitCrew profile lock {LockPath} could not be inspected: {Reason}")]
  private partial void LogUnreadableLock(
      string lockPath,
      string reason);
}
