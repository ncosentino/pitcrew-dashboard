using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("windows")]
internal sealed class SupportBrokerPipeServer(
    SupportBrokerOptions _options,
    SupportDiagnosticsBroker _broker) : ISupportBrokerServer
{
  public async Task RunOnceAsync(CancellationToken cancellationToken)
  {
    var expectedAgentSid = new SecurityIdentifier(_options.ExpectedAgentSid!);
    var brokerServiceSid = new SecurityIdentifier(_options.BrokerServiceSid!);
    var security = WindowsPipeAccessPolicy.Create(
        expectedAgentSid,
        brokerServiceSid);
    await using var pipe = NamedPipeServerStreamAcl.Create(
        _options.PipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous,
        0,
        0,
        security,
        HandleInheritability.None,
        (PipeAccessRights)0);
    await pipe.WaitForConnectionAsync(cancellationToken);
    if (!WindowsNamedPipePeerValidator.IsExpectedClient(
        pipe,
        expectedAgentSid))
    {
      pipe.Disconnect();
      return;
    }
    var request = await SupportBrokerPipeCodec.ReadAsync<SupportBrokerRequest>(
        pipe,
        cancellationToken);
    var response = request is null
        ? new SupportBrokerExecution(SupportBrokerStatus.ExecutionFailed, null, "Request body was empty.")
        : await _broker.ExecuteAsync(request, cancellationToken);
    await SupportBrokerPipeCodec.WriteAsync(pipe, response, cancellationToken);
  }

  public void Dispose()
  {
  }
}
