using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class AlertEvaluationUnitOfWork(
    IAlertEvidenceStore _evidenceStore,
    IAlertIncidentStore _incidentStore,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : IAlertEvaluationUnitOfWork
{
  public async Task EvaluateAsync(CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();
    var options = _options.Value;
    var evidence = await _evidenceStore.LoadAsync(
        now.AddMinutes(-options.AlertResourceWindowMinutes),
        options.AlertResourcePressureSamples + 1,
        cancellationToken);
    var evaluation = AlertRuleEvaluator.Evaluate(
        evidence,
        options,
        now);
    await _incidentStore.ReconcileAsync(
        evaluation.Candidates,
        evaluation.Suppressions,
        now,
        now.AddDays(-options.AlertIncidentRetentionDays),
        options.MaximumResolvedAlertIncidentsPerTenant,
        cancellationToken);
  }
}
