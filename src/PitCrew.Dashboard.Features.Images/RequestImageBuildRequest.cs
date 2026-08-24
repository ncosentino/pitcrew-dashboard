using System.Text.Json;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Requests one trusted image build from an exact frozen registration version.
/// </summary>
public sealed record RequestImageBuildRequest(
    Guid RequestId,
    Guid RegistrationId,
    int RegistrationVersion,
    string? SourceRef,
    string? SourceCommit,
    IReadOnlyDictionary<string, JsonElement>? Inputs);
