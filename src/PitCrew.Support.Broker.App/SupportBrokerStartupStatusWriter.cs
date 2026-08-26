using System.Text;
using System.Text.Json;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportBrokerStartupStatusWriter(
    string _root,
    TimeProvider _timeProvider)
{
  private const int SchemaVersion = 1;
  private const string FileName =
      "broker-startup-status.json";
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  public void Clear()
  {
    var path = Path.Combine(_root, FileName);
    if (File.Exists(path))
    {
      File.Delete(path);
    }
  }

  public void Write(string disposition)
  {
    var path = Path.Combine(_root, FileName);
    var temporaryPath =
        $"{path}.{Guid.NewGuid():N}.tmp";
    try
    {
      var status = new SupportBrokerStartupStatus(
          SchemaVersion,
          disposition,
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
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite);
      }
      File.Move(
          temporaryPath,
          path,
          overwrite: true);
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }
}
