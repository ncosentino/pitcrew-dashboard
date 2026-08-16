namespace PitCrew.Support.Relay.App;

internal sealed record RelayPollOutcome(
    bool CredentialAccepted,
    RelaySessionRecord? Session);
