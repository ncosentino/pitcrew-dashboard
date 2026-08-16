namespace PitCrew.Dashboard.Features.Support;

internal interface IRotateSupportIdentityUnitOfWork
{
  Task<SupportIdentityRotationCompletion> RotateAsync(
      RotateSupportIdentityInput input,
      CancellationToken cancellationToken);

  Task<SupportIdentityRotationCompletion> FinalizeAsync(
      FinalizeSupportIdentityRotationInput input,
      CancellationToken cancellationToken);
}
