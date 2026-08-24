using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Reads and writes the strict run-local topology control contract.
/// </summary>
public static class CanaryTopologyControlFile
{
  private const int MaximumBytes = 4_096;
  private const string RestartRelayOperation = "restart-relay";
  private static readonly JsonSerializerOptions _jsonOptions = new(
      JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
  };

  /// <summary>
  /// Current topology control schema version.
  /// </summary>
  public const int SchemaVersion = 1;

  /// <summary>
  /// Gets the fixed request filename below the run root.
  /// </summary>
  public const string RequestFileName = "topology-control.request.json";

  /// <summary>
  /// Gets the fixed result filename below the run root.
  /// </summary>
  public const string ResultFileName = "topology-control.result.json";

  /// <summary>
  /// Creates one validated relay-restart request.
  /// </summary>
  /// <param name="runId">Canary run identifier.</param>
  /// <param name="requestId">Unique request identifier.</param>
  /// <returns>The closed request contract.</returns>
  public static CanaryTopologyControlRequest CreateRestartRelayRequest(
      string runId,
      Guid requestId) =>
      new(
          SchemaVersion,
          runId,
          requestId,
          RestartRelayOperation);

  /// <summary>
  /// Reads and validates one topology control request.
  /// </summary>
  /// <param name="path">Request path.</param>
  /// <returns>The validated request.</returns>
  public static CanaryTopologyControlRequest ReadRequest(string path)
  {
    var request = Read<CanaryTopologyControlRequest>(path);
    ValidateRequest(request);
    return request;
  }

  /// <summary>
  /// Writes one topology control request atomically.
  /// </summary>
  /// <param name="path">Destination path.</param>
  /// <param name="request">Validated request.</param>
  public static void WriteRequest(
      string path,
      CanaryTopologyControlRequest request)
  {
    ValidateRequest(request);
    Write(path, request);
  }

  /// <summary>
  /// Reads and validates one topology control result.
  /// </summary>
  /// <param name="path">Result path.</param>
  /// <returns>The validated result.</returns>
  public static CanaryTopologyControlResult ReadResult(string path)
  {
    var result = Read<CanaryTopologyControlResult>(path);
    ValidateResult(result);
    return result;
  }

  /// <summary>
  /// Writes one topology control result atomically.
  /// </summary>
  /// <param name="path">Destination path.</param>
  /// <param name="result">Validated result.</param>
  public static void WriteResult(
      string path,
      CanaryTopologyControlResult result)
  {
    ValidateResult(result);
    Write(path, result);
  }

  private static T Read<T>(string path)
  {
    var file = new FileInfo(path);
    if (!file.Exists ||
        file.Length is <= 0 or > MaximumBytes ||
        (file.Attributes & FileAttributes.ReparsePoint) != 0)
    {
      throw new InvalidDataException(
          "The topology control file is unavailable or exceeds its bound.");
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
      return JsonSerializer.Deserialize<T>(stream, _jsonOptions) ??
          throw new InvalidDataException(
              "The topology control file was empty.");
    }
    catch (JsonException exception)
    {
      throw new InvalidDataException(
          "The topology control file is malformed.",
          exception);
    }
  }

  private static void Write<T>(string path, T value)
  {
    var payload = JsonSerializer.SerializeToUtf8Bytes(
        value,
        _jsonOptions);
    if (payload.Length + 1 > MaximumBytes)
    {
      throw new InvalidDataException(
          "The topology control file exceeds its serialized bound.");
    }
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath) ??
        throw new InvalidDataException(
            "The topology control path has no parent directory.");
    Directory.CreateDirectory(directory);
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
      File.Move(temporaryPath, fullPath, overwrite: false);
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }

  private static void ValidateRequest(
      CanaryTopologyControlRequest request)
  {
    if (request is null ||
        request.SchemaVersion != SchemaVersion ||
        !IsRunId(request.RunId) ||
        request.RequestId == Guid.Empty ||
        request.Operation != RestartRelayOperation)
    {
      throw new InvalidDataException(
          "The topology control request is invalid.");
    }
  }

  private static void ValidateResult(
      CanaryTopologyControlResult result)
  {
    if (result is null ||
        result.SchemaVersion != SchemaVersion ||
        !IsRunId(result.RunId) ||
        result.RequestId == Guid.Empty ||
        result.Status is not ("succeeded" or "failed") ||
        result.Disposition is not (
            "restart-command-succeeded" or
            "restart-command-rejected") ||
        (result.Status == "succeeded") !=
            (result.Disposition == "restart-command-succeeded"))
    {
      throw new InvalidDataException(
          "The topology control result is invalid.");
    }
  }

  private static bool IsRunId(string? value) =>
      value is { Length: 32 } &&
      value.All(character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
