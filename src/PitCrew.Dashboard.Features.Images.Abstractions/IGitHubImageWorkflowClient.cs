namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Provides the narrow outbound GitHub App transport required for trusted image workflows.
/// </summary>
public interface IGitHubImageWorkflowClient
{
  /// <summary>Loads one exact repository authorized to an installation.</summary>
  /// <param name="installationId">Positive GitHub App installation identity.</param>
  /// <param name="repositoryId">Positive exact GitHub repository identity.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>A bounded repository outcome.</returns>
  Task<GitHubClientOutcome<GitHubRepositoryIdentity>> LoadRepositoryAsync(
      long installationId,
      long repositoryId,
      CancellationToken cancellationToken);

  /// <summary>Loads one exact workflow and its current activation state.</summary>
  /// <param name="installationId">Positive GitHub App installation identity.</param>
  /// <param name="repository">Validated exact repository identity.</param>
  /// <param name="workflowId">Positive exact workflow identity.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>A bounded workflow outcome.</returns>
  Task<GitHubClientOutcome<GitHubWorkflowIdentity>> LoadWorkflowAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      long workflowId,
      CancellationToken cancellationToken);

  /// <summary>Loads the exact workflow file blob identity at one branch or tag.</summary>
  /// <param name="installationId">Positive GitHub App installation identity.</param>
  /// <param name="repository">Validated exact repository identity.</param>
  /// <param name="workflowPath">Validated repository-relative workflow path.</param>
  /// <param name="reference">Exact branch or tag.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>A bounded workflow-file outcome.</returns>
  Task<GitHubClientOutcome<GitHubWorkflowFileRevision>> LoadWorkflowFileRevisionAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      string workflowPath,
      string reference,
      CancellationToken cancellationToken);

  /// <summary>Resolves one exact commit identity in a repository.</summary>
  /// <param name="installationId">Positive GitHub App installation identity.</param>
  /// <param name="repository">Validated exact repository identity.</param>
  /// <param name="commitSha">Exact lowercase 40-character commit SHA.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>A bounded commit outcome.</returns>
  Task<GitHubClientOutcome<GitHubCommitIdentity>> ResolveCommitAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      string commitSha,
      CancellationToken cancellationToken);

  /// <summary>Verifies whether an allowed source ref contains one exact source commit.</summary>
  /// <param name="installationId">Positive GitHub App installation identity.</param>
  /// <param name="repository">Validated exact repository identity.</param>
  /// <param name="sourceCommitSha">Exact lowercase 40-character source commit SHA.</param>
  /// <param name="allowedSourceReference">Exact allowed branch or tag.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>A bounded reachability outcome.</returns>
  Task<GitHubClientOutcome<GitHubCommitReachability>> VerifyCommitReachableAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      string sourceCommitSha,
      string allowedSourceReference,
      CancellationToken cancellationToken);

  /// <summary>
  /// Dispatches one exact workflow at one exact branch or tag using a bounded caller-supplied input map.
  /// </summary>
  /// <remarks>
  /// The transport performs one dispatch attempt and never retries this side
  /// effect. A transient outcome may be indeterminate; orchestration must not
  /// issue another dispatch without its own durable deduplication decision.
  /// </remarks>
  /// <param name="installationId">Positive GitHub App installation identity.</param>
  /// <param name="repository">Validated exact repository identity.</param>
  /// <param name="workflowId">Positive exact workflow identity.</param>
  /// <param name="reference">Exact reviewed branch or tag.</param>
  /// <param name="inputs">Bounded validated workflow input values.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>The exact run identity and URLs from the pinned dispatch response.</returns>
  Task<GitHubClientOutcome<GitHubWorkflowDispatch>> DispatchWorkflowAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      long workflowId,
      string reference,
      IReadOnlyDictionary<string, string> inputs,
      CancellationToken cancellationToken);

  /// <summary>Loads one exact workflow run.</summary>
  /// <param name="installationId">Positive GitHub App installation identity.</param>
  /// <param name="repository">Validated exact repository identity.</param>
  /// <param name="runId">Positive exact workflow run identity.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>A bounded workflow-run outcome.</returns>
  Task<GitHubClientOutcome<GitHubWorkflowRun>> LoadWorkflowRunAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      long runId,
      CancellationToken cancellationToken);

  /// <summary>Lists bounded artifact metadata for one exact workflow run.</summary>
  /// <param name="installationId">Positive GitHub App installation identity.</param>
  /// <param name="repository">Validated exact repository identity.</param>
  /// <param name="runId">Positive exact workflow run identity.</param>
  /// <param name="limit">Positive maximum number of artifacts, no greater than 100.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>A bounded artifact-list outcome.</returns>
  Task<GitHubClientOutcome<GitHubWorkflowArtifactList>> ListWorkflowRunArtifactsAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      long runId,
      int limit,
      CancellationToken cancellationToken);
}
