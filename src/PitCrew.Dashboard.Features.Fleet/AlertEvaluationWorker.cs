using System.Data.Common;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class AlertEvaluationWorker(
    IAlertEvaluationUnitOfWork _unitOfWork,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider,
    ILogger<AlertEvaluationWorker> _logger) : BackgroundService
{
  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(
        TimeSpan.FromSeconds(_options.Value.AlertEvaluationSeconds),
        _timeProvider);
    try
    {
      while (await timer.WaitForNextTickAsync(stoppingToken))
      {
        try
        {
          await _unitOfWork.EvaluateAsync(stoppingToken);
        }
        catch (DbException exception)
        {
          _logger.LogWarning(
              exception,
              "Alert evaluation could not access durable evidence and will retry.");
        }
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
    }
  }
}
