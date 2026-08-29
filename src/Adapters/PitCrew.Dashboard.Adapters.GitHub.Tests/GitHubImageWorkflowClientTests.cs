using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

public sealed class GitHubImageWorkflowClientTests
{
  private const string CommitSha =
      "0123456789abcdef0123456789abcdef01234567";
  private const string BlobSha =
      "abcdef0123456789abcdef0123456789abcdef01";

  [Test]
  public async Task Exact_Identity_Requests_Are_Constructed_And_Mapped(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");

    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            """
            {
              "id": 99,
              "name": "Build candidate",
              "path": ".github/workflows/image-candidate.yml",
              "state": "active"
            }
            """));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            $$"""
            {
              "type": "file",
              "path": ".github/workflows/image-candidate.yml",
              "sha": "{{BlobSha}}"
            }
            """));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            $$"""
            {
              "type": "file",
              "path": ".github/workflows/image-candidate.yml",
              "sha": "{{BlobSha}}"
            }
            """));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            $$"""{"sha":"{{CommitSha}}"}"""));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse("""{"status":"ahead"}"""));

    var workflow = await context.Client.LoadWorkflowAsync(
        77,
        repository,
        99,
        cancellationToken);
    var file = await context.Client.LoadWorkflowFileRevisionAsync(
        77,
        repository,
        ".github/workflows/image-candidate.yml",
        "release/v1",
        cancellationToken);
    var fileAtCommit =
        await context.Client.LoadWorkflowFileRevisionAtCommitAsync(
            77,
            repository,
            ".github/workflows/image-candidate.yml",
            CommitSha,
            cancellationToken);
    var commit = await context.Client.ResolveCommitAsync(
        77,
        repository,
        CommitSha,
        cancellationToken);
    var reachability = await context.Client.VerifyCommitReachableAsync(
        77,
        repository,
        CommitSha,
        "release/stable",
        cancellationToken);

    await Assert.That(workflow.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(workflow.Value!.Id).IsEqualTo(99);
    await Assert.That(workflow.Value.State)
        .IsEqualTo(GitHubWorkflowState.Active);
    await Assert.That(file.Kind).IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(file.Value!.BlobSha).IsEqualTo(BlobSha);
    await Assert.That(fileAtCommit.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(fileAtCommit.Value!.Reference)
        .IsEqualTo(CommitSha);
    await Assert.That(fileAtCommit.Value.BlobSha).IsEqualTo(BlobSha);
    await Assert.That(commit.Value!.Sha).IsEqualTo(CommitSha);
    await Assert.That(reachability.Value!.IsReachable).IsTrue()
        .Because("an ahead allowed ref contains the exact source commit");

    var operationRequests = context.Handler.Requests
        .Where(static request =>
            request.Uri.AbsolutePath !=
            "/app/installations/77/access_tokens")
        .ToArray();
    await Assert.That(operationRequests).Count().IsEqualTo(5);
    await Assert.That(operationRequests[0].Uri.PathAndQuery)
        .IsEqualTo(
            "/repos/nexus-labs/pitcrew/actions/workflows/99");
    await Assert.That(operationRequests[1].Uri.PathAndQuery)
        .IsEqualTo(
            "/repos/nexus-labs/pitcrew/contents/.github/workflows/" +
            "image-candidate.yml?ref=release%2Fv1");
    await Assert.That(operationRequests[2].Uri.PathAndQuery)
        .IsEqualTo(
            "/repos/nexus-labs/pitcrew/contents/.github/workflows/" +
            $"image-candidate.yml?ref={CommitSha}");
    await Assert.That(operationRequests[3].Uri.AbsolutePath)
        .IsEqualTo(
            $"/repos/nexus-labs/pitcrew/commits/{CommitSha}");
    await Assert.That(operationRequests[4].Uri.AbsolutePath)
        .IsEqualTo(
            $"/repos/nexus-labs/pitcrew/compare/{CommitSha}...release%2Fstable");
  }

  [Test]
  public async Task Invalid_Identifiers_Paths_Refs_And_Inputs_Fail_Before_Http(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    var oversizedInputs = Enumerable.Range(0, 26)
        .ToDictionary(
            static index => $"key-{index}",
            static _ => "value",
            StringComparer.Ordinal);
    var controlInputs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["valid"] = "bad\rvalue",
    };

    var badRepository = await context.Client.LoadRepositoryAsync(
        1,
        0,
        cancellationToken);
    var traversal = await context.Client.LoadWorkflowFileRevisionAsync(
        1,
        repository,
        ".github/workflows/../secret.yml",
        "main",
        cancellationToken);
    var controlPath = await context.Client.LoadWorkflowFileRevisionAsync(
        1,
        repository,
        ".github/workflows/bad\n.yml",
        "main",
        cancellationToken);
    var malformedRevision =
        await context.Client.LoadWorkflowFileRevisionAtCommitAsync(
            1,
            repository,
            ".github/workflows/image-candidate.yml",
            "not-a-sha",
            cancellationToken);
    var badSha = await context.Client.ResolveCommitAsync(
        1,
        repository,
        "not-a-sha",
        cancellationToken);
    var badRef = await context.Client.VerifyCommitReachableAsync(
        1,
        repository,
        CommitSha,
        "../main",
        cancellationToken);
    var badWorkflowId = await context.Client.DispatchWorkflowAsync(
        1,
        repository,
        -1,
        "main",
        ReadOnlyDictionary<string, string>.Empty,
        cancellationToken);
    var tooManyInputs = await context.Client.DispatchWorkflowAsync(
        1,
        repository,
        99,
        "main",
        oversizedInputs,
        cancellationToken);
    var badInput = await context.Client.DispatchWorkflowAsync(
        1,
        repository,
        99,
        "main",
        controlInputs,
        cancellationToken);
    var oversizedRef = await context.Client.DispatchWorkflowAsync(
        1,
        repository,
        99,
        new string('a', 256),
        ReadOnlyDictionary<string, string>.Empty,
        cancellationToken);
    var commitDispatch = await context.Client.DispatchWorkflowAsync(
        1,
        repository,
        99,
        CommitSha,
        ReadOnlyDictionary<string, string>.Empty,
        cancellationToken);
    var uppercaseCommitDispatch =
        await context.Client.DispatchWorkflowAsync(
            1,
            repository,
            99,
            CommitSha.ToUpperInvariant(),
            ReadOnlyDictionary<string, string>.Empty,
            cancellationToken);

    var outcomes = new[]
    {
      badRepository.Kind,
      traversal.Kind,
      controlPath.Kind,
      malformedRevision.Kind,
      badSha.Kind,
      badRef.Kind,
      badWorkflowId.Kind,
      tooManyInputs.Kind,
      badInput.Kind,
      oversizedRef.Kind,
      commitDispatch.Kind,
      uppercaseCommitDispatch.Kind,
    };
    await Assert.That(
            outcomes.All(static outcome =>
                outcome == GitHubClientOutcomeKind.InvalidRequest))
        .IsTrue()
        .Because("every rejected value must fail before HTTP");
    await Assert.That(context.Handler.Requests).IsEmpty();
  }

  [Test]
  public async Task Exact_Commit_Workflow_Revision_Rejects_Malformed_Response(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            """
            {
              "type": "file",
              "path": ".github/workflows/image-candidate.yml",
              "sha": "not-a-sha"
            }
            """));

    var outcome =
        await context.Client.LoadWorkflowFileRevisionAtCommitAsync(
            77,
            repository,
            ".github/workflows/image-candidate.yml",
            CommitSha,
            cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
  }

  [Test]
  public async Task Dispatch_Uses_Exact_Response_And_Rejects_Old_Shapes(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            """
            {
              "workflow_run_id": 987,
              "run_url": "https://api.github.com/repos/nexus-labs/pitcrew/actions/runs/987",
              "html_url": "https://github.com/nexus-labs/pitcrew/actions/runs/987"
            }
            """));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            """
            {
              "id": 988,
              "url": "https://api.github.com/repos/nexus-labs/pitcrew/actions/runs/988",
              "html_url": "https://github.com/nexus-labs/pitcrew/actions/runs/988"
            }
            """));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            """
            {
              "run_url": "https://api.github.com/repos/nexus-labs/pitcrew/actions/runs/989",
              "html_url": "https://github.com/nexus-labs/pitcrew/actions/runs/989"
            }
            """));
    var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["pitcrew_request_id"] = "request-1",
      ["source_commit"] = CommitSha,
    };

    var success = await context.Client.DispatchWorkflowAsync(
        77,
        repository,
        99,
        "release/v1",
        inputs,
        cancellationToken);
    var oldShape = await context.Client.DispatchWorkflowAsync(
        77,
        repository,
        99,
        "release/v1",
        inputs,
        cancellationToken);
    var missingId = await context.Client.DispatchWorkflowAsync(
        77,
        repository,
        99,
        "release/v1",
        inputs,
        cancellationToken);

    await Assert.That(success.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(success.Value!.RunId).IsEqualTo(987);
    await Assert.That(success.Value.RunApiUrl.AbsoluteUri)
        .IsEqualTo(
            "https://api.github.com/repos/nexus-labs/pitcrew/actions/runs/987");
    await Assert.That(success.Value.RunHtmlUrl.AbsoluteUri)
        .IsEqualTo(
            "https://github.com/nexus-labs/pitcrew/actions/runs/987");
    await Assert.That(oldShape.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(missingId.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);

    var dispatch = context.Handler.Requests[1];
    await Assert.That(dispatch.Method).IsEqualTo(HttpMethod.Post);
    await Assert.That(dispatch.Uri.AbsolutePath)
        .IsEqualTo(
            "/repos/nexus-labs/pitcrew/actions/workflows/99/dispatches");
    await Assert.That(dispatch.Body).Contains("\"ref\":\"release/v1\"");
    await Assert.That(dispatch.Body)
        .Contains($"\"source_commit\":\"{CommitSha}\"");
  }

  [Test]
  public async Task Workflow_Content_Is_Decoded_And_Bound_To_Exact_Revision(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    var revision = new GitHubWorkflowFileRevision(
        ".github/workflows/image-candidate.yml",
        BlobSha,
        "release/v1");
    var yaml =
        """
        on:
          workflow_dispatch:
            inputs:
              pitcrew_request_id:
                type: string
                required: true
        """;

    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            CreateWorkflowContentJson(
                revision.Path,
                revision.BlobSha,
                Encoding.UTF8.GetBytes(yaml))));

    var content = await context.Client.LoadWorkflowFileContentAsync(
        77,
        repository,
        revision,
        cancellationToken);

    await Assert.That(content.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(content.Value).IsNotNull();
    await Assert.That(content.Value!.Path).IsEqualTo(revision.Path);
    await Assert.That(content.Value.BlobSha).IsEqualTo(revision.BlobSha);
    await Assert.That(content.Value.Reference)
        .IsEqualTo(revision.Reference);
    await Assert.That(content.Value.Content).IsEqualTo(yaml);
    await Assert.That(context.Handler.Requests[1].Uri.PathAndQuery)
        .IsEqualTo(
            "/repos/nexus-labs/pitcrew/contents/.github/workflows/" +
            "image-candidate.yml?ref=release%2Fv1");
  }

  [Test]
  public async Task Workflow_Content_Rejects_Invalid_Encoding_And_Identity(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    var revision = new GitHubWorkflowFileRevision(
        ".github/workflows/image-candidate.yml",
        BlobSha,
        "release/v1");

    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            """
            {
              "type": "file",
              "path": ".github/workflows/image-candidate.yml",
              "sha": "abcdef0123456789abcdef0123456789abcdef01",
              "size": 12,
              "encoding": "utf-16",
              "content": "dGVzdA=="
            }
            """));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            CreateWorkflowContentJson(
                ".github/workflows/other.yml",
                revision.BlobSha,
                Encoding.UTF8.GetBytes("name: build"))));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            CreateWorkflowContentJson(
                revision.Path,
                new string('b', 40),
                Encoding.UTF8.GetBytes("name: build"))));

    var invalidEncoding = await context.Client.LoadWorkflowFileContentAsync(
        77,
        repository,
        revision,
        cancellationToken);
    var wrongPath = await context.Client.LoadWorkflowFileContentAsync(
        77,
        repository,
        revision,
        cancellationToken);
    var wrongBlob = await context.Client.LoadWorkflowFileContentAsync(
        77,
        repository,
        revision,
        cancellationToken);

    await Assert.That(invalidEncoding.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(wrongPath.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(wrongBlob.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
  }

  [Test]
  public async Task Workflow_Content_Rejects_Oversized_And_Invalid_Utf8(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    var revision = new GitHubWorkflowFileRevision(
        ".github/workflows/image-candidate.yml",
        BlobSha,
        "release/v1");
    var invalidUtf8 = new byte[]
    {
      0xC3,
      0x28,
    };

    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            CreateWorkflowContentJson(
                revision.Path,
                revision.BlobSha,
                Encoding.UTF8.GetBytes("name: build"),
                size: 65_537)));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            CreateWorkflowContentJson(
                revision.Path,
                revision.BlobSha,
                invalidUtf8)));

    var oversized = await context.Client.LoadWorkflowFileContentAsync(
        77,
        repository,
        revision,
        cancellationToken);
    var malformedUtf8 = await context.Client.LoadWorkflowFileContentAsync(
        77,
        repository,
        revision,
        cancellationToken);

    await Assert.That(oversized.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(malformedUtf8.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
  }

  [Test]
  public async Task Exact_Run_And_Bounded_Artifacts_Are_Mapped(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            $$"""
            {
              "id": 987,
              "workflow_id": 99,
              "head_sha": "{{CommitSha}}",
              "status": "completed",
              "conclusion": "success",
              "event": "workflow_dispatch",
              "url": "https://api.github.com/repos/nexus-labs/pitcrew/actions/runs/987",
              "html_url": "https://github.com/nexus-labs/pitcrew/actions/runs/987",
              "created_at": "2026-08-23T16:01:00Z",
              "updated_at": "2026-08-23T16:02:00Z"
            }
            """));
    context.EnqueueToken();
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            """
            {
              "total_count": 1,
              "artifacts": [
                {
                  "id": 555,
                  "name": "pitcrew-image-candidate",
                  "size_in_bytes": 1234,
                  "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "expired": false,
                  "expires_at": "2026-09-01T00:00:00Z",
                  "archive_download_url": "https://api.github.com/repos/nexus-labs/pitcrew/actions/artifacts/555/zip",
                  "workflow_run": { "id": 987 }
                }
              ]
            }
            """));

    var run = await context.Client.LoadWorkflowRunAsync(
        77,
        repository,
        987,
        cancellationToken);
    var artifacts = await context.Client.ListWorkflowRunArtifactsAsync(
        77,
        repository,
        987,
        10,
        cancellationToken);

    await Assert.That(run.Kind).IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(run.Value!.Id).IsEqualTo(987);
    await Assert.That(run.Value.WorkflowId).IsEqualTo(99);
    await Assert.That(run.Value.HeadSha).IsEqualTo(CommitSha);
    await Assert.That(run.Value.Event).IsEqualTo("workflow_dispatch");
    await Assert.That(artifacts.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(artifacts.Value!.Artifacts).Count().IsEqualTo(1);
    await Assert.That(artifacts.Value.Artifacts[0].WorkflowRunId)
        .IsEqualTo(987);
    await Assert.That(artifacts.Value.Artifacts[0].Name)
        .IsEqualTo("pitcrew-image-candidate");
    await Assert.That(context.Handler.Requests[3].Uri.PathAndQuery)
        .IsEqualTo(
            "/repos/nexus-labs/pitcrew/actions/runs/987/artifacts?per_page=10&page=1");
  }

  [Test]
  public async Task Exact_Artifact_Archive_Follows_One_Allowed_Redirect_Without_Token(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    var redirect = new Uri(
        "https://productionresultssa0.blob.core.windows.net/actions-results/candidate.zip?sig=bounded",
        UriKind.Absolute);
    var expected = Encoding.UTF8.GetBytes("bounded-archive");
    var artifact = CreateArtifact(expected.Length);
    context.EnqueueToken();
    context.Handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Found)
    {
      Headers =
      {
        Location = redirect,
      },
    });
    var archiveResponse = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ByteArrayContent(expected),
    };
    archiveResponse.Content.Headers.ContentType =
        new MediaTypeHeaderValue("application/zip");
    context.Handler.Enqueue(archiveResponse);

    var outcome = await context.Client.DownloadWorkflowArtifactArchiveAsync(
        77,
        repository,
        artifact,
        1024,
        cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(outcome.Value!.ArtifactId).IsEqualTo(555);
    await Assert.That(outcome.Value.Content.ToArray())
        .IsEquivalentTo(expected);
    await Assert.That(context.Handler.Requests).Count().IsEqualTo(3);
    await Assert.That(context.Handler.Requests[1].Headers["Authorization"])
        .IsEqualTo("Bearer installation-token");
    await Assert.That(
            context.Handler.Requests[2].Headers.ContainsKey("Authorization"))
        .IsFalse()
        .Because("signed archive redirects must not receive installation tokens");
    await Assert.That(context.Handler.Requests[2].Uri)
        .IsEqualTo(redirect);
  }

  [Test]
  public async Task Artifact_Archive_Rejects_Unexpected_Redirect_Authority(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    var artifact = CreateArtifact(64);
    context.EnqueueToken();
    context.Handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Found)
    {
      Headers =
      {
        Location = new Uri(
            "https://downloads.example.com/candidate.zip",
            UriKind.Absolute),
      },
    });

    var outcome = await context.Client.DownloadWorkflowArtifactArchiveAsync(
        77,
        repository,
        artifact,
        1024,
        cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(outcome.Detail)
        .IsEqualTo("artifact-download-redirect-invalid");
    await Assert.That(context.Handler.Requests).Count().IsEqualTo(2);
  }

  [Test]
  public async Task Artifact_Archive_Rejects_Response_That_Exceeds_Bound(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");
    var artifact = CreateArtifact(10);
    context.EnqueueToken();
    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ByteArrayContent(new byte[11]),
    };
    response.Content.Headers.ContentType =
        new MediaTypeHeaderValue("application/zip");
    context.Handler.Enqueue(response);

    var outcome = await context.Client.DownloadWorkflowArtifactArchiveAsync(
        77,
        repository,
        artifact,
        10,
        cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(outcome.Detail)
        .IsEqualTo("response-body-oversized");
  }

  private static GitHubWorkflowArtifact CreateArtifact(long sizeInBytes) =>
      new(
          555,
          987,
          "pitcrew-image-candidate",
          sizeInBytes,
          "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          false,
          GitHubAdapterTestContext.FixedNow.AddHours(1),
          new Uri(
              "https://api.github.com/repos/nexus-labs/pitcrew/actions/artifacts/555/zip",
              UriKind.Absolute));

  private static string CreateWorkflowContentJson(
      string path,
      string sha,
      byte[] content,
      string encoding = "base64",
      long? size = null)
  {
    var base64 = Convert.ToBase64String(content);
    var wrapped = WrapBase64(base64);
    var escapedContent = wrapped
        .Replace(
            "\\",
            "\\\\",
            StringComparison.Ordinal)
        .Replace(
            "\"",
            "\\\"",
            StringComparison.Ordinal)
        .Replace(
            "\r",
            "\\r",
            StringComparison.Ordinal)
        .Replace(
            "\n",
            "\\n",
            StringComparison.Ordinal);
    return
        $$"""
          {
            "type": "file",
            "path": "{{path}}",
            "sha": "{{sha}}",
            "size": {{size ?? content.Length}},
            "encoding": "{{encoding}}",
            "content": "{{escapedContent}}"
          }
          """;
  }

  private static string WrapBase64(string value)
  {
    var builder = new StringBuilder(value.Length + (value.Length / 60));
    for (var offset = 0; offset < value.Length; offset += 60)
    {
      if (offset > 0)
      {
        builder.Append('\n');
      }

      builder.Append(
          value,
          offset,
          Math.Min(
              60,
              value.Length - offset));
    }

    return builder.ToString();
  }
}
