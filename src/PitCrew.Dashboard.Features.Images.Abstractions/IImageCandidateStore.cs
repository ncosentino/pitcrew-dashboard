namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Persists tenant-scoped reviewed image recipes, build requests, and immutable candidates.
/// </summary>
public interface IImageCandidateStore
{
  /// <summary>
  /// Creates one immutable recipe registration version.
  /// </summary>
  /// <param name="registration">Registration version to persist.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Typed mutation outcome.</returns>
  Task<ImageCandidateMutationResult> CreateRecipeVersionAsync(
      ImageRecipeRegistration registration,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists bounded tenant-owned image recipe registrations.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the registrations.</param>
  /// <param name="includeDisabled">Whether disabled registrations should be included.</param>
  /// <param name="limit">Maximum rows returned; callers must supply a positive bounded value.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Newest registrations first.</returns>
  Task<IReadOnlyList<ImageRecipeRegistration>> ListRecipeRegistrationsAsync(
      string tenantId,
      bool includeDisabled,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists bounded recipe versions for one tenant and recipe.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the registrations.</param>
  /// <param name="recipeId">Recipe whose versions are returned.</param>
  /// <param name="limit">Maximum rows returned; callers must supply a positive bounded value.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Newest registration versions first.</returns>
  Task<IReadOnlyList<ImageRecipeRegistration>> ListRecipeVersionsAsync(
      string tenantId,
      string recipeId,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads one tenant-owned registration by its caller-supplied GUID.
  /// </summary>
  /// <param name="tenantId">Tenant expected to own the registration.</param>
  /// <param name="registrationId">Caller-supplied registration identity.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The registration, or <see langword="null" /> when absent from the tenant.</returns>
  Task<ImageRecipeRegistration?> GetRecipeRegistrationOrNullAsync(
      string tenantId,
      Guid registrationId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists bounded versions for one tenant-owned registration.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the registration.</param>
  /// <param name="registrationId">Stable registration identity.</param>
  /// <param name="limit">Maximum rows returned; callers must supply a positive bounded value.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Newest registration versions first.</returns>
  Task<IReadOnlyList<ImageRecipeRegistration>> ListRegistrationVersionsAsync(
      string tenantId,
      Guid registrationId,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads one tenant-owned registration version.
  /// </summary>
  /// <param name="tenantId">Tenant expected to own the registration.</param>
  /// <param name="registrationId">Stable registration identity.</param>
  /// <param name="version">Registration version.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The registration, or <see langword="null" /> when absent from the tenant.</returns>
  Task<ImageRecipeRegistration?> GetRecipeVersionOrNullAsync(
      string tenantId,
      Guid registrationId,
      int version,
      CancellationToken cancellationToken);

  /// <summary>
  /// Disables one registration without rewriting its frozen identity.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the registration.</param>
  /// <param name="registrationId">Caller-supplied registration identity.</param>
  /// <param name="disabledByGitHubUserId">GitHub user that disables the registration.</param>
  /// <param name="disabledAt">Caller-supplied disable time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Typed mutation outcome.</returns>
  Task<ImageCandidateMutationResult> DisableRecipeRegistrationAsync(
      string tenantId,
      Guid registrationId,
      string disabledByGitHubUserId,
      DateTimeOffset disabledAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Disables one registration version without rewriting its frozen identity.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the registration.</param>
  /// <param name="registrationId">Stable registration identity.</param>
  /// <param name="version">Registration version.</param>
  /// <param name="disabledByGitHubUserId">GitHub user that disables the version.</param>
  /// <param name="disabledAt">Caller-supplied disable time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Typed mutation outcome.</returns>
  Task<ImageCandidateMutationResult> DisableRecipeVersionAsync(
      string tenantId,
      Guid registrationId,
      int version,
      string disabledByGitHubUserId,
      DateTimeOffset disabledAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Creates one durable build request in the requested state.
  /// </summary>
  /// <param name="request">Build request to persist.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Typed mutation outcome.</returns>
  Task<ImageCandidateMutationResult> CreateBuildRequestAsync(
      ImageBuildRequest request,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists bounded build requests for one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the requests.</param>
  /// <param name="status">Optional status filter.</param>
  /// <param name="limit">Maximum rows returned; callers must supply a positive bounded value.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Newest requests first.</returns>
  Task<IReadOnlyList<ImageBuildRequest>> ListBuildRequestsAsync(
      string tenantId,
      ImageBuildRequestStatus? status,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads one tenant-owned build request.
  /// </summary>
  /// <param name="tenantId">Tenant expected to own the request.</param>
  /// <param name="requestId">Dashboard request identity.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The request, or <see langword="null" /> when absent from the tenant.</returns>
  Task<ImageBuildRequest?> GetBuildRequestOrNullAsync(
      string tenantId,
      Guid requestId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists bounded tenant-owned immutable candidates with complete qualifications.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the candidates.</param>
  /// <param name="limit">Maximum candidates returned.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Newest candidates first with bounded evidence.</returns>
  Task<IReadOnlyList<ImageCandidateDetails>> ListCandidatesAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads one tenant-owned immutable candidate with complete qualifications.
  /// </summary>
  /// <param name="tenantId">Tenant expected to own the candidate.</param>
  /// <param name="candidateId">Candidate identity.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The candidate details, or <see langword="null" /> when absent.</returns>
  Task<ImageCandidateDetails?> GetCandidateOrNullAsync(
      string tenantId,
      Guid candidateId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Transactionally leases a deterministic bounded batch of due active requests.
  /// </summary>
  Task<IReadOnlyList<ImageBuildExecutionClaim>> ClaimDueBuildRequestsAsync(
      string leaseOwner,
      DateTimeOffset now,
      DateTimeOffset leaseExpiresAt,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Durably records that dispatch may be in flight before the external side effect begins.
  /// </summary>
  Task<ImageCandidateMutationResult> MarkDispatchStartedAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      DateTimeOffset startedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Defers retryable read-only dispatch authority validation without making
  /// dispatch indeterminate.
  /// </summary>
  Task<ImageCandidateMutationResult> DeferDispatchAuthorityAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      DateTimeOffset retryAt,
      string externalStatus,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Defers a definitively unaccepted dispatch and marks it safe to retry.
  /// </summary>
  Task<ImageCandidateMutationResult> DeferRateLimitedDispatchAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      DateTimeOffset retryAt,
      string externalStatus,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Freezes the exact accepted GitHub run identity and advances to building.
  /// </summary>
  Task<ImageCandidateMutationResult> RecordDispatchSucceededAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      long runId,
      string runApiUrl,
      string runHtmlUrl,
      DateTimeOffset nextPollAt,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Defers exact-run polling without changing the building lifecycle state.
  /// </summary>
  Task<ImageCandidateMutationResult> DeferBuildRunPollAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      DateTimeOffset nextPollAt,
      string externalStatus,
      ImageBuildNotFoundCounterAction notFoundCounterAction,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Records a definitive exact-run observation while retaining the lease for
  /// workflow-revision verification.
  /// </summary>
  Task<ImageCandidateMutationResult> MarkBuildRunObservedAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Defers exact workflow-revision verification without changing the building state.
  /// </summary>
  Task<ImageCandidateMutationResult> DeferBuildRevisionPollAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      DateTimeOffset nextPollAt,
      string externalStatus,
      ImageBuildNotFoundCounterAction notFoundCounterAction,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Records successful exact workflow-revision validation while retaining
  /// the current lease for lifecycle completion.
  /// </summary>
  Task<ImageCandidateMutationResult> MarkBuildRevisionObservedAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Advances a successfully completed exact run to qualifying.
  /// </summary>
  Task<ImageCandidateMutationResult> MarkBuildQualifyingAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      string externalStatus,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Defers retryable exact-artifact qualification without changing the qualifying state.
  /// </summary>
  Task<ImageCandidateMutationResult> DeferCandidateQualificationAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      DateTimeOffset nextPollAt,
      string externalStatus,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Terminalizes a leased active request with bounded blocked or failed evidence.
  /// </summary>
  Task<ImageCandidateMutationResult> TerminalizeBuildRequestAsync(
      string tenantId,
      Guid requestId,
      string leaseOwner,
      ImageBuildRequestStatus terminalStatus,
      string category,
      string detail,
      string externalStatus,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically applies one optimistic monotonic request transition.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the request.</param>
  /// <param name="requestId">Dashboard request identity.</param>
  /// <param name="transition">Expected and requested lifecycle values.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Typed mutation outcome.</returns>
  Task<ImageCandidateMutationResult> ApplyBuildRequestTransitionAsync(
      string tenantId,
      Guid requestId,
      ImageBuildRequestTransition transition,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically stores one immutable candidate and its complete qualification set while terminalizing its qualifying request.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the request and candidate.</param>
  /// <param name="candidate">Schema-valid ready or failed candidate.</param>
  /// <param name="qualifications">Complete bounded schema version 1 qualification set.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Typed mutation outcome.</returns>
  Task<ImageCandidateMutationResult> StoreCandidateAsync(
      string tenantId,
      ImageCandidate candidate,
      IReadOnlyList<ImageCandidateQualification> qualifications,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically purges a bounded deterministic batch of old terminal requests and their candidate evidence.
  /// </summary>
  /// <param name="tenantId">Tenant whose terminal history is purged.</param>
  /// <param name="olderThan">Exclusive updated-time cutoff; newer and active requests are preserved.</param>
  /// <param name="limit">Maximum requests deleted; the store clamps this to its bounded retention batch size.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Number of terminal requests deleted.</returns>
  Task<int> PurgeTerminalBuildRequestsAsync(
      string tenantId,
      DateTimeOffset olderThan,
      int limit,
      CancellationToken cancellationToken);
}
