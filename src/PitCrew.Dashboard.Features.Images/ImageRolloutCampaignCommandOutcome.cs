using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Images;

internal sealed record ImageRolloutCampaignCommandOutcome(
    ImageRolloutCampaignCommandStatus Status,
    ImageRolloutCampaignState? Campaign,
    string? Code,
    string? Error);
