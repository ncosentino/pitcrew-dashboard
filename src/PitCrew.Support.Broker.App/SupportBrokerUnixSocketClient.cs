using System.Net.Sockets;
using System.Runtime.Versioning;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("linux")]
internal sealed class SupportBrokerUnixSocketClient(string _socketPath)
{
  public async Task<SupportBrokerExecution> ExecuteAsync(
      SupportBrokerRequest request,
      CancellationToken cancellationToken)
  {
    using var socket = new Socket(
        AddressFamily.Unix,
        SocketType.Stream,
        ProtocolType.Unspecified);
    await socket.ConnectAsync(
        new UnixDomainSocketEndPoint(_socketPath),
        cancellationToken);
    await using var stream = new NetworkStream(socket, ownsSocket: false);
    await SupportBrokerPipeCodec.WriteAsync(
        stream,
        request,
        cancellationToken);
    var result = await SupportBrokerPipeCodec.ReadAsync<SupportBrokerExecution>(
        stream,
        cancellationToken);
    return result ?? new SupportBrokerExecution(
        SupportBrokerStatus.ExecutionFailed,
        null,
        "Broker returned an empty response.");
  }
}
