using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Resolves the locally configured PitCrew checkout and profile state directory
/// shared by every typed local operation.
/// </summary>
[DoNotAutoRegister]
internal sealed partial class LocalProfileStateLocator(
    IOptions<ConnectorOptions> _options,
    ILogger<LocalProfileStateLocator> _logger)
{
  /// <summary>
  /// Resolves one profile's PitCrew checkout and state directory.
  /// </summary>
  /// <param name="profileId">Locally resolved profile identifier.</param>
  /// <returns>The resolved location, or the local reason it is unavailable.</returns>
  public LocalProfileStateResolution Locate(string profileId)
  {
    var pitCrewRoot = Path.GetFullPath(_options.Value.PitCrewRoot);
    var setupPath = Path.Combine(pitCrewRoot, "Setup-Runner.ps1");
    if (!File.Exists(setupPath))
    {
      return new LocalProfileStateResolution(
          null,
          "The configured PitCrew root does not contain Setup-Runner.ps1.");
    }
    if (!string.Equals(
            profileId,
            "default",
            StringComparison.OrdinalIgnoreCase) &&
        !File.Exists(Path.Combine(
            pitCrewRoot,
            "profiles",
            profileId,
            "profile.json")))
    {
      return new LocalProfileStateResolution(
          null,
          "Only the default and built-in PitCrew profiles are supported.");
    }

    var stateRoot = Path.GetFullPath(_options.Value.StateRoot);
    var profileDirectory = Path.GetFullPath(Path.Combine(
        stateRoot,
        profileId));
    if (!IsChildPath(stateRoot, profileDirectory) ||
        !Directory.Exists(profileDirectory))
    {
      return new LocalProfileStateResolution(
          null,
          "Profile state directory is unavailable.");
    }
    try
    {
      if ((File.GetAttributes(profileDirectory) &
          FileAttributes.ReparsePoint) != 0)
      {
        return new LocalProfileStateResolution(
            null,
            "Linked profile state directories are not supported.");
      }
    }
    catch (IOException exception)
    {
      LogProfileStateFailure(profileId, exception.Message);
      return new LocalProfileStateResolution(
          null,
          "Profile state could not be read.");
    }
    catch (UnauthorizedAccessException exception)
    {
      LogProfileStateFailure(profileId, exception.Message);
      return new LocalProfileStateResolution(
          null,
          "Profile state could not be read.");
    }

    return new LocalProfileStateResolution(
        new LocalProfileStateLocation(pitCrewRoot, profileDirectory),
        null);
  }

  /// <summary>
  /// Reads one bounded local state document.
  /// </summary>
  /// <param name="path">State document path.</param>
  /// <param name="maximumBytes">Largest accepted document size.</param>
  /// <param name="cancellationToken">Token that cancels the read.</param>
  /// <returns>The document bytes.</returns>
  public static async Task<byte[]> ReadBoundedAsync(
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
          $"State file '{Path.GetFileName(path)}' is outside the supported size range.");
    }
    var bytes = new byte[(int)stream.Length];
    await stream.ReadExactlyAsync(bytes, cancellationToken);
    return bytes;
  }

  private static bool IsChildPath(
      string parent,
      string candidate)
  {
    var relative = Path.GetRelativePath(parent, candidate);
    return relative.Length > 0 &&
        !relative.StartsWith("..", StringComparison.Ordinal) &&
        !Path.IsPathRooted(relative);
  }

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Profile {ProfileId} state could not be located: {Reason}")]
  private partial void LogProfileStateFailure(
      string profileId,
      string reason);
}
