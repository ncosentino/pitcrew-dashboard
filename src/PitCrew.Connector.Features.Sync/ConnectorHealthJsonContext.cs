using System.Text.Json.Serialization;

namespace PitCrew.Connector.Features.Sync;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ConnectorHealthEvent))]
[JsonSerializable(typeof(ConnectorHealthSnapshot))]
[JsonSerializable(typeof(ConnectorHealthAcknowledgementState))]
internal sealed partial class ConnectorHealthJsonContext : JsonSerializerContext;
