using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one profile whose stalled manager may be recovered remotely.
/// </summary>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="ManagerContractVersion">Runtime compatibility contract implemented by the manager.</param>
/// <param name="ManagerContractSupported">Whether the local recovery implementation supports that contract.</param>
/// <param name="ExpectedManagerInstanceId">Manager instance that must still own the profile before execution.</param>
/// <param name="DesiredGeneration">Desired-capacity generation that must still be current.</param>
/// <param name="DesiredStateHash">Desired-state hash that must still be current, or <see langword="null"/>.</param>
/// <param name="ObservedStateAgeSeconds">Age of the locally readable observed state.</param>
/// <param name="RecoveryAllowed">Whether the local recovery allowlist includes the profile.</param>
/// <param name="SingleManagerResolved">Whether exactly one running manager is locally resolvable.</param>
/// <param name="OperationActive">Whether another local profile operation is already running.</param>
/// <param name="CommandTimeoutSeconds">Bounded local execution timeout.</param>
/// <param name="MaximumExpirySeconds">Longest command lifetime the connector accepts.</param>
public sealed record RecoveryOperatorProfile(
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] int ManagerContractVersion,
    [property: JsonRequired] bool ManagerContractSupported,
    [property: JsonRequired] string? ExpectedManagerInstanceId,
    [property: JsonRequired] int DesiredGeneration,
    [property: JsonRequired] string? DesiredStateHash,
    [property: JsonRequired] int ObservedStateAgeSeconds,
    [property: JsonRequired] bool RecoveryAllowed,
    [property: JsonRequired] bool SingleManagerResolved,
    [property: JsonRequired] bool OperationActive,
    [property: JsonRequired] int CommandTimeoutSeconds,
    [property: JsonRequired] int MaximumExpirySeconds);

/// <summary>
/// Advertises the locally enabled manager-recovery surface.
/// </summary>
/// <param name="Profiles">Profiles whose manager may be recovered.</param>
public sealed record RecoveryOperatorCapability(
    [property: JsonRequired]
    IReadOnlyList<RecoveryOperatorProfile> Profiles);

/// <summary>
/// Requests recovery of one stalled profile manager through an outbound connector.
/// </summary>
/// <param name="CommandId">Dashboard-assigned at-most-once command identifier.</param>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="ExpectedManagerInstanceId">Manager instance that must still own the profile.</param>
/// <param name="ExpectedGeneration">Desired-capacity generation that must still be current.</param>
/// <param name="ExpectedDesiredStateHash">Desired-state hash that must still be current, or <see langword="null"/>.</param>
/// <param name="RequestedAt">Dashboard time when the command was queued.</param>
/// <param name="ExpiresAt">Time after which the connector must reject the command.</param>
public sealed record RecoverManagerCommand(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] string ExpectedManagerInstanceId,
    [property: JsonRequired] int ExpectedGeneration,
    [property: JsonRequired] string? ExpectedDesiredStateHash,
    [property: JsonRequired] DateTimeOffset RequestedAt,
    [property: JsonRequired] DateTimeOffset ExpiresAt);

/// <summary>
/// Reports that one recovery command was durably claimed or started locally.
/// </summary>
/// <param name="CommandId">Command identifier supplied by the dashboard.</param>
/// <param name="Phase">Durable local phase: claimed or started.</param>
/// <param name="ReportedAt">Connector time when the phase was durably recorded.</param>
public sealed record RecoveryCommandProgress(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Phase,
    [property: JsonRequired] DateTimeOffset ReportedAt);

/// <summary>
/// Reports the locally observed terminal result of one recovery command.
/// </summary>
/// <param name="CommandId">Command identifier supplied by the dashboard.</param>
/// <param name="Status">Terminal status: succeeded, rejected, failed, or indeterminate.</param>
/// <param name="FailureCategory">Bounded non-success category, or <see langword="null"/> after success.</param>
/// <param name="Message">Bounded operator-facing result detail.</param>
/// <param name="BeforeManagerInstanceId">Manager instance observed before execution.</param>
/// <param name="AfterManagerInstanceId">Manager instance observed after execution.</param>
/// <param name="CompletedAt">Connector time when the command reached a terminal state.</param>
public sealed record RecoveryCommandOutcome(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Status,
    [property: JsonRequired] string? FailureCategory,
    [property: JsonRequired] string? Message,
    [property: JsonRequired] string? BeforeManagerInstanceId,
    [property: JsonRequired] string? AfterManagerInstanceId,
    [property: JsonRequired] DateTimeOffset CompletedAt);
