namespace PitCrew.Support.Agent.App;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed partial class SupportAgentWorker(
    SupportAgentOptions _options,
    SupportRelayTransportClient _relayClient,
    SupportAgentRequestProcessor _processor,
    TimeProvider _timeProvider,
    ILogger<SupportAgentWorker> _logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), _timeProvider);
    do
    {
      try
      {
        await PollOnceAsync(stoppingToken);
      }
      catch (HttpRequestException)
      {
        LogRelayUnavailable(_logger);
      }
      catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
      {
        LogRelayUnavailable(_logger);
      }
      catch (IOException)
      {
        LogBrokerUnavailable(_logger);
      }
      catch (TimeoutException)
      {
        LogBrokerUnavailable(_logger);
      }
      catch (System.Text.Json.JsonException)
      {
        LogRelayResponseInvalid(_logger);
      }
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
    var result = await _processor.ProcessAsync(
        polled.SessionId,
        requestEnvelope,
        cancellationToken);
    if (result is not null &&
        !await _relayClient.UploadResultAsync(
            _options.NodeId,
            polled.SessionId,
            result,
            cancellationToken))
    {
      throw new HttpRequestException("The support relay rejected the result upload.");
    }
  }

  [LoggerMessage(
      EventId = 1,
      Level = LogLevel.Warning,
      Message = "The support relay is temporarily unavailable; polling will retry.")]
  private static partial void LogRelayUnavailable(ILogger logger);

  [LoggerMessage(
      EventId = 2,
      Level = LogLevel.Warning,
      Message = "The local support diagnostics broker is temporarily unavailable; polling will retry.")]
  private static partial void LogBrokerUnavailable(ILogger logger);

  [LoggerMessage(
      EventId = 3,
      Level = LogLevel.Warning,
      Message = "The support relay returned an invalid response; polling will retry.")]
  private static partial void LogRelayResponseInvalid(ILogger logger);
}
