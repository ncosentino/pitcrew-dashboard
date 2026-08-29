using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Advertises the locally enabled profile-image rollout surface.
/// </summary>
/// <param name="Profiles">Profiles whose current image may be replaced.</param>
public sealed record ImageRolloutOperatorCapability(
    [property: JsonRequired]
    IReadOnlyList<ImageRolloutOperatorProfile> Profiles);
