namespace PitCrew.Support.Agent.App;

internal enum SupportAgentProvisioningStatus
{
  Ready,
  ActiveIdentityUnavailable,
  IdentityLifecycleUnavailable,
  EnrollmentMaterialUnavailable,
  PendingIdentityUnavailable,
  EnrollmentRejected,
  LocalEnrollmentCommitFailed,
  LegacyConfigurationUnavailable,
}
