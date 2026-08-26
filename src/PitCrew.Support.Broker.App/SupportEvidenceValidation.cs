namespace PitCrew.Support.Broker.App;

internal sealed record SupportEvidenceValidation(
    SupportBrokerStatus Status,
    string? ProfileId,
    string? CollectorPath,
    string? Error,
    string? FailureStage = null)
{
  public bool Succeeded => Status == SupportBrokerStatus.Succeeded;
}
