using System.Text.Json.Serialization;

namespace PitCrew.Connector.Features.Sync;

internal sealed record ConnectorIdentity(
    string ConnectorInstanceId,
    Guid? NodeId,
    string? Credential);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ConnectorIdentity))]
internal sealed partial class ConnectorIdentityJsonContext : JsonSerializerContext;
