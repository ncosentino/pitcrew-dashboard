using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed record SupportAccessDecision(
    bool Allowed,
    string? ActorId,
    DiagnosticAccessScope? DiagnosticScope);
