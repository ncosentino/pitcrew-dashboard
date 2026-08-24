using System.Text.Json;

namespace PitCrew.Dashboard.Features.Images;

internal sealed record RequestImageBuildInput(
    Guid RequestId,
    Guid RegistrationId,
    int RegistrationVersion,
    string SourceRef,
    string SourceCommit,
    IReadOnlyDictionary<string, JsonElement> Inputs);
