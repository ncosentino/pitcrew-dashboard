using System.Text.Json.Serialization;

namespace PitCrew.Connector.Features.Sync;

internal static class ConnectorHealthStates
{
  public const string Starting = "starting";
  public const string Healthy = "healthy";
  public const string Degraded = "degraded";
  public const string Stopping = "stopping";
}

internal static class ConnectorHealthEventKinds
{
  public const string ProcessStarted = "process-started";
  public const string ProcessStopping = "process-stopping";
  public const string SynchronizationSucceeded = "synchronization-succeeded";
  public const string SynchronizationFailed = "synchronization-failed";
  public const string ObservationIncomplete = "observation-incomplete";
  public const string EnrollmentFailed = "enrollment-failed";
  public const string Rejected = "rejected";
  public const string Recovered = "recovered";
}

internal static class ConnectorHealthFailureCategories
{
  public const string StateRootMissing = "state-root-missing";
  public const string StateRootUnreadable = "state-root-unreadable";
  public const string ProfileDirectoryUnreadable = "profile-directory-unreadable";
  public const string ProfileStateInvalid = "profile-state-invalid";
  public const string ProfileStateUnreadable = "profile-state-unreadable";
  public const string SynchronizationNetwork = "synchronization-network";
  public const string SynchronizationTimeout = "synchronization-timeout";
  public const string SynchronizationRateLimited = "synchronization-rate-limited";
  public const string SynchronizationServer = "synchronization-server";
  public const string SynchronizationIo = "synchronization-io";
  public const string PayloadRejected = "payload-rejected";
  public const string CredentialRejected = "credential-rejected";
  public const string EnrollmentRejected = "enrollment-rejected";
  public const string EnrollmentNetwork = "enrollment-network";
  public const string EnrollmentTimeout = "enrollment-timeout";
  public const string EnrollmentRateLimited = "enrollment-rate-limited";
  public const string EnrollmentServer = "enrollment-server";
  public const string ConfigurationInvalid = "configuration-invalid";
  public const string EnrollmentConfiguration = "enrollment-configuration";
}

internal sealed record ConnectorHealthFailure(
    string Category,
    string Detail,
    string? ProfileId = null);

internal sealed record ConnectorHealthEvent(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] Guid EventId,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] DateTimeOffset OccurredAt,
    [property: JsonRequired] string State,
    [property: JsonRequired] Guid? OutageId,
    [property: JsonRequired] DateTimeOffset? OutageStartedAt,
    [property: JsonRequired] string? FailureCategory,
    [property: JsonRequired] string? ProfileId,
    [property: JsonRequired] int ConsecutiveFailures,
    [property: JsonRequired] int? RetryDelaySeconds,
    [property: JsonRequired] string? Detail);

internal sealed record ConnectorHealthSnapshot(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string State,
    [property: JsonRequired] DateTimeOffset ProcessStartedAt,
    [property: JsonRequired] DateTimeOffset UpdatedAt,
    [property: JsonRequired] DateTimeOffset? LastAttemptAt,
    [property: JsonRequired] DateTimeOffset? LastSuccessAt,
    [property: JsonRequired] Guid? ActiveOutageId,
    [property: JsonRequired] DateTimeOffset? ActiveOutageStartedAt,
    [property: JsonRequired] DateTimeOffset? LastFailureAt,
    [property: JsonRequired] string? LastFailureCategory,
    [property: JsonRequired] string? LastFailureProfileId,
    [property: JsonRequired] string? LastFailureDetail,
    [property: JsonRequired] int ConsecutiveFailures,
    [property: JsonRequired] DateTimeOffset? NextRetryAt,
    [property: JsonRequired] Guid? LastRecoveredOutageId,
    [property: JsonRequired] DateTimeOffset? LastRecoveredOutageStartedAt,
    [property: JsonRequired] DateTimeOffset? LastRecoveredAt,
    [property: JsonRequired] string? LastRecoveredFailureCategory);
