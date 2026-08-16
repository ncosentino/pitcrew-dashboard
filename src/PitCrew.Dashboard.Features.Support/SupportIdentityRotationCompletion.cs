using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed record SupportIdentityRotationCompletion(
    SupportMutationStatus Status,
    CreatedSupportEnrollment? Identity);
