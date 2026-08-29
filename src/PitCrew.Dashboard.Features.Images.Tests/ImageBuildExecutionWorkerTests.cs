using System.Security.Cryptography;
using System.Text;
using System.Globalization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

using Moq;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images.Tests;

[NotInParallel]
public sealed class ImageBuildExecutionWorkerTests
{
  [Test]
  public async Task Worker_Dispatches_And_Persists_Ready_Exact_Run_Candidate(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath("dispatch-poll");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        2,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var requestId = Guid.Parse(
          "88888888-8888-8888-8888-888888888888",
          CultureInfo.InvariantCulture);
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew");
      var runApiUrl = new Uri(
          "https://api.github.example/runs/7001",
          UriKind.Absolute);
      var runHtmlUrl = new Uri(
          "https://github.example/runs/7001",
          UriKind.Absolute);
      SetupDispatchAuthority(clientMock, repository);
      clientMock.Setup(client => client.DispatchWorkflowAsync(
              1001,
              repository,
              3001,
              "release/v1",
              It.Is<IReadOnlyDictionary<string, string>>(inputs =>
                  inputs.Count == 4
                  && inputs["channel"] == "stable"
                  && inputs["pitcrew_request_id"] == requestId.ToString("D")
                  && inputs["pitcrew_source_commit"] == new string('b', 40)
                  && inputs["pitcrew_recipe_id"] == "pitcrew-default"),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowDispatch>(
              GitHubClientOutcomeKind.Success,
              new GitHubWorkflowDispatch(
                  7001,
                  runApiUrl,
                  runHtmlUrl),
              null,
              null))
          .Verifiable();
      clientMock.Setup(client => client.LoadWorkflowRunAsync(
              1001,
              repository,
              7001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowRun>(
              GitHubClientOutcomeKind.Success,
              new GitHubWorkflowRun(
                  7001,
                  3001,
                  new string('c', 40),
                  "completed",
                  "success",
                  runApiUrl,
                  runHtmlUrl,
                  now,
                  now.AddMinutes(1),
                  "workflow_dispatch"),
              null,
              null))
          .Verifiable();
      SetupRunRevision(
          clientMock,
          repository,
          new string('c', 40),
          new string('a', 40));
      var archive = ImageCandidateArchiveTestData.CreateArchive(
          ImageCandidateArchiveTestData.CreateReadyReport());
      var artifact = ImageCandidateArchiveTestData.CreateArtifact(
          archive,
          now);
      clientMock.Setup(client => client.ListWorkflowRunArtifactsAsync(
              1001,
              repository,
              7001,
              100,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowArtifactList>(
              GitHubClientOutcomeKind.Success,
              new GitHubWorkflowArtifactList(1, [artifact]),
              null,
              null))
          .Verifiable();
      clientMock.Setup(client => client.DownloadWorkflowArtifactArchiveAsync(
              1001,
              repository,
              artifact,
              262_144,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new GitHubClientOutcome<GitHubWorkflowArtifactArchive>(
                  GitHubClientOutcomeKind.Success,
                  new GitHubWorkflowArtifactArchive(
                      artifact.Id,
                      archive),
                  null,
                  null))
          .Verifiable();

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var worker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);

      var dispatched = await worker.ProcessOnceAsync(cancellationToken);
      var building = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      fakeTime.Advance(TimeSpan.FromSeconds(15));
      var polled = await worker.ProcessOnceAsync(cancellationToken);
      var qualifying = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      var qualified = await worker.ProcessOnceAsync(cancellationToken);
      var ready = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      var candidate = await store.GetCandidateOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(dispatched).IsEqualTo(1);
      await Assert.That(building!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Building);
      await Assert.That(building.GitHubRunId).IsEqualTo(7001);
      await Assert.That(building.GitHubRunApiUrl)
          .IsEqualTo(runApiUrl.AbsoluteUri);
      await Assert.That(building.GitHubRunUrl)
          .IsEqualTo(runHtmlUrl.AbsoluteUri);
      await Assert.That(polled).IsEqualTo(1);
      await Assert.That(qualifying!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Qualifying);
      await Assert.That(qualified).IsEqualTo(1);
      await Assert.That(ready!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Ready);
      await Assert.That(candidate).IsNotNull();
      await Assert.That(candidate!.Candidate)
          .IsTypeOf<ReadyImageCandidate>();
      await Assert.That(candidate.Qualifications).Count().IsEqualTo(4);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Qualifying_Transient_GitHub_Failure_Remains_Retryable(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath("qualifying-retry");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        2,
        30,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var requestId = Guid.NewGuid();
      var repository = Repository();
      clientMock.Setup(client => client.ListWorkflowRunArtifactsAsync(
              1001,
              repository,
              7001,
              100,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowArtifactList>(
              GitHubClientOutcomeKind.TransientFailure,
              null,
              null,
              "never-log-this-detail"))
          .Verifiable();

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var worker = await GetStoppedWorkerAsync(factory, cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);
      await AdvanceToQualifyingAsync(
          store,
          requestId,
          now,
          cancellationToken);

      var processed = await worker.ProcessOnceAsync(cancellationToken);
      var immediateReplay = await worker.ProcessOnceAsync(cancellationToken);
      var request = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      var candidate = await store.GetCandidateOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(processed).IsEqualTo(1);
      await Assert.That(immediateReplay).IsEqualTo(0);
      await Assert.That(request!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Qualifying);
      await Assert.That(candidate).IsNull();
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Qualifying_Failed_Report_Persists_Failed_Candidate(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath("qualifying-failed");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        2,
        45,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var requestId = Guid.NewGuid();
      var repository = Repository();
      var archive = ImageCandidateArchiveTestData.CreateArchive(
          ImageCandidateArchiveTestData.CreateFailedReport());
      var artifact = ImageCandidateArchiveTestData.CreateArtifact(
          archive,
          now);
      clientMock.Setup(client => client.ListWorkflowRunArtifactsAsync(
              1001,
              repository,
              7001,
              100,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowArtifactList>(
              GitHubClientOutcomeKind.Success,
              new GitHubWorkflowArtifactList(1, [artifact]),
              null,
              null))
          .Verifiable();
      clientMock.Setup(client => client.DownloadWorkflowArtifactArchiveAsync(
              1001,
              repository,
              artifact,
              262_144,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new GitHubClientOutcome<GitHubWorkflowArtifactArchive>(
                  GitHubClientOutcomeKind.Success,
                  new GitHubWorkflowArtifactArchive(
                      artifact.Id,
                      archive),
                  null,
                  null))
          .Verifiable();

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var worker = await GetStoppedWorkerAsync(factory, cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);
      await AdvanceToQualifyingAsync(
          store,
          requestId,
          now,
          cancellationToken);

      var processed = await worker.ProcessOnceAsync(cancellationToken);
      var request = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      var candidate = await store.GetCandidateOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(processed).IsEqualTo(1);
      await Assert.That(request!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Failed);
      await Assert.That(request.TerminalCategory)
          .IsEqualTo("build-failed");
      await Assert.That(candidate).IsNotNull();
      await Assert.That(candidate!.Candidate)
          .IsTypeOf<FailedImageCandidate>();
      await Assert.That(candidate.Qualifications).Count().IsEqualTo(4);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Ambiguous_Dispatch_Timeout_Blocks_And_Never_Redispatches(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath("dispatch-timeout");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        3,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var requestId = Guid.Parse(
          "99999999-9999-9999-9999-999999999999",
          CultureInfo.InvariantCulture);
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew");
      SetupDispatchAuthority(clientMock, repository);
      clientMock.Setup(client => client.DispatchWorkflowAsync(
              1001,
              repository,
              3001,
              "release/v1",
              It.IsAny<IReadOnlyDictionary<string, string>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowDispatch>(
              GitHubClientOutcomeKind.TimedOut,
              null,
              null,
              "request-timed-out"))
          .Verifiable();

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var worker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);

      var processed = await worker.ProcessOnceAsync(cancellationToken);
      fakeTime.Advance(TimeSpan.FromHours(1));
      var replayed = await worker.ProcessOnceAsync(cancellationToken);
      var blocked = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(processed).IsEqualTo(1);
      await Assert.That(replayed).IsEqualTo(0);
      await Assert.That(blocked!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Blocked);
      await Assert.That(blocked.TerminalCategory)
          .IsEqualTo("dispatch-indeterminate");
      mocks.VerifyAll();
      clientMock.Verify(client => client.DispatchWorkflowAsync(
              1001,
              repository,
              3001,
              "release/v1",
              It.IsAny<IReadOnlyDictionary<string, string>>(),
              It.IsAny<CancellationToken>()),
          Times.Once());
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Restart_Finding_Unsafe_Dispatching_Request_Blocks_Without_GitHub(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath("dispatch-crash");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        4,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var requestId = Guid.Parse(
          "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          CultureInfo.InvariantCulture);
      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var worker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);
      var crashedClaim = (await store.ClaimDueBuildRequestsAsync(
          "crashed-worker",
          now,
          now.AddSeconds(30),
          1,
          cancellationToken)).Single();
      await store.MarkDispatchStartedAsync(
          "tenant-a",
          requestId,
          crashedClaim.LeaseOwner,
          now,
          cancellationToken);

      fakeTime.Advance(TimeSpan.FromSeconds(30));
      var processed = await worker.ProcessOnceAsync(cancellationToken);
      var blocked = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(processed).IsEqualTo(1);
      await Assert.That(blocked!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Blocked);
      await Assert.That(blocked.TerminalCategory)
          .IsEqualTo("dispatch-indeterminate");
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  [Arguments("neutral")]
  [Arguments("skipped")]
  public async Task Completed_NonSuccess_Conclusion_Fails_Without_Candidate(
      string conclusion,
      CancellationToken cancellationToken)
  {
    var request = await RunCompletedObservationAsync(
        $"conclusion-{conclusion}",
        conclusion,
        new string('c', 40),
        new string('a', 40),
        cancellationToken);

    await Assert.That(request.Status)
        .IsEqualTo(ImageBuildRequestStatus.Failed);
    await Assert.That(request.TerminalCategory)
        .IsEqualTo($"workflow-{conclusion}");
  }

  [Test]
  public async Task Completed_Run_With_Different_Workflow_Blob_Blocks(
      CancellationToken cancellationToken)
  {
    var request = await RunCompletedObservationAsync(
        "revision-mismatch",
        "success",
        new string('c', 40),
        new string('d', 40),
        cancellationToken);

    await Assert.That(request.Status)
        .IsEqualTo(ImageBuildRequestStatus.Blocked);
    await Assert.That(request.TerminalCategory)
        .IsEqualTo("run-revision-mismatch");
  }

  [Test]
  public async Task Completed_Run_With_Malformed_Head_Sha_Blocks(
      CancellationToken cancellationToken)
  {
    var request = await RunCompletedObservationAsync(
        "revision-malformed",
        "success",
        "not-a-canonical-sha",
        revisionBlobSha: null,
        cancellationToken);

    await Assert.That(request.Status)
        .IsEqualTo(ImageBuildRequestStatus.Blocked);
    await Assert.That(request.TerminalCategory)
        .IsEqualTo("run-revision-invalid");
  }

  [Test]
  public async Task Completed_Run_With_Invalid_Revision_Response_Blocks(
      CancellationToken cancellationToken)
  {
    var request = await RunCompletedObservationAsync(
        "revision-invalid-response",
        "success",
        new string('c', 40),
        revisionBlobSha: null,
        cancellationToken,
        GitHubClientOutcomeKind.InvalidResponse);

    await Assert.That(request.Status)
        .IsEqualTo(ImageBuildRequestStatus.Blocked);
    await Assert.That(request.TerminalCategory)
        .IsEqualTo("run-revision-invalid");
  }

  [Test]
  public async Task Run_And_Revision_NotFound_Budgets_Are_Independent_Across_Restart(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "independent-not-found");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        5,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = Repository();
      var requestId = Guid.NewGuid();
      var runApiUrl = RunApiUrl();
      var runHtmlUrl = RunHtmlUrl();
      SetupDispatchAuthority(clientMock, repository);
      SetupSuccessfulDispatch(
          clientMock,
          repository,
          requestId,
          runApiUrl,
          runHtmlUrl);
      var runNotFound = new GitHubClientOutcome<GitHubWorkflowRun>(
          GitHubClientOutcomeKind.NotFound,
          null,
          null,
          "not-found");
      var completedRun = new GitHubClientOutcome<GitHubWorkflowRun>(
          GitHubClientOutcomeKind.Success,
          new GitHubWorkflowRun(
              7001,
              3001,
              new string('c', 40),
              "completed",
              "success",
              runApiUrl,
              runHtmlUrl,
              now,
              now.AddMinutes(1),
              "workflow_dispatch"),
          null,
          null);
      clientMock.SetupSequence(client => client.LoadWorkflowRunAsync(
              1001,
              repository,
              7001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(runNotFound)
          .ReturnsAsync(runNotFound)
          .ReturnsAsync(runNotFound)
          .ReturnsAsync(completedRun)
          .ReturnsAsync(completedRun);
      clientMock.SetupSequence(client =>
              client.LoadWorkflowFileRevisionAtCommitAsync(
                  1001,
                  repository,
                  ".github/workflows/image-candidate.yml",
                  new string('c', 40),
                  It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new GitHubClientOutcome<GitHubWorkflowFileRevision>(
                  GitHubClientOutcomeKind.NotFound,
                  null,
                  null,
                  "not-found"))
          .ReturnsAsync(RevisionOutcome(
              new string('c', 40),
              new string('a', 40)));
      clientMock.Setup(client => client.ListWorkflowRunArtifactsAsync(
              1001,
              repository,
              7001,
              100,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowArtifactList>(
              GitHubClientOutcomeKind.TransientFailure,
              null,
              null,
              "transient"));

      await using (var firstFactory = CreateFactory(
          fakeTime,
          clientMock.Object))
      {
        using var client = firstFactory.CreateClient();
        var worker = await GetStoppedWorkerAsync(
            firstFactory,
            cancellationToken);
        var store =
            firstFactory.Services.GetRequiredService<IImageCandidateStore>();
        await SeedAsync(
            firstFactory.Services,
            store,
            requestId,
            now,
            cancellationToken);
        await worker.ProcessOnceAsync(cancellationToken);
        for (var attempt = 0; attempt < 3; attempt++)
        {
          fakeTime.Advance(TimeSpan.FromSeconds(301));
          await worker.ProcessOnceAsync(cancellationToken);
        }
        fakeTime.Advance(TimeSpan.FromSeconds(301));
        await worker.ProcessOnceAsync(cancellationToken);
        var awaitingRevision = await store.GetBuildRequestOrNullAsync(
            "tenant-a",
            requestId,
            cancellationToken);
        await Assert.That(awaitingRevision!.Status)
            .IsEqualTo(ImageBuildRequestStatus.Building);
      }

      fakeTime.Advance(TimeSpan.FromSeconds(301));
      await using var restartedFactory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var restartedClient = restartedFactory.CreateClient();
      var restartedWorker = await GetStoppedWorkerAsync(
          restartedFactory,
          cancellationToken);
      var restartedStore =
          restartedFactory.Services.GetRequiredService<IImageCandidateStore>();
      await restartedWorker.ProcessOnceAsync(cancellationToken);
      var qualifying = await restartedStore.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(qualifying!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Qualifying);
      clientMock.Verify(client => client.LoadWorkflowRunAsync(
              1001,
              repository,
              7001,
              It.IsAny<CancellationToken>()),
          Times.Exactly(5));
      clientMock.Verify(client =>
              client.LoadWorkflowFileRevisionAtCommitAsync(
                  1001,
                  repository,
                  ".github/workflows/image-candidate.yml",
                  new string('c', 40),
                  It.IsAny<CancellationToken>()),
          Times.Exactly(2));
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Crash_After_Revision_Success_Persists_Reset_Before_Restart(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "revision-success-crash");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        5,
        15,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = Repository();
      var requestId = Guid.NewGuid();
      var runApiUrl = RunApiUrl();
      var runHtmlUrl = RunHtmlUrl();
      SetupDispatchAuthority(clientMock, repository);
      SetupSuccessfulDispatch(
          clientMock,
          repository,
          requestId,
          runApiUrl,
          runHtmlUrl);
      SetupCompletedRun(
          clientMock,
          repository,
          runApiUrl,
          runHtmlUrl,
          new string('c', 40),
          "success",
          now);
      clientMock.Setup(client =>
              client.LoadWorkflowFileRevisionAtCommitAsync(
                  1001,
                  repository,
                  ".github/workflows/image-candidate.yml",
                  new string('c', 40),
                  It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new GitHubClientOutcome<GitHubWorkflowFileRevision>(
                  GitHubClientOutcomeKind.NotFound,
                  null,
                  null,
                  "not-found"));

      await using (var firstFactory = CreateFactory(
          fakeTime,
          clientMock.Object))
      {
        using var client = firstFactory.CreateClient();
        var worker = await GetStoppedWorkerAsync(
            firstFactory,
            cancellationToken);
        var store =
            firstFactory.Services.GetRequiredService<IImageCandidateStore>();
        await SeedAsync(
            firstFactory.Services,
            store,
            requestId,
            now,
            cancellationToken);
        await worker.ProcessOnceAsync(cancellationToken);
        for (var attempt = 0; attempt < 3; attempt++)
        {
          fakeTime.Advance(TimeSpan.FromSeconds(301));
          await worker.ProcessOnceAsync(cancellationToken);
        }

        fakeTime.Advance(TimeSpan.FromSeconds(301));
        var crashedClaim =
            (await store.ClaimDueBuildRequestsAsync(
                "revision-success-worker",
                fakeTime.GetUtcNow(),
                fakeTime.GetUtcNow().AddSeconds(300),
                1,
                cancellationToken)).Single();
        await Assert.That(crashedClaim.RevisionNotFoundAttempts)
            .IsEqualTo(3);
        var runObserved = await store.MarkBuildRunObservedAsync(
            "tenant-a",
            requestId,
            crashedClaim.LeaseOwner,
            fakeTime.GetUtcNow(),
            cancellationToken);
        var revisionObserved =
            await store.MarkBuildRevisionObservedAsync(
                "tenant-a",
                requestId,
                crashedClaim.LeaseOwner,
                fakeTime.GetUtcNow(),
                cancellationToken);
        var reclaimBeforeExpiry =
            await store.ClaimDueBuildRequestsAsync(
                "premature-worker",
                fakeTime.GetUtcNow(),
                fakeTime.GetUtcNow().AddSeconds(300),
                1,
                cancellationToken);

        await Assert.That(runObserved)
            .IsEqualTo(ImageCandidateMutationResult.Succeeded);
        await Assert.That(revisionObserved)
            .IsEqualTo(ImageCandidateMutationResult.Succeeded);
        await Assert.That(reclaimBeforeExpiry).IsEmpty()
            .Because("revision success must retain the current lease");
      }

      fakeTime.Advance(TimeSpan.FromSeconds(301));
      await using var restartedFactory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var restartedClient = restartedFactory.CreateClient();
      var restartedWorker = await GetStoppedWorkerAsync(
          restartedFactory,
          cancellationToken);
      var restartedStore =
          restartedFactory.Services.GetRequiredService<IImageCandidateStore>();
      await restartedWorker.ProcessOnceAsync(cancellationToken);
      var request = await restartedStore.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      fakeTime.Advance(TimeSpan.FromSeconds(30));
      var retryClaim =
          (await restartedStore.ClaimDueBuildRequestsAsync(
              "inspection-worker",
              fakeTime.GetUtcNow(),
              fakeTime.GetUtcNow().AddSeconds(300),
              1,
              cancellationToken)).Single();

      await Assert.That(request!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Building);
      await Assert.That(retryClaim.RevisionNotFoundAttempts).IsEqualTo(1);
      await Assert.That(retryClaim.RunNotFoundAttempts).IsEqualTo(0);
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Run_NotFound_Uses_Independent_Exhaustion_Budget(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "run-not-found-exhaustion");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        5,
        30,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = Repository();
      var requestId = Guid.NewGuid();
      SetupDispatchAuthority(clientMock, repository);
      SetupSuccessfulDispatch(
          clientMock,
          repository,
          requestId,
          RunApiUrl(),
          RunHtmlUrl());
      clientMock.Setup(client => client.LoadWorkflowRunAsync(
              1001,
              repository,
              7001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowRun>(
              GitHubClientOutcomeKind.NotFound,
              null,
              null,
              "not-found"));

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var hostedWorker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);

      await hostedWorker.ProcessOnceAsync(cancellationToken);
      for (var attempt = 0; attempt < 5; attempt++)
      {
        fakeTime.Advance(TimeSpan.FromSeconds(301));
        await hostedWorker.ProcessOnceAsync(cancellationToken);
      }
      var request = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(request!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Blocked);
      await Assert.That(request.TerminalCategory)
          .IsEqualTo("run-not-found");
      clientMock.Verify(client => client.LoadWorkflowRunAsync(
              1001,
              repository,
              7001,
              It.IsAny<CancellationToken>()),
          Times.Exactly(5));
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Revision_NotFound_Uses_Independent_Exhaustion_Budget(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath("revision-missing");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        5,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = Repository();
      var requestId = Guid.NewGuid();
      var runApiUrl = RunApiUrl();
      var runHtmlUrl = RunHtmlUrl();
      SetupDispatchAuthority(clientMock, repository);
      SetupSuccessfulDispatch(
          clientMock,
          repository,
          requestId,
          runApiUrl,
          runHtmlUrl);
      SetupCompletedRun(
          clientMock,
          repository,
          runApiUrl,
          runHtmlUrl,
          new string('c', 40),
          "success",
          now);
      clientMock.Setup(client =>
              client.LoadWorkflowFileRevisionAtCommitAsync(
                  1001,
                  repository,
                  ".github/workflows/image-candidate.yml",
                  new string('c', 40),
                  It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new GitHubClientOutcome<GitHubWorkflowFileRevision>(
                  GitHubClientOutcomeKind.NotFound,
                  null,
                  null,
                  "not-found"));

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var hostedWorker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);

      await hostedWorker.ProcessOnceAsync(cancellationToken);
      for (var attempt = 0; attempt < 5; attempt++)
      {
        fakeTime.Advance(TimeSpan.FromSeconds(301));
        await hostedWorker.ProcessOnceAsync(cancellationToken);
      }
      var request = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(request!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Blocked);
      await Assert.That(request.TerminalCategory)
          .IsEqualTo("run-revision-not-found");
      clientMock.Verify(client =>
              client.LoadWorkflowFileRevisionAtCommitAsync(
                  1001,
                  repository,
                  ".github/workflows/image-candidate.yml",
                  new string('c', 40),
                  It.IsAny<CancellationToken>()),
          Times.Exactly(5));
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Run_Revision_Rate_Limit_Retries_Then_Qualifies(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath("revision-rate-limit");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        6,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = Repository();
      var requestId = Guid.NewGuid();
      var runApiUrl = RunApiUrl();
      var runHtmlUrl = RunHtmlUrl();
      SetupDispatchAuthority(clientMock, repository);
      SetupSuccessfulDispatch(
          clientMock,
          repository,
          requestId,
          runApiUrl,
          runHtmlUrl);
      SetupCompletedRun(
          clientMock,
          repository,
          runApiUrl,
          runHtmlUrl,
          new string('c', 40),
          "success",
          now);
      clientMock.SetupSequence(client =>
              client.LoadWorkflowFileRevisionAtCommitAsync(
                  1001,
                  repository,
                  ".github/workflows/image-candidate.yml",
                  new string('c', 40),
                  It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new GitHubClientOutcome<GitHubWorkflowFileRevision>(
                  GitHubClientOutcomeKind.RateLimited,
                  null,
                  now.AddMinutes(1),
                  "rate-limited"))
          .ReturnsAsync(RevisionOutcome(
              new string('c', 40),
              new string('a', 40)));

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var hostedWorker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);

      await hostedWorker.ProcessOnceAsync(cancellationToken);
      fakeTime.Advance(TimeSpan.FromSeconds(15));
      await hostedWorker.ProcessOnceAsync(cancellationToken);
      var deferred = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      fakeTime.Advance(TimeSpan.FromSeconds(45));
      await hostedWorker.ProcessOnceAsync(cancellationToken);
      var qualifying = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(deferred!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Building);
      await Assert.That(qualifying!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Qualifying);
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  [Arguments(0)]
  [Arguments(1)]
  [Arguments(2)]
  public async Task Cancellation_During_Authority_Lookup_Remains_Retryable(
      int cancelledLookup,
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            $"authority-cancel-{cancelledLookup}");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        7,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = Repository();
      var requestId = Guid.NewGuid();
      SetupCancellableDispatchAuthority(
          clientMock,
          repository,
          cancelledLookup);
      SetupSuccessfulDispatch(
          clientMock,
          repository,
          requestId,
          RunApiUrl(),
          RunHtmlUrl());

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var hostedWorker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);

      await hostedWorker.ProcessOnceAsync(cancellationToken);
      var retryable = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      fakeTime.Advance(TimeSpan.FromSeconds(301));
      await hostedWorker.ProcessOnceAsync(cancellationToken);
      var building = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(retryable!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Requested);
      await Assert.That(building!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Building);
      clientMock.Verify(client => client.DispatchWorkflowAsync(
              1001,
              repository,
              3001,
              "release/v1",
              It.IsAny<IReadOnlyDictionary<string, string>>(),
              It.IsAny<CancellationToken>()),
          Times.Once());
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Crash_After_Mark_Before_Send_Blocks_On_Restart(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "crash-after-mark");
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        8,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = Repository();
      var requestId = Guid.NewGuid();
      SetupDispatchAuthority(clientMock, repository);
      clientMock.Setup(client => client.DispatchWorkflowAsync(
              1001,
              repository,
              3001,
              "release/v1",
              It.IsAny<IReadOnlyDictionary<string, string>>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var hostedWorker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);

      await Assert.That(async () =>
          await hostedWorker.ProcessOnceAsync(cancellationToken))
          .Throws<OperationCanceledException>();
      var indeterminate = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      fakeTime.Advance(TimeSpan.FromSeconds(301));
      await hostedWorker.ProcessOnceAsync(cancellationToken);
      var blocked = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);

      await Assert.That(indeterminate!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Dispatching);
      await Assert.That(blocked!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Blocked);
      await Assert.That(blocked.TerminalCategory)
          .IsEqualTo("dispatch-indeterminate");
      clientMock.Verify(client => client.DispatchWorkflowAsync(
              1001,
              repository,
              3001,
              "release/v1",
              It.IsAny<IReadOnlyDictionary<string, string>>(),
              It.IsAny<CancellationToken>()),
          Times.Once());
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  private static async Task<ImageBuildRequest> RunCompletedObservationAsync(
      string suffix,
      string conclusion,
      string headSha,
      string? revisionBlobSha,
      CancellationToken cancellationToken,
      GitHubClientOutcomeKind revisionKind =
          GitHubClientOutcomeKind.Success)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(suffix);
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        9,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = Repository();
      var requestId = Guid.NewGuid();
      var runApiUrl = RunApiUrl();
      var runHtmlUrl = RunHtmlUrl();
      SetupDispatchAuthority(clientMock, repository);
      SetupSuccessfulDispatch(
          clientMock,
          repository,
          requestId,
          runApiUrl,
          runHtmlUrl);
      SetupCompletedRun(
          clientMock,
          repository,
          runApiUrl,
          runHtmlUrl,
          headSha,
          conclusion,
          now);
      if (revisionBlobSha is not null)
      {
        SetupRunRevision(
            clientMock,
            repository,
            headSha,
            revisionBlobSha);
      }
      else if (revisionKind != GitHubClientOutcomeKind.Success
          && headSha.Length == 40)
      {
        clientMock.Setup(client =>
                client.LoadWorkflowFileRevisionAtCommitAsync(
                    1001,
                    repository,
                    ".github/workflows/image-candidate.yml",
                    headSha,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new GitHubClientOutcome<GitHubWorkflowFileRevision>(
                    revisionKind,
                    null,
                    null,
                    "invalid-response"));
      }

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var hostedWorker = await GetStoppedWorkerAsync(
          factory,
          cancellationToken);
      var store = factory.Services.GetRequiredService<IImageCandidateStore>();
      await SeedAsync(
          factory.Services,
          store,
          requestId,
          now,
          cancellationToken);

      await hostedWorker.ProcessOnceAsync(cancellationToken);
      fakeTime.Advance(TimeSpan.FromSeconds(15));
      await hostedWorker.ProcessOnceAsync(cancellationToken);
      return (await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken))!;
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  private static ValueTask<ImageBuildExecutionWorker> GetStoppedWorkerAsync(
      WebApplicationFactory<Program> factory,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    return ValueTask.FromResult(
        factory.Services.GetRequiredService<ImageBuildExecutionWorker>());
  }

  private static GitHubRepositoryIdentity Repository() =>
      new(2001, "ncosentino", "pitcrew");

  private static Uri RunApiUrl() =>
      new("https://api.github.example/runs/7001", UriKind.Absolute);

  private static Uri RunHtmlUrl() =>
      new("https://github.example/runs/7001", UriKind.Absolute);

  private static void SetupSuccessfulDispatch(
      Mock<IGitHubImageWorkflowClient> clientMock,
      GitHubRepositoryIdentity repository,
      Guid requestId,
      Uri runApiUrl,
      Uri runHtmlUrl)
  {
    clientMock.Setup(client => client.DispatchWorkflowAsync(
            1001,
            repository,
            3001,
            "release/v1",
            It.Is<IReadOnlyDictionary<string, string>>(inputs =>
                inputs["pitcrew_request_id"] == requestId.ToString("D")),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new GitHubClientOutcome<GitHubWorkflowDispatch>(
                GitHubClientOutcomeKind.Success,
                new GitHubWorkflowDispatch(
                    7001,
                    runApiUrl,
                    runHtmlUrl),
                null,
                null));
  }

  private static void SetupCompletedRun(
      Mock<IGitHubImageWorkflowClient> clientMock,
      GitHubRepositoryIdentity repository,
      Uri runApiUrl,
      Uri runHtmlUrl,
      string headSha,
      string conclusion,
      DateTimeOffset now)
  {
    clientMock.Setup(client => client.LoadWorkflowRunAsync(
            1001,
            repository,
            7001,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new GitHubClientOutcome<GitHubWorkflowRun>(
                GitHubClientOutcomeKind.Success,
                new GitHubWorkflowRun(
                    7001,
                    3001,
                    headSha,
                    "completed",
                    conclusion,
                    runApiUrl,
                    runHtmlUrl,
                    now,
                    now.AddMinutes(1),
                    "workflow_dispatch"),
                null,
                null));
  }

  private static GitHubClientOutcome<GitHubWorkflowFileRevision>
      RevisionOutcome(
          string headSha,
          string blobSha) =>
          new(
              GitHubClientOutcomeKind.Success,
              new GitHubWorkflowFileRevision(
                  ".github/workflows/image-candidate.yml",
                  blobSha,
                  headSha),
              null,
              null);

  private static void SetupRunRevision(
      Mock<IGitHubImageWorkflowClient> clientMock,
      GitHubRepositoryIdentity repository,
      string headSha,
      string blobSha)
  {
    clientMock.Setup(client =>
            client.LoadWorkflowFileRevisionAtCommitAsync(
                1001,
                repository,
                ".github/workflows/image-candidate.yml",
                headSha,
                It.IsAny<CancellationToken>()))
        .ReturnsAsync(RevisionOutcome(headSha, blobSha));
  }

  private static void SetupCancellableDispatchAuthority(
      Mock<IGitHubImageWorkflowClient> clientMock,
      GitHubRepositoryIdentity repository,
      int cancelledLookup)
  {
    var workflow = new GitHubWorkflowIdentity(
        3001,
        "Build candidate",
        ".github/workflows/image-candidate.yml",
        GitHubWorkflowState.Active);
    var cancelledRepository =
        new GitHubClientOutcome<GitHubRepositoryIdentity>(
            GitHubClientOutcomeKind.Cancelled,
            null,
            null,
            "cancelled");
    var successfulRepository =
        new GitHubClientOutcome<GitHubRepositoryIdentity>(
            GitHubClientOutcomeKind.Success,
            repository,
            null,
            null);
    var cancelledWorkflow =
        new GitHubClientOutcome<GitHubWorkflowIdentity>(
            GitHubClientOutcomeKind.Cancelled,
            null,
            null,
            "cancelled");
    var successfulWorkflow =
        new GitHubClientOutcome<GitHubWorkflowIdentity>(
            GitHubClientOutcomeKind.Success,
            workflow,
            null,
            null);
    var cancelledRevision =
        new GitHubClientOutcome<GitHubWorkflowFileRevision>(
            GitHubClientOutcomeKind.Cancelled,
            null,
            null,
            "cancelled");
    var successfulRevision = new GitHubClientOutcome<GitHubWorkflowFileRevision>(
        GitHubClientOutcomeKind.Success,
        new GitHubWorkflowFileRevision(
            workflow.Path,
            new string('a', 40),
            "release/v1"),
        null,
        null);

    var repositorySetup = clientMock.SetupSequence(client =>
        client.LoadRepositoryAsync(
            1001,
            2001,
            It.IsAny<CancellationToken>()));
    if (cancelledLookup == 0)
    {
      repositorySetup
          .ReturnsAsync(cancelledRepository)
          .ReturnsAsync(successfulRepository);
    }
    else
    {
      repositorySetup
          .ReturnsAsync(successfulRepository)
          .ReturnsAsync(successfulRepository);
    }

    var workflowSetup = clientMock.SetupSequence(client =>
        client.LoadWorkflowAsync(
            1001,
            repository,
            3001,
            It.IsAny<CancellationToken>()));
    if (cancelledLookup == 1)
    {
      workflowSetup
          .ReturnsAsync(cancelledWorkflow)
          .ReturnsAsync(successfulWorkflow);
    }
    else if (cancelledLookup == 0)
    {
      workflowSetup.ReturnsAsync(successfulWorkflow);
    }
    else
    {
      workflowSetup
          .ReturnsAsync(successfulWorkflow)
          .ReturnsAsync(successfulWorkflow);
    }

    var revisionSetup = clientMock.SetupSequence(client =>
        client.LoadWorkflowFileRevisionAsync(
            1001,
            repository,
            workflow.Path,
            "release/v1",
            It.IsAny<CancellationToken>()));
    if (cancelledLookup == 2)
    {
      revisionSetup
          .ReturnsAsync(cancelledRevision)
          .ReturnsAsync(successfulRevision);
    }
    else
    {
      revisionSetup.ReturnsAsync(successfulRevision);
    }
  }

  private static WebApplicationFactory<Program> CreateFactory(
      FakeTimeProvider fakeTime,
      IGitHubImageWorkflowClient gitHubClient) =>
      new WebApplicationFactory<Program>()
          .WithWebHostBuilder(
              builder => builder.ConfigureServices(
                  services =>
                  {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(fakeTime);
                    services.RemoveAll<IGitHubImageWorkflowClient>();
                    services.AddSingleton(gitHubClient);
                    var workerRegistrations = services
                        .Where(descriptor =>
                            descriptor.ServiceType == typeof(IHostedService)
                            && descriptor.ImplementationType ==
                                typeof(ImageBuildExecutionWorker))
                        .ToArray();
                    if (workerRegistrations.Length != 1)
                    {
                      throw new InvalidOperationException(
                          "Expected one hosted image worker registration.");
                    }
                    foreach (var registration in workerRegistrations)
                    {
                      services.Remove(registration);
                    }
                    services.AddSingleton<ImageBuildExecutionWorker>();
                  }));

  private static void SetupDispatchAuthority(
      Mock<IGitHubImageWorkflowClient> clientMock,
      GitHubRepositoryIdentity repository)
  {
    var workflow = new GitHubWorkflowIdentity(
        3001,
        "Build candidate",
        ".github/workflows/image-candidate.yml",
        GitHubWorkflowState.Active);
    clientMock.Setup(client => client.LoadRepositoryAsync(
            1001,
            2001,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GitHubClientOutcome<GitHubRepositoryIdentity>(
            GitHubClientOutcomeKind.Success,
            repository,
            null,
            null))
        .Verifiable();
    clientMock.Setup(client => client.LoadWorkflowAsync(
            1001,
            repository,
            3001,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowIdentity>(
            GitHubClientOutcomeKind.Success,
            workflow,
            null,
            null))
        .Verifiable();
    clientMock.Setup(client => client.LoadWorkflowFileRevisionAsync(
            1001,
            repository,
            workflow.Path,
            "release/v1",
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GitHubClientOutcome<GitHubWorkflowFileRevision>(
            GitHubClientOutcomeKind.Success,
            new GitHubWorkflowFileRevision(
                workflow.Path,
                new string('a', 40),
                "release/v1"),
            null,
            null))
        .Verifiable();
  }

  private static async Task SeedAsync(
      IServiceProvider services,
      IImageCandidateStore store,
      Guid requestId,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    var owner = new DashboardUser(
        "owner-user",
        "owner-login",
        "Owner",
        null);
    var accessStore = services.GetRequiredService<IAccessStore>();
    await accessStore.EnsureTenantOwnerAsync(
        "tenant-a",
        "Tenant A",
        owner,
        now,
        cancellationToken);
    var registration = new ImageRecipeRegistration(
        "tenant-a",
        Guid.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            CultureInfo.InvariantCulture),
        1,
        1001,
        2001,
        3001,
        "ncosentino",
        "pitcrew",
        ".github/workflows/image-candidate.yml",
        new string('a', 40),
        "release/v1",
        "pitcrew-default",
        1,
        "{\"allowedSourceRefs\":[\"refs/heads/main\"]}",
        "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"channel\":{\"type\":\"string\",\"enum\":[\"stable\"]}},\"required\":[\"channel\"]}",
        owner.GitHubUserId,
        now,
        null,
        null);
    await store.CreateRecipeVersionAsync(registration, cancellationToken);
    const string inputJson = "{\"channel\":\"stable\"}";
    var request = new ImageBuildRequest(
        "tenant-a",
        requestId,
        registration.RegistrationId,
        1,
        registration.RecipeId,
        "ncosentino/pitcrew",
        new string('b', 40),
        inputJson,
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(inputJson)))
            .ToLowerInvariant(),
        owner.GitHubUserId,
        now,
        ImageBuildRequestStatus.Requested,
        null,
        null,
        null,
        null,
        now,
        "refs/heads/main",
        null);
    await store.CreateBuildRequestAsync(request, cancellationToken);
  }

  private static async Task AdvanceToQualifyingAsync(
      IImageCandidateStore store,
      Guid requestId,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    await store.ApplyBuildRequestTransitionAsync(
        "tenant-a",
        requestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Requested,
            ImageBuildRequestStatus.Dispatching,
            null,
            null,
            null,
            null,
            now),
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        "tenant-a",
        requestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Dispatching,
            ImageBuildRequestStatus.Building,
            7001,
            RunHtmlUrl().AbsoluteUri,
            null,
            null,
            now),
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        "tenant-a",
        requestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Building,
            ImageBuildRequestStatus.Qualifying,
            7001,
            RunHtmlUrl().AbsoluteUri,
            null,
            null,
            now),
        cancellationToken);
  }
}
