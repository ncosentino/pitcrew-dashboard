namespace PitCrew.Dashboard.Features.Fleet;

internal enum ProfileRuntimeState
{
  Active,
  Idle,
  IdleScaleToZero,
  ScalingUp,
  ScalingDown,
  Paused,
  ManagerStopped,
  Degraded,
  Stale,
}
