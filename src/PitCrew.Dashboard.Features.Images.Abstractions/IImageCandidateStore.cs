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
