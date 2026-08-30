namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Returns one typed campaign mutation outcome and the resulting campaign when visible.
/// </summary>
/// <param name="Outcome">Typed mutation result.</param>
/// <param name="Campaign">Resulting campaign for success or replay, otherwise <see langword="null"/>.</param>
public sealed record ImageRolloutCampaignMutation(
    ImageRolloutCampaignMutationOutcome Outcome,
    ImageRolloutCampaignState? Campaign);
