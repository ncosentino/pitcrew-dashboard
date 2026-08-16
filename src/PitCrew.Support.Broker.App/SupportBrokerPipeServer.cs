using System.IO.Pipes;
namespace PitCrew.Support.Broker.App;

internal sealed class SupportBrokerPipeServer(
    SupportBrokerOptions _options,
    SupportDiagnosticsBroker _broker)
{
  public async Task RunOnceAsync(CancellationToken cancellationToken)
  {
    await using var pipe = new NamedPipeServerStream(
        _options.PipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.WaitForConnectionAsync(cancellationToken);
    var request = await SupportBrokerPipeCodec.ReadAsync<SupportBrokerRequest>(
        pipe,
        cancellationToken);
    var response = request is null
        ? new SupportBrokerExecution(SupportBrokerStatus.ExecutionFailed, null, "Request body was empty.")
        : await _broker.ExecuteAsync(request, cancellationToken);
    await SupportBrokerPipeCodec.WriteAsync(pipe, response, cancellationToken);
  }
}
