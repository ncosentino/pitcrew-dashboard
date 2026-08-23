namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubDispatchPayload(
    string Ref,
    IReadOnlyDictionary<string, string> Inputs);
