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

internal sealed record CreatedEnrollmentCode(
    Guid EnrollmentCodeId,
    string Code,
    DateTimeOffset ExpiresAt);
