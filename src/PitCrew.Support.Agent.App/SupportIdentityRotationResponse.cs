namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityRotationResponse(
    SupportIdentityCompletionData? Completion,
    string? FailureDisposition);
