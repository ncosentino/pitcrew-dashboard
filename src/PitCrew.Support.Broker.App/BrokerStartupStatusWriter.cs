using System.Text.Json;

namespace PitCrew.Support.Broker.App;

internal static class BrokerStartupStatusWriter
{
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  public static string GetPath(string contentRoot) =>
      Path.Combine(contentRoot, "broker-startup-status.json");

  public static async Task WriteFailureAsync(
      string path,
      string exceptionType,
      CancellationToken cancellationToken)
  {
    var temporaryPath = $"{path}.{Guid.NewGuid():N}.new";
    await using (var stream = new FileStream(
        temporaryPath,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        4096,
        FileOptions.Asynchronous | FileOptions.WriteThrough))
    {
      await JsonSerializer.SerializeAsync(
          stream,
          new BrokerStartupStatus(1, exceptionType),
          _jsonOptions,
          cancellationToken);
      await stream.FlushAsync(cancellationToken);
    }
    File.Move(temporaryPath, path, overwrite: true);
  }

  private sealed record BrokerStartupStatus(
      int SchemaVersion,
      string ExceptionType);
}
