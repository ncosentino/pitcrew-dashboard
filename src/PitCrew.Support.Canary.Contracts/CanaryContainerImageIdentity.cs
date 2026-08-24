namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Identifies one exact locally built canary container image.
/// </summary>
/// <param name="Reference">Run-scoped local image reference.</param>
/// <param name="ImageId">Docker content-addressed image identifier.</param>
public sealed record CanaryContainerImageIdentity(
    string Reference,
    string ImageId);
