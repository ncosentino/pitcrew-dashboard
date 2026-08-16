namespace PitCrew.Support.Agent.App;

using Microsoft.Extensions.Hosting;

internal sealed class SupportAgentWorker(
    SupportAgentOptions _options,
    SupportRelayTransportClient _relayClient,
    SupportAgentRequestProcessor _processor,
    TimeProvider _timeProvider) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), _timeProvider);
    do
    {
      await PollOnceAsync(stoppingToken);
    }
    while (await timer.WaitForNextTickAsync(stoppingToken));
  }

  private async Task PollOnceAsync(CancellationToken cancellationToken)
  {
    var polled = await _relayClient.PollAsync(_options.NodeId, cancellationToken);
    var requestEnvelope = polled?.GetRequestEnvelopeOrNull();
    if (polled is null || requestEnvelope is null)
    {
      return;
    }
    var result = await _processor.ProcessAsync(requestEnvelope, cancellationToken);
    if (result is not null)
    {
      await _relayClient.UploadResultAsync(_options.NodeId, polled.SessionId, result, cancellationToken);
    }
  }
}

