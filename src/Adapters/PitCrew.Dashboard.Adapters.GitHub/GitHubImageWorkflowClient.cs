using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed class GitHubImageWorkflowClient(
    IHttpClientFactory _httpClientFactory,
    GitHubAppTokenProvider _tokenProvider,
    IOptions<GitHubAppOptions> _options,
    TimeProvider _timeProvider) : IGitHubImageWorkflowClient
{
  internal const string ApiVersion = "2026-03-10";

  public Task<GitHubClientOutcome<GitHubRepositoryIdentity>> LoadRepositoryAsync(
      long installationId,
      long repositoryId,
      CancellationToken cancellationToken)
  {
    if (!_options.Value.Enabled)
    {
      return NotConfiguredAsync<GitHubRepositoryIdentity>(cancellationToken);
    }
    if (!GitHubTransportValidation.IsPositiveId(installationId) ||
        !GitHubTransportValidation.IsPositiveId(repositoryId))
    {
      return InvalidRequestAsync<GitHubRepositoryIdentity>(
          "installation-or-repository-id-invalid",
          cancellationToken);
    }

    return ExecuteAsync(
        installationId,
        repositoryId,
        token => CreateRequest(
            HttpMethod.Get,
            $"repositories/{repositoryId}",
            token),
        GitHubJsonContext.Default.GitHubRepositoryPayload,
        response =>
        {
          if (response.Id != repositoryId ||
              !GitHubTransportValidation.IsOwner(response.Owner?.Login) ||
              !GitHubTransportValidation.IsRepositoryName(response.Name))
          {
            return InvalidResponse<GitHubRepositoryIdentity>(
                "repository-response-invalid");
          }
          return Success(
              new GitHubRepositoryIdentity(
                  response.Id,
                  response.Owner!.Login!,
                  response.Name!));
        },
        cancellationToken);
  }

  public Task<GitHubClientOutcome<GitHubWorkflowIdentity>> LoadWorkflowAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      long workflowId,
      CancellationToken cancellationToken)
  {
    if (!_options.Value.Enabled)
    {
      return NotConfiguredAsync<GitHubWorkflowIdentity>(cancellationToken);
    }
    if (!IsOperationIdentityValid(installationId, repository) ||
        !GitHubTransportValidation.IsPositiveId(workflowId))
    {
      return InvalidRequestAsync<GitHubWorkflowIdentity>(
          "workflow-request-invalid",
          cancellationToken);
    }

    var path =
        $"{GitHubTransportValidation.RepositoryPath(repository)}/actions/workflows/{workflowId}";
    return ExecuteAsync(
        installationId,
        repository.Id,
        token => CreateRequest(HttpMethod.Get, path, token),
        GitHubJsonContext.Default.GitHubWorkflowPayload,
        response =>
        {
          if (response.Id != workflowId ||
              !GitHubTransportValidation.IsBoundedText(response.Name, 256) ||
              !GitHubTransportValidation.IsWorkflowPath(response.Path))
          {
            return InvalidResponse<GitHubWorkflowIdentity>(
                "workflow-response-invalid");
          }
          return Success(
              new GitHubWorkflowIdentity(
                  response.Id,
                  response.Name!,
                  response.Path!,
                  MapWorkflowState(response.State)));
        },
        cancellationToken);
  }

  public Task<GitHubClientOutcome<GitHubWorkflowFileRevision>>
      LoadWorkflowFileRevisionAsync(
          long installationId,
          GitHubRepositoryIdentity repository,
          string workflowPath,
          string reference,
          CancellationToken cancellationToken)
  {
    if (!_options.Value.Enabled)
    {
      return NotConfiguredAsync<GitHubWorkflowFileRevision>(
          cancellationToken);
    }
    if (!IsOperationIdentityValid(installationId, repository) ||
        !GitHubTransportValidation.IsWorkflowPath(workflowPath) ||
        !GitHubTransportValidation.IsReference(reference))
    {
      return InvalidRequestAsync<GitHubWorkflowFileRevision>(
          "workflow-file-request-invalid",
          cancellationToken);
    }

    var path =
        $"{GitHubTransportValidation.RepositoryPath(repository)}/contents/" +
        $"{GitHubTransportValidation.EncodeWorkflowPath(workflowPath)}" +
        $"?ref={GitHubTransportValidation.Encode(reference)}";
    return ExecuteAsync(
        installationId,
        repository.Id,
        token => CreateRequest(HttpMethod.Get, path, token),
        GitHubJsonContext.Default.GitHubContentPayload,
        response =>
        {
          if (response.Type != "file" ||
              response.Path != workflowPath ||
              !GitHubTransportValidation.IsSha1(response.Sha))
          {
            return InvalidResponse<GitHubWorkflowFileRevision>(
                "workflow-file-response-invalid");
          }
          return Success(
              new GitHubWorkflowFileRevision(
                  response.Path,
                  response.Sha!,
                  reference));
        },
        cancellationToken);
  }

  public Task<GitHubClientOutcome<GitHubCommitIdentity>> ResolveCommitAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      string commitSha,
      CancellationToken cancellationToken)
  {
    if (!_options.Value.Enabled)
    {
      return NotConfiguredAsync<GitHubCommitIdentity>(cancellationToken);
    }
    if (!IsOperationIdentityValid(installationId, repository) ||
        !GitHubTransportValidation.IsSha1(commitSha))
    {
      return InvalidRequestAsync<GitHubCommitIdentity>(
          "commit-request-invalid",
          cancellationToken);
    }

    var path =
        $"{GitHubTransportValidation.RepositoryPath(repository)}/commits/{commitSha}";
    return ExecuteAsync(
        installationId,
        repository.Id,
        token => CreateRequest(HttpMethod.Get, path, token),
        GitHubJsonContext.Default.GitHubCommitPayload,
        response => response.Sha == commitSha
            ? Success(new GitHubCommitIdentity(response.Sha))
            : InvalidResponse<GitHubCommitIdentity>(
                "commit-response-invalid"),
        cancellationToken);
  }

  public Task<GitHubClientOutcome<GitHubCommitReachability>>
      VerifyCommitReachableAsync(
          long installationId,
          GitHubRepositoryIdentity repository,
          string sourceCommitSha,
          string allowedSourceReference,
          CancellationToken cancellationToken)
  {
    if (!_options.Value.Enabled)
    {
      return NotConfiguredAsync<GitHubCommitReachability>(
          cancellationToken);
    }
    if (!IsOperationIdentityValid(installationId, repository) ||
        !GitHubTransportValidation.IsSha1(sourceCommitSha) ||
        !GitHubTransportValidation.IsReference(allowedSourceReference))
    {
      return InvalidRequestAsync<GitHubCommitReachability>(
          "compare-request-invalid",
          cancellationToken);
    }

    var path =
        $"{GitHubTransportValidation.RepositoryPath(repository)}/compare/" +
        $"{sourceCommitSha}...{GitHubTransportValidation.Encode(allowedSourceReference)}";
    return ExecuteAsync(
        installationId,
        repository.Id,
        token => CreateRequest(HttpMethod.Get, path, token),
        GitHubJsonContext.Default.GitHubComparePayload,
        response => response.Status switch
        {
          "ahead" or "identical" =>
              Success(new GitHubCommitReachability(true)),
          "behind" or "diverged" =>
              Success(new GitHubCommitReachability(false)),
          _ => InvalidResponse<GitHubCommitReachability>(
              "compare-response-invalid"),
        },
        cancellationToken);
  }

  public Task<GitHubClientOutcome<GitHubWorkflowDispatch>>
      DispatchWorkflowAsync(
          long installationId,
          GitHubRepositoryIdentity repository,
          long workflowId,
          string reference,
          IReadOnlyDictionary<string, string> inputs,
          CancellationToken cancellationToken)
  {
    if (!_options.Value.Enabled)
    {
      return NotConfiguredAsync<GitHubWorkflowDispatch>(
          cancellationToken);
    }
    if (!IsOperationIdentityValid(installationId, repository) ||
        !GitHubTransportValidation.IsPositiveId(workflowId) ||
        !GitHubTransportValidation.IsReference(reference) ||
        !GitHubTransportValidation.CopyInputs(inputs, out var boundedInputs))
    {
      return InvalidRequestAsync<GitHubWorkflowDispatch>(
          "dispatch-request-invalid",
          cancellationToken);
    }

    var path =
        $"{GitHubTransportValidation.RepositoryPath(repository)}/actions/workflows/" +
        $"{workflowId}/dispatches";
    return ExecuteAsync(
        installationId,
        repository.Id,
        token =>
        {
          var request = CreateRequest(HttpMethod.Post, path, token);
          request.Content = JsonContent.Create(
              new GitHubDispatchPayload(reference, boundedInputs),
              GitHubJsonContext.Default.GitHubDispatchPayload);
          return request;
        },
        GitHubJsonContext.Default.GitHubDispatchResultPayload,
        response =>
        {
          if (!GitHubTransportValidation.IsPositiveId(response.WorkflowRunId) ||
              !GitHubTransportValidation.GetHttpsUri(
                  response.RunUrl,
                  out var runApiUrl) ||
              !GitHubTransportValidation.GetHttpsUri(
                  response.HtmlUrl,
                  out var runHtmlUrl))
          {
            return InvalidResponse<GitHubWorkflowDispatch>(
                "dispatch-response-missing-exact-run");
          }
          return Success(
              new GitHubWorkflowDispatch(
                  response.WorkflowRunId,
                  runApiUrl!,
                  runHtmlUrl!));
        },
        cancellationToken);
  }

  public Task<GitHubClientOutcome<GitHubWorkflowRun>> LoadWorkflowRunAsync(
      long installationId,
      GitHubRepositoryIdentity repository,
      long runId,
      CancellationToken cancellationToken)
  {
    if (!_options.Value.Enabled)
    {
      return NotConfiguredAsync<GitHubWorkflowRun>(cancellationToken);
    }
    if (!IsOperationIdentityValid(installationId, repository) ||
        !GitHubTransportValidation.IsPositiveId(runId))
    {
      return InvalidRequestAsync<GitHubWorkflowRun>(
          "workflow-run-request-invalid",
          cancellationToken);
    }

    var path =
        $"{GitHubTransportValidation.RepositoryPath(repository)}/actions/runs/{runId}";
    return ExecuteAsync(
        installationId,
        repository.Id,
        token => CreateRequest(HttpMethod.Get, path, token),
        GitHubJsonContext.Default.GitHubWorkflowRunPayload,
        response => MapWorkflowRun(response, runId),
        cancellationToken);
  }

  public Task<GitHubClientOutcome<GitHubWorkflowArtifactList>>
      ListWorkflowRunArtifactsAsync(
          long installationId,
          GitHubRepositoryIdentity repository,
          long runId,
          int limit,
          CancellationToken cancellationToken)
  {
    if (!_options.Value.Enabled)
    {
      return NotConfiguredAsync<GitHubWorkflowArtifactList>(
          cancellationToken);
    }
    if (!IsOperationIdentityValid(installationId, repository) ||
        !GitHubTransportValidation.IsPositiveId(runId) ||
        limit is <= 0 or > GitHubTransportValidation.MaximumArtifacts)
    {
      return InvalidRequestAsync<GitHubWorkflowArtifactList>(
          "artifact-list-request-invalid",
          cancellationToken);
    }

    var path =
        $"{GitHubTransportValidation.RepositoryPath(repository)}/actions/runs/{runId}" +
        $"/artifacts?per_page={limit}&page=1";
    return ExecuteAsync(
        installationId,
        repository.Id,
        token => CreateRequest(HttpMethod.Get, path, token),
        GitHubJsonContext.Default.GitHubArtifactListPayload,
        response => MapArtifactList(response, runId, limit),
        cancellationToken);
  }

  private async Task<GitHubClientOutcome<TResult>> ExecuteAsync<TExternal, TResult>(
      long installationId,
      long repositoryId,
      Func<string, HttpRequestMessage> requestFactory,
      JsonTypeInfo<TExternal> jsonTypeInfo,
      Func<TExternal, GitHubClientOutcome<TResult>> map,
      CancellationToken cancellationToken)
  {
    var tokenOutcome = await _tokenProvider.CreateAsync(
        installationId,
        repositoryId,
        cancellationToken);
    if (tokenOutcome.Kind != GitHubClientOutcomeKind.Success ||
        string.IsNullOrEmpty(tokenOutcome.Value))
    {
      return new(
          tokenOutcome.Kind,
          default,
          tokenOutcome.RetryAt,
          tokenOutcome.Detail);
    }

    using var request = requestFactory(tokenOutcome.Value);
    try
    {
      using var client = CreateClient();
      using var timeoutSource = new CancellationTokenSource(
          _options.Value.Timeout,
          _timeProvider);
      using var requestSource = CancellationTokenSource.CreateLinkedTokenSource(
          cancellationToken,
          timeoutSource.Token);
      using var response = await client.SendAsync(
          request,
          HttpCompletionOption.ResponseHeadersRead,
          requestSource.Token);
      var externalOutcome =
          await GitHubHttpResponseReader.ReadJsonAsync(
              response,
              jsonTypeInfo,
              GitHubHttpResponseReader.MaximumJsonBytes,
              _timeProvider,
              requestSource.Token);
      if (externalOutcome.Kind != GitHubClientOutcomeKind.Success ||
          externalOutcome.Value is null)
      {
        return new(
            externalOutcome.Kind,
            default,
            externalOutcome.RetryAt,
            externalOutcome.Detail);
      }
      return map(externalOutcome.Value);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return new(GitHubClientOutcomeKind.Cancelled, default, null, "cancelled");
    }
    catch (OperationCanceledException)
    {
      return new(
          GitHubClientOutcomeKind.TimedOut,
          default,
          null,
          "request-timed-out");
    }
    catch (HttpRequestException)
    {
      return new(
          GitHubClientOutcomeKind.TransientFailure,
          default,
          null,
          "transport-failure");
    }
  }

  private HttpClient CreateClient()
  {
    var client = _httpClientFactory.CreateClient(
        GitHubApiHttpClientOptions.ClientName);
    client.BaseAddress = _options.Value.BaseAddress;
    client.Timeout = Timeout.InfiniteTimeSpan;
    client.MaxResponseContentBufferSize =
        GitHubHttpResponseReader.MaximumJsonBytes;
    return client;
  }

  private static HttpRequestMessage CreateRequest(
      HttpMethod method,
      string path,
      string token)
  {
    var request = new HttpRequestMessage(method, path);
    GitHubAppTokenProvider.ApplyPinnedHeaders(request);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return request;
  }

  private static GitHubClientOutcome<GitHubWorkflowRun> MapWorkflowRun(
      GitHubWorkflowRunPayload response,
      long expectedRunId)
  {
    if (response.Id != expectedRunId ||
        !GitHubTransportValidation.IsPositiveId(response.WorkflowId) ||
        !GitHubTransportValidation.IsSha1(response.HeadSha) ||
        !GitHubTransportValidation.IsBoundedText(response.Status, 32) ||
        response.Conclusion is { } conclusion &&
        !GitHubTransportValidation.IsBoundedText(conclusion, 32) ||
        !GitHubTransportValidation.GetHttpsUri(response.Url, out var runApiUrl) ||
        !GitHubTransportValidation.GetHttpsUri(response.HtmlUrl, out var runHtmlUrl) ||
        response.CreatedAt == default ||
        response.UpdatedAt == default)
    {
      return InvalidResponse<GitHubWorkflowRun>(
          "workflow-run-response-invalid");
    }

    return Success(
        new GitHubWorkflowRun(
            response.Id,
            response.WorkflowId,
            response.HeadSha!,
            response.Status!,
            response.Conclusion,
            runApiUrl!,
            runHtmlUrl!,
            response.CreatedAt,
            response.UpdatedAt));
  }

  private static GitHubClientOutcome<GitHubWorkflowArtifactList> MapArtifactList(
      GitHubArtifactListPayload response,
      long expectedRunId,
      int limit)
  {
    if (response.TotalCount < 0 ||
        response.Artifacts is null ||
        response.Artifacts.Count > limit)
    {
      return InvalidResponse<GitHubWorkflowArtifactList>(
          "artifact-list-response-invalid");
    }

    var artifacts = new List<GitHubWorkflowArtifact>(response.Artifacts.Count);
    foreach (var artifact in response.Artifacts)
    {
      if (!GitHubTransportValidation.IsPositiveId(artifact.Id) ||
          artifact.WorkflowRun?.Id != expectedRunId ||
          !GitHubTransportValidation.IsBoundedText(artifact.Name, 256) ||
          artifact.SizeInBytes < 0 ||
          artifact.Digest is { } digest &&
          !GitHubTransportValidation.IsBoundedText(digest, 256) ||
          artifact.ExpiresAt == default ||
          !GitHubTransportValidation.GetHttpsUri(
              artifact.ArchiveDownloadUrl,
              out var archiveUrl))
      {
        return InvalidResponse<GitHubWorkflowArtifactList>(
            "artifact-response-invalid");
      }

      artifacts.Add(
          new GitHubWorkflowArtifact(
              artifact.Id,
              expectedRunId,
              artifact.Name!,
              artifact.SizeInBytes,
              artifact.Digest,
              artifact.Expired,
              artifact.ExpiresAt,
              archiveUrl!));
    }

    return Success(
        new GitHubWorkflowArtifactList(response.TotalCount, artifacts));
  }

  private static GitHubWorkflowState MapWorkflowState(string? value) =>
      value switch
      {
        "active" => GitHubWorkflowState.Active,
        "deleted" => GitHubWorkflowState.Deleted,
        "disabled_fork" => GitHubWorkflowState.DisabledFork,
        "disabled_inactivity" => GitHubWorkflowState.DisabledInactivity,
        "disabled_manually" => GitHubWorkflowState.DisabledManually,
        _ => GitHubWorkflowState.Unknown,
      };

  private static bool IsOperationIdentityValid(
      long installationId,
      GitHubRepositoryIdentity repository) =>
      GitHubTransportValidation.IsPositiveId(installationId) &&
      GitHubTransportValidation.IsRepository(repository);

  private static Task<GitHubClientOutcome<T>> InvalidRequestAsync<T>(
      string detail,
      CancellationToken cancellationToken)
  {
    _ = cancellationToken;
    return
      Task.FromResult(
          new GitHubClientOutcome<T>(
              GitHubClientOutcomeKind.InvalidRequest,
              default,
              null,
              detail));
  }

  private static Task<GitHubClientOutcome<T>> NotConfiguredAsync<T>(
      CancellationToken cancellationToken)
  {
    _ = cancellationToken;
    return Task.FromResult(
        new GitHubClientOutcome<T>(
            GitHubClientOutcomeKind.NotConfigured,
            default,
            null,
            "github-app-disabled"));
  }

  private static GitHubClientOutcome<T> InvalidResponse<T>(string detail) =>
      new(GitHubClientOutcomeKind.InvalidResponse, default, null, detail);

  private static GitHubClientOutcome<T> Success<T>(T value) =>
      new(GitHubClientOutcomeKind.Success, value, null, null);
}
