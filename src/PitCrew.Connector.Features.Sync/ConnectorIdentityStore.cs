using System.Text.Json;

using Microsoft.Extensions.Options;

namespace PitCrew.Connector.Features.Sync;

internal sealed class ConnectorIdentityStore(
    IOptions<ConnectorOptions> _options)
{
  public async Task<ConnectorIdentity> LoadOrCreatePendingAsync(
      CancellationToken cancellationToken)
  {
    var path = Path.GetFullPath(_options.Value.IdentityPath);
    if (File.Exists(path))
    {
      await using var stream = File.OpenRead(path);
      var identity = await JsonSerializer.DeserializeAsync(
          stream,
          ConnectorIdentityJsonContext.Default.ConnectorIdentity,
          cancellationToken);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
      }
      return identity ?? throw new InvalidOperationException(
          $"Connector identity at '{path}' is empty.");
    }

    var pending = new ConnectorIdentity(
        Guid.NewGuid().ToString("D"),
        null,
        null);
    await SaveAsync(pending, cancellationToken);
    return pending;
  }

  public Task SaveAsync(
      ConnectorIdentity identity,
      CancellationToken cancellationToken) =>
      WriteAtomicallyAsync(
          Path.GetFullPath(_options.Value.IdentityPath),
          identity,
          cancellationToken);

  private static async Task WriteAtomicallyAsync(
      string path,
      ConnectorIdentity identity,
      CancellationToken cancellationToken)
  {
    var directory = Path.GetDirectoryName(path);
    if (string.IsNullOrWhiteSpace(directory))
    {
      throw new InvalidOperationException(
          $"Connector identity path '{path}' does not have a parent directory.");
    }

    Directory.CreateDirectory(directory);
    var temporaryPath = Path.Combine(
        directory,
        $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    try
    {
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
            identity,
            ConnectorIdentityJsonContext.Default.ConnectorIdentity,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
      }

      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(
            temporaryPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
      }
      File.Move(temporaryPath, path, true);
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
