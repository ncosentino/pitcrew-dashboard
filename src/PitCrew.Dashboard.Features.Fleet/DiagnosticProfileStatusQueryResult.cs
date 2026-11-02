namespace PitCrew.Dashboard.Features.Fleet;

internal sealed record DiagnosticProfileStatusQueryResult(
    DiagnosticQueryStatus Status,
    string? Error,
    ProfileOperationalStatusResult? ProfileStatus);
