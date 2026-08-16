namespace PitCrew.Support.Broker.App;

internal sealed record SupportEvidenceValidation(
    SupportBrokerStatus Status,
    string? ProfileId,
    string? CollectorPath,
    string? Error)
{
  public bool Succeeded => Status == SupportBrokerStatus.Succeeded;
}
