using System.Text.Json.Serialization;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Provides source-generated JSON metadata for the local recovery execution ledger.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RecoveryLedgerEntry))]
internal sealed partial class RecoveryLedgerJsonContext : JsonSerializerContext;
