namespace PitCrew.Dashboard.Features.Fleet;

internal interface IAlertEvaluationUnitOfWork
{
  Task EvaluateAsync(CancellationToken cancellationToken);
}
