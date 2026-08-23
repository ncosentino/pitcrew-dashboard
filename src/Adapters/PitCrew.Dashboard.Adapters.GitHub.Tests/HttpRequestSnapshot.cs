namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

internal sealed record HttpRequestSnapshot(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);
