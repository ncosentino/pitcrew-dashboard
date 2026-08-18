using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("windows")]
internal sealed class SupportBrokerPipeClient(string _pipeName)
{
  public async Task<SupportBrokerExecution> ExecuteAsync(
      SupportBrokerRequest request,
      CancellationToken cancellationToken)
  {
    await using var pipe = new NamedPipeClientStream(
        ".",
        _pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous,
        TokenImpersonationLevel.Identification);
    await pipe.ConnectAsync(5000, cancellationToken);
    await SupportBrokerPipeCodec.WriteAsync(pipe, request, cancellationToken);
    var result = await SupportBrokerPipeCodec.ReadAsync<SupportBrokerExecution>(
        pipe,
        cancellationToken);
    return result ?? new SupportBrokerExecution(
        SupportBrokerStatus.ExecutionFailed,
        null,
        "Broker returned an empty response.");
  }
}
