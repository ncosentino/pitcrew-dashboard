using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Reads and writes the strict run-local rejected-request control contract.
/// </summary>
public static class CanaryRejectedRequestControlFile
{
  private const int MaximumBytes = 8_192;
  private const string EnqueueOperation =
      "enqueue-rejected-request";
  private const string CancelOperation =
      "cancel-rejected-request";
  private static readonly JsonSerializerOptions _jsonOptions = new(
      JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
  };

  /// <summary>
  /// Current rejected-request control schema version.
  /// </summary>
  public const int SchemaVersion = 1;

  /// <summary>
  /// Gets the fixed request filename below the run root.
  /// </summary>
  public const string RequestFileName =
      "rejected-request-control.request.json";

  /// <summary>
  /// Gets the fixed result filename below the run root.
  /// </summary>
  public const string ResultFileName =
      "rejected-request-control.result.json";

  /// <summary>
  /// Creates one validated enqueue request.
  /// </summary>
  /// <param name="runId">Canary run identifier.</param>
  /// <param name="requestId">Unique control request identifier.</param>
  /// <param name="injectionCase">Closed invalid request shape.</param>
  /// <param name="sessionId">Relay session identifier.</param>
  /// <param name="nodeId">Enrolled node route.</param>
  /// <param name="nodeEncryptionPublicKeySpki">
  /// Enrolled node RSA public key.
  /// </param>
  /// <param name="replayId">Replay group for replay cases.</param>
  /// <returns>The validated enqueue request.</returns>
  public static CanaryRejectedRequestControlRequest
      CreateEnqueueRequest(
          string runId,
          Guid requestId,
          string injectionCase,
          Guid sessionId,
          Guid nodeId,
          string nodeEncryptionPublicKeySpki,
          Guid? replayId = null)
  {
    var request = new CanaryRejectedRequestControlRequest(
        SchemaVersion,
        runId,
        requestId,
        EnqueueOperation,
        injectionCase,
        sessionId,
        nodeId,
        nodeEncryptionPublicKeySpki,
        replayId);
    ValidateRequest(request);
    return request;
  }

  /// <summary>
  /// Creates one validated cancellation request.
  /// </summary>
  /// <param name="runId">Canary run identifier.</param>
  /// <param name="requestId">Unique control request identifier.</param>
  /// <param name="sessionId">Injected relay session to cancel.</param>
  /// <returns>The validated cancellation request.</returns>
  public static CanaryRejectedRequestControlRequest
      CreateCancellationRequest(
          string runId,
          Guid requestId,
          Guid sessionId)
  {
    var request = new CanaryRejectedRequestControlRequest(
        SchemaVersion,
        runId,
        requestId,
        CancelOperation,
        null,
        sessionId,
        null,
        null,
        null);
    ValidateRequest(request);
    return request;
  }

  /// <summary>
  /// Reads and validates one control request.
  /// </summary>
  /// <param name="path">Request path.</param>
  /// <returns>The validated request.</returns>
  public static CanaryRejectedRequestControlRequest ReadRequest(
      string path)
  {
    var request = Read<CanaryRejectedRequestControlRequest>(path);
    ValidateRequest(request);
    return request;
  }

  /// <summary>
  /// Writes one control request atomically.
  /// </summary>
  /// <param name="path">Destination path.</param>
  /// <param name="request">Validated request.</param>
  public static void WriteRequest(
      string path,
      CanaryRejectedRequestControlRequest request)
  {
    ValidateRequest(request);
    Write(path, request);
  }

  /// <summary>
  /// Reads and validates one control result.
  /// </summary>
  /// <param name="path">Result path.</param>
  /// <returns>The validated result.</returns>
  public static CanaryRejectedRequestControlResult ReadResult(
      string path)
  {
    var result = Read<CanaryRejectedRequestControlResult>(path);
    ValidateResult(result);
    return result;
  }

  /// <summary>
  /// Writes one control result atomically.
  /// </summary>
  /// <param name="path">Destination path.</param>
  /// <param name="result">Validated result.</param>
  public static void WriteResult(
      string path,
      CanaryRejectedRequestControlResult result)
  {
    ValidateResult(result);
    Write(path, result);
  }

  /// <summary>
  /// Returns whether a request asks to enqueue a rejected request.
  /// </summary>
  /// <param name="request">Validated request.</param>
  /// <returns><see langword="true"/> for enqueue.</returns>
  public static bool IsEnqueue(
      CanaryRejectedRequestControlRequest request) =>
      request.Operation == EnqueueOperation;

  private static T Read<T>(string path)
  {
    var file = new FileInfo(path);
    if (!file.Exists ||
        file.Length is <= 0 or > MaximumBytes ||
        (file.Attributes & FileAttributes.ReparsePoint) != 0)
    {
      throw new InvalidDataException(
          "The rejected-request control file is unavailable or exceeds its bound.");
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
              "The rejected-request control file was empty.");
    }
    catch (JsonException exception)
    {
      throw new InvalidDataException(
          "The rejected-request control file was malformed.",
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
          "The rejected-request control file exceeds its serialized bound.");
    }
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath) ??
        throw new InvalidDataException(
            "The rejected-request control path has no parent directory.");
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
      CanaryRejectedRequestControlRequest request)
  {
    if (request is null ||
        request.SchemaVersion != SchemaVersion ||
        !IsRunId(request.RunId) ||
        request.RequestId == Guid.Empty ||
        request.SessionId == Guid.Empty)
    {
      throw new InvalidDataException(
          "The rejected-request control request is invalid.");
    }
    if (request.Operation == EnqueueOperation)
    {
      var replayCase = request.InjectionCase is
          CanaryRejectedRequestCases.ReplaySeed or
          CanaryRejectedRequestCases.RequestReplay;
      if (request.InjectionCase is null ||
          !CanaryRejectedRequestCases.IsSupported(
              request.InjectionCase) ||
          request.NodeId is null ||
          request.NodeId == Guid.Empty ||
          !IsPublicKey(request.NodeEncryptionPublicKeySpki) ||
          replayCase != (request.ReplayId is not null) ||
          request.ReplayId == Guid.Empty)
      {
        throw new InvalidDataException(
            "The rejected-request enqueue request is invalid.");
      }
      return;
    }
    if (request.Operation != CancelOperation ||
        request.InjectionCase is not null ||
        request.NodeId is not null ||
        request.NodeEncryptionPublicKeySpki is not null ||
        request.ReplayId is not null)
    {
      throw new InvalidDataException(
          "The rejected-request cancellation request is invalid.");
    }
  }

  private static void ValidateResult(
      CanaryRejectedRequestControlResult result)
  {
    if (result is null ||
        result.SchemaVersion != SchemaVersion ||
        !IsRunId(result.RunId) ||
        result.RequestId == Guid.Empty ||
        result.Status is not ("succeeded" or "failed") ||
        result.Disposition is not (
            "request-enqueued" or
            "request-cancelled" or
            "request-control-rejected") ||
        (result.Status == "succeeded") !=
            (result.Disposition is
                "request-enqueued" or
                "request-cancelled"))
    {
      throw new InvalidDataException(
          "The rejected-request control result is invalid.");
    }
  }

  private static bool IsPublicKey(string? value) =>
      value is { Length: >= 256 and <= 2_048 } &&
      value.All(character =>
          character is >= 'A' and <= 'Z' or
          >= 'a' and <= 'z' or
          >= '0' and <= '9' or
          '-' or
          '_');

  private static bool IsRunId(string? value) =>
      value is { Length: 32 } &&
      value.All(character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
