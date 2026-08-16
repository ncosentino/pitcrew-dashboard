using System.Net.Sockets;
using System.Runtime.Versioning;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("linux")]
internal sealed class SupportBrokerUnixSocketServer(
    SupportBrokerOptions _options,
    SupportDiagnosticsBroker _broker) : ISupportBrokerServer, IDisposable
{
  private Socket? _listener;

  internal void Initialize()
  {
    if (_listener is not null)
    {
      return;
    }
    var socketDirectory = Path.GetDirectoryName(_options.SocketPath);
    if (string.IsNullOrWhiteSpace(socketDirectory) ||
        !Directory.Exists(socketDirectory))
    {
      throw new InvalidOperationException(
          "The installer-owned support broker socket directory is unavailable.");
    }
    if (UnixProcessIdentity.GetEffectiveUserId() != _options.BrokerUid ||
        UnixProcessIdentity.GetEffectiveGroupId() != _options.IpcGroupGid)
    {
      throw new InvalidOperationException(
          "The support broker process identity does not match the installed policy.");
    }
    if (File.Exists(_options.SocketPath))
    {
      File.Delete(_options.SocketPath);
    }
    var listener = new Socket(
        AddressFamily.Unix,
        SocketType.Stream,
        ProtocolType.Unspecified);
    try
    {
      listener.Bind(new UnixDomainSocketEndPoint(_options.SocketPath));
      File.SetUnixFileMode(
          _options.SocketPath,
          UnixSocketAccessPolicy.RequiredMode);
      listener.Listen(1);
      _listener?.Dispose();
      _listener = listener;
      VerifySocketAccess();
    }
    catch
    {
      listener.Dispose();
      throw;
    }
  }

  public async Task RunOnceAsync(CancellationToken cancellationToken)
  {
    Initialize();
    VerifySocketAccess();
    using var client = await _listener!.AcceptAsync(cancellationToken);
    var credentials = UnixPeerCredentialReader.Read(client);
    if (!UnixPeerCredentialPolicy.IsExpected(
        credentials,
        _options.ExpectedAgentUid!.Value))
    {
      return;
    }
    await using var stream = new NetworkStream(client, ownsSocket: false);
    var request = await SupportBrokerPipeCodec.ReadAsync<SupportBrokerRequest>(
        stream,
        cancellationToken);
    var response = request is null
        ? new SupportBrokerExecution(
            SupportBrokerStatus.ExecutionFailed,
            null,
            "Request body was empty.")
        : await _broker.ExecuteAsync(request, cancellationToken);
    await SupportBrokerPipeCodec.WriteAsync(
        stream,
        response,
        cancellationToken);
  }

  public void Dispose()
  {
    _listener?.Dispose();
    _listener = null;
    if (File.Exists(_options.SocketPath))
    {
      File.Delete(_options.SocketPath);
    }
  }

  private void VerifySocketAccess()
  {
    var metadata = UnixSocketMetadataReader.Read(_options.SocketPath);
    if (!UnixSocketAccessPolicy.IsExpected(
        metadata,
        _options.BrokerUid!.Value,
        _options.IpcGroupGid!.Value))
    {
      throw new InvalidOperationException(
          "The support broker socket ownership or mode does not match the installed policy.");
    }
  }
}
