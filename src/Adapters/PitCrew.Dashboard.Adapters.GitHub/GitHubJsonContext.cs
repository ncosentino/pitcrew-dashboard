using System.Text.Json.Serialization;

namespace PitCrew.Dashboard.Adapters.GitHub;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GitHubAppJwtPayload))]
[JsonSerializable(typeof(GitHubInstallationTokenPayload))]
[JsonSerializable(typeof(GitHubInstallationTokenReplyPayload))]
[JsonSerializable(typeof(GitHubRepositoryPayload))]
[JsonSerializable(typeof(GitHubWorkflowPayload))]
[JsonSerializable(typeof(GitHubContentPayload))]
[JsonSerializable(typeof(GitHubCommitPayload))]
[JsonSerializable(typeof(GitHubComparePayload))]
[JsonSerializable(typeof(GitHubDispatchPayload))]
[JsonSerializable(typeof(GitHubDispatchResultPayload))]
[JsonSerializable(typeof(GitHubWorkflowRunPayload))]
[JsonSerializable(typeof(GitHubArtifactListPayload))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
