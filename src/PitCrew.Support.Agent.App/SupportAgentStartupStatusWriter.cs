using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PitCrew.Support.Agent.App;

internal sealed partial class SupportAgentStartupStatusWriter(
    IHostEnvironment _environment,
    TimeProvider _timeProvider,
    ILogger<SupportAgentStartupStatusWriter> _logger)
{
  private const int SchemaVersion = 1;
  private const string FileName = "agent-startup-status.json";
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  public void Write(
      string phase,
      string disposition,
      Type? exceptionType)
  {
    var path = GetPath();
    var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
    try
    {
      var status = new SupportAgentStartupStatus(
          SchemaVersion,
          phase,
          disposition,
          exceptionType?.Name,
          _timeProvider.GetUtcNow());
      File.WriteAllText(
          temporaryPath,
          JsonSerializer.Serialize(
              status,
              _jsonOptions) + "\n",
          new UTF8Encoding(
              encoderShouldEmitUTF8Identifier: false,
              throwOnInvalidBytes: true));
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(
            temporaryPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
      }
      File.Move(temporaryPath, path, overwrite: true);
    }
    catch (Exception exception)
        when (exception is
            IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            ArgumentException)
    {
      LogStatusWriteFailure(
          _logger,
          exception.GetType().Name);
    }
    finally
    {
      DeleteIfPresent(temporaryPath);
    }
  }

  public void Clear() => DeleteIfPresent(GetPath());

  private string GetPath() =>
      Path.Combine(
          _environment.ContentRootPath,
          FileName);

  private void DeleteIfPresent(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch (Exception exception)
        when (exception is
            IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            ArgumentException)
    {
      LogStatusDeleteFailure(
          _logger,
          exception.GetType().Name);
    }
  }

  [LoggerMessage(
      EventId = 20,
      Level = LogLevel.Warning,
      Message = "The bounded support-agent startup status could not be written: {ExceptionType}")]
  private static partial void LogStatusWriteFailure(
      ILogger logger,
      string exceptionType);

  [LoggerMessage(
      EventId = 21,
      Level = LogLevel.Warning,
      Message = "The bounded support-agent startup status could not be removed: {ExceptionType}")]
  private static partial void LogStatusDeleteFailure(
      ILogger logger,
      string exceptionType);
}
