using System.Text.Json.Serialization;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Provides source-generated JSON metadata for the local image-rollout ledger.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ImageRolloutLedgerEntry))]
internal sealed partial class ImageRolloutLedgerJsonContext : JsonSerializerContext;
