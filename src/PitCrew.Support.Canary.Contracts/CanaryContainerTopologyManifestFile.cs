using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Reads and writes the strict run-local container topology contract.
/// </summary>
public static class CanaryContainerTopologyManifestFile
{
  private const int MaximumManifestBytes = 16_384;
  private const string DashboardImagePrefix =
      "pitcrew-support-canary-dashboard:";
  private const string RelayImagePrefix =
      "pitcrew-support-canary-relay:";
  private const string ResourcePrefix = "pitcrew-canary-";
  private static readonly JsonSerializerOptions _jsonOptions = new(
      JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
  };

  /// <summary>
  /// Current container topology schema version.
  /// </summary>
  public const int SchemaVersion = 1;

  /// <summary>
  /// Reads and validates one run-local container topology manifest.
  /// </summary>
  /// <param name="path">Existing manifest path.</param>
  /// <returns>The validated topology contract.</returns>
  /// <exception cref="InvalidDataException">
  /// The file is missing, malformed, oversized, or violates the closed contract.
  /// </exception>
  public static CanaryContainerTopologyManifest Read(string path)
  {
    var file = new FileInfo(path);
    if (!file.Exists ||
        file.Length is <= 0 or > MaximumManifestBytes ||
        (file.Attributes & FileAttributes.ReparsePoint) != 0)
    {
      throw new InvalidDataException(
          "The container topology manifest is unavailable or exceeds its bound.");
    }
    try
    {
      using var stream = new FileStream(
          file.FullName,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          4096,
          FileOptions.SequentialScan);
      var manifest = JsonSerializer.Deserialize<
          CanaryContainerTopologyManifest>(
              stream,
              _jsonOptions) ??
          throw new InvalidDataException(
              "The container topology manifest was empty.");
      Validate(manifest);
      return manifest;
    }
    catch (JsonException exception)
    {
      throw new InvalidDataException(
          "The container topology manifest is malformed.",
          exception);
    }
  }

  /// <summary>
  /// Atomically writes one validated run-local container topology manifest.
  /// </summary>
  /// <param name="path">Destination path.</param>
  /// <param name="manifest">Container topology contract.</param>
  public static void Write(
      string path,
      CanaryContainerTopologyManifest manifest)
  {
    Validate(manifest);
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath) ??
        throw new InvalidDataException(
            "The container topology manifest path has no parent directory.");
    Directory.CreateDirectory(directory);
    var payload = JsonSerializer.SerializeToUtf8Bytes(
        manifest,
        _jsonOptions);
    if (payload.Length + 1 > MaximumManifestBytes)
    {
      throw new InvalidDataException(
          "The container topology manifest exceeds its serialized bound.");
    }
    var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
    try
    {
      using (var stream = new FileStream(
          temporaryPath,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None,
          4096,
          FileOptions.WriteThrough))
      {
        stream.Write(payload);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
      }
      File.Move(temporaryPath, fullPath, overwrite: true);
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }

  private static void Validate(
      CanaryContainerTopologyManifest manifest)
  {
    if (manifest is null ||
        manifest.SchemaVersion != SchemaVersion ||
        !IsRunId(manifest.RunId) ||
        manifest.Dashboard is null ||
        manifest.Dashboard.Repository !=
            "ncosentino/pitcrew-dashboard" ||
        !IsCommit(manifest.Dashboard.Commit) ||
        !IsImage(
            manifest.DashboardImage,
            $"{DashboardImagePrefix}{manifest.RunId}") ||
        !IsImage(
            manifest.RelayImage,
            $"{RelayImagePrefix}{manifest.RunId}") ||
        manifest.DashboardContainerName !=
            $"{ResourcePrefix}{manifest.RunId}-dashboard" ||
        manifest.RelayContainerName !=
            $"{ResourcePrefix}{manifest.RunId}-relay" ||
        manifest.DashboardVolumeName !=
            $"{ResourcePrefix}{manifest.RunId}-dashboard-data" ||
        manifest.RelayVolumeName !=
            $"{ResourcePrefix}{manifest.RunId}-relay-data" ||
        manifest.CreatedAt == default)
    {
      throw new InvalidDataException(
          "The container topology manifest is invalid.");
    }
  }

  private static bool IsImage(
      CanaryContainerImageIdentity? image,
      string expectedReference) =>
      image is not null &&
      image.Reference == expectedReference &&
      image.ImageId is { Length: 71 } &&
      image.ImageId.StartsWith(
          "sha256:",
          StringComparison.Ordinal) &&
      image.ImageId[7..].All(IsLowercaseHex);

  private static bool IsRunId(string? value) =>
      value is { Length: 32 } &&
      value.All(IsLowercaseHex);

  private static bool IsCommit(string? value) =>
      value is { Length: 40 } &&
      value.All(IsLowercaseHex);

  private static bool IsLowercaseHex(char character) =>
      character is >= '0' and <= '9' or >= 'a' and <= 'f';
}
