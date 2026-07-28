namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Requests an expiring one-time connector enrollment code.
/// </summary>
/// <param name="Label">Operator-facing purpose for the code.</param>
public sealed record CreateEnrollmentCodeRequest(string Label);

/// <summary>
/// Returns a one-time connector enrollment code.
/// </summary>
/// <param name="EnrollmentCodeId">Dashboard-assigned code identifier.</param>
/// <param name="Code">Raw code shown only in this response.</param>
/// <param name="ExpiresAt">Time after which redemption is rejected.</param>
public sealed record CreateEnrollmentCodeResponse(
    Guid EnrollmentCodeId,
    string Code,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Requests a new operator-facing name for one enrolled server.
/// </summary>
/// <param name="DisplayName">New operator-facing server name.</param>
public sealed record RenameNodeRequest(string DisplayName);

/// <summary>
/// Requests an absolute maximum for one connector-advertised profile target.
/// </summary>
/// <param name="Maximum">Requested absolute capacity maximum.</param>
public sealed record SetCapacityMaximumRequest(int Maximum);

/// <summary>
/// Returns the queued capacity command.
/// </summary>
/// <param name="CommandId">Dashboard-assigned command identifier.</param>
/// <param name="Status">Initial command status.</param>
public sealed record SetCapacityMaximumResponse(
    Guid CommandId,
    string Status);

/// <summary>
/// Requests recovery of one connector-advertised stalled profile manager.
/// </summary>
/// <param name="ExpectedManagerInstanceId">Manager instance the administrator observed.</param>
/// <param name="ExpectedGeneration">Desired-capacity generation the administrator observed.</param>
/// <param name="ExpectedDesiredStateHash">Desired-state hash the administrator observed, when present.</param>
public sealed record RecoverManagerRequest(
    string ExpectedManagerInstanceId,
    int ExpectedGeneration,
    string? ExpectedDesiredStateHash);

/// <summary>
/// Returns the queued manager-recovery command.
/// </summary>
/// <param name="CommandId">Dashboard-assigned command identifier.</param>
/// <param name="Status">Initial command status.</param>
public sealed record RecoverManagerResponse(
    Guid CommandId,
    string Status);

/// <summary>
/// Returns one visible operational incident.
/// </summary>
/// <param name="IncidentId">Dashboard-assigned incident identifier.</param>
/// <param name="NodeId">Node associated with the incident.</param>
/// <param name="ProfileId">Profile associated with the incident, or <see langword="null"/>.</param>
/// <param name="Kind">Closed alert kind.</param>
/// <param name="Severity">Severity: warning or critical.</param>
/// <param name="Status">Lifecycle status: triggered, acknowledged, or resolved.</param>
/// <param name="Title">Short operator-facing title.</param>
/// <param name="Summary">Latest bounded summary.</param>
/// <param name="Reason">Machine-readable reason.</param>
/// <param name="Evidence">Sanitized bounded evidence, or <see langword="null"/>.</param>
/// <param name="Link">Tenant-scoped UI path for related evidence.</param>
/// <param name="FirstObservedAt">Earliest time the condition was proven present.</param>
/// <param name="TriggeredAt">Time the debounce boundary was reached.</param>
/// <param name="LastObservedAt">Latest evaluation that still observed the condition.</param>
/// <param name="AcknowledgedAt">Time an administrator acknowledged the incident.</param>
/// <param name="AcknowledgedByGitHubUserId">Acknowledging GitHub user identifier.</param>
/// <param name="ResolvedAt">Time the condition cleared.</param>
public sealed record AlertIncidentResponse(
    Guid IncidentId,
    Guid NodeId,
    string? ProfileId,
    string Kind,
    string Severity,
    string Status,
    string Title,
    string Summary,
    string Reason,
    string? Evidence,
    string Link,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset TriggeredAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedByGitHubUserId,
    DateTimeOffset? ResolvedAt);

/// <summary>
/// Returns bounded operational incident history for one tenant.
/// </summary>
/// <param name="GeneratedAt">Dashboard time when the response was generated.</param>
/// <param name="Incidents">Visible incidents ordered newest first.</param>
/// <param name="Truncated">Whether the query limit hid older matching incidents.</param>
public sealed record AlertIncidentListResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AlertIncidentResponse> Incidents,
    bool Truncated);

internal sealed record CreatedEnrollmentCode(
    Guid EnrollmentCodeId,
    string Code,
    DateTimeOffset ExpiresAt);
