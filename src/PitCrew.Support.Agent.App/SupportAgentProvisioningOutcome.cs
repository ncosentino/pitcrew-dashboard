namespace PitCrew.Support.Agent.App;

internal sealed record SupportAgentProvisioningOutcome(
    SupportAgentProvisioningStatus Status,
    SupportAgentOptions? Options);
