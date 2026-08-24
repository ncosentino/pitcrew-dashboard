namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Selects the phase-local durable not-found counter mutation for a deferred poll.
/// </summary>
public enum ImageBuildNotFoundCounterAction
{
  Preserve = 0,
  Increment = 1,
  Reset = 2,
}
