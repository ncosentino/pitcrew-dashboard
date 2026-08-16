namespace PitCrew.Support.Agent.App;

internal sealed record SupportRelayPollOutcome(
    bool CredentialAccepted,
    AgentRelayPollResponse? Response);
