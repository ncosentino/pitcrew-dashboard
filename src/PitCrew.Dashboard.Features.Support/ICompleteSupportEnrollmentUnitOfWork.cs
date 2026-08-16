namespace PitCrew.Dashboard.Features.Support;

internal interface ICompleteSupportEnrollmentUnitOfWork
{
  Task<SupportEnrollmentCompletion> CompleteAsync(
      CompleteSupportEnrollmentInput input,
      CancellationToken cancellationToken);
}
