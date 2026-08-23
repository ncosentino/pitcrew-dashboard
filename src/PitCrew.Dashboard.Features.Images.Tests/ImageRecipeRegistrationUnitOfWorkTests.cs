using System.Security.Claims;
using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Images.Tests;

[NotInParallel]
public sealed class ImageRecipeRegistrationUnitOfWorkTests
{
  [Test]
  public async Task CreateAsync_Persists_Caller_Guid_And_Deterministic_Canonical_Json(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "canonical");
    var now = new DateTimeOffset(
        2026,
        8,
        23,
        18,
        0,
        0,
        TimeSpan.Zero);
    var registrationId = ParseGuid(
        "11111111-1111-1111-1111-111111111111");
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew-dashboard");
      var workflow = new GitHubWorkflowIdentity(
          3001,
          "Build candidate",
          ".github/workflows/image-candidate.yml",
          GitHubWorkflowState.Active);
      var revision = new GitHubWorkflowFileRevision(
          workflow.Path,
          new string('a', 40),
          "release/v1");
      SetupSuccess(
          clientMock,
          repository,
          workflow,
          revision);

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await EnsureHostReadyAsync(
          factory,
          cancellationToken);
      using var scope = factory.Services.CreateScope();
      var unitOfWork = scope.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();
      var store = scope.ServiceProvider.GetRequiredService<
          IImageCandidateStore>();

      var result = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          CreateRequest(
              "pitcrew-default",
              registrationId),
          cancellationToken);

      var stored = await store.GetRecipeRegistrationOrNullAsync(
          "local",
          registrationId,
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Succeeded);
      await Assert.That(stored).IsNotNull();
      await Assert.That(stored!.RegistrationId)
          .IsEqualTo(registrationId);
      await Assert.That(stored.Version).IsEqualTo(1);
      await Assert.That(stored.WorkflowPath)
          .IsEqualTo(workflow.Path);
      await Assert.That(stored.CandidateSchemaVersion).IsEqualTo(1);
      await Assert.That(stored.SourceRefPolicyJson)
          .IsEqualTo(
              "{\"allowedSourceRefs\":[\"refs/heads/main\",\"refs/tags/v1\"]}");
      await Assert.That(stored.InputSchemaJson)
          .IsEqualTo(
              "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"channel\":{\"type\":\"string\",\"maxLength\":12,\"enum\":[\"beta\",\"stable\"]},\"enableCache\":{\"type\":\"boolean\"}},\"required\":[\"channel\"]}");
      await Assert.That(stored.CreatedAt).IsEqualTo(now);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task CreateAsync_Replays_Exact_Guid_And_Conflicts_When_Request_Changes(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "guid-replay");
    var now = new DateTimeOffset(
        2026,
        8,
        23,
        19,
        0,
        0,
        TimeSpan.Zero);
    var registrationId = ParseGuid(
        "22222222-2222-2222-2222-222222222222");
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew-dashboard");
      var workflow = new GitHubWorkflowIdentity(
          3001,
          "Build candidate",
          ".github/workflows/image-candidate.yml",
          GitHubWorkflowState.Active);
      var revision = new GitHubWorkflowFileRevision(
          workflow.Path,
          new string('b', 40),
          "release/v1");
      SetupSuccess(
          clientMock,
          repository,
          workflow,
          revision);

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await EnsureHostReadyAsync(
          factory,
          cancellationToken);
      using var scope = factory.Services.CreateScope();
      var unitOfWork = scope.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();
      var store = scope.ServiceProvider.GetRequiredService<
          IImageCandidateStore>();
      var principal = CreatePrincipal(
          "owner-user",
          "owner-login");
      var request = CreateRequest(
          "pitcrew-default",
          registrationId);

      var created = await unitOfWork.CreateAsync(
          principal,
          "local",
          request,
          cancellationToken);
      mocks.VerifyAll();
      clientMock.Verify(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()),
          Times.Once);
      clientMock.Verify(client => client.LoadWorkflowAsync(
              1001,
              repository,
              3001,
              It.IsAny<CancellationToken>()),
          Times.Once);
      clientMock.Verify(client => client.LoadWorkflowFileRevisionAsync(
              1001,
              repository,
              workflow.Path,
              "release/v1",
              It.IsAny<CancellationToken>()),
          Times.Once);
      clientMock.Verify(client => client.LoadWorkflowFileContentAsync(
              1001,
              repository,
              revision,
              It.IsAny<CancellationToken>()),
          Times.Once);
      clientMock.VerifyNoOtherCalls();
      clientMock.Reset();
      clientMock.Setup(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(RateLimited<GitHubRepositoryIdentity>(
              now.AddMinutes(30)));
      fakeTime.Advance(TimeSpan.FromMinutes(5));
      var replay = await unitOfWork.CreateAsync(
          principal,
          "local",
          request,
          cancellationToken);
      var changed = await unitOfWork.CreateAsync(
          principal,
          "local",
          request with
          {
            AllowedSourceRefs =
            [
                "refs/heads/main",
            ],
          },
          cancellationToken);

      var registrations = await store.ListRecipeRegistrationsAsync(
          "local",
          includeDisabled: true,
          10,
          cancellationToken);

      await Assert.That(created.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Succeeded);
      await Assert.That(replay.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Unchanged);
      await Assert.That(changed.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(replay.Registration).IsNotNull();
      await Assert.That(replay.Registration!.RegistrationId)
          .IsEqualTo(registrationId);
      await Assert.That(registrations).Count().IsEqualTo(1);
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task CreateAsync_Replays_Stored_Registration_During_GitHub_Blob_Mutation(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "blob-mutation");
    var now = new DateTimeOffset(
        2026,
        8,
        23,
        19,
        30,
        0,
        TimeSpan.Zero);
    var registrationId = ParseGuid(
        "33333333-3333-3333-3333-333333333333");
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew-dashboard");
      var workflow = new GitHubWorkflowIdentity(
          3001,
          "Build candidate",
          ".github/workflows/image-candidate.yml",
          GitHubWorkflowState.Active);
      var revision = new GitHubWorkflowFileRevision(
          workflow.Path,
          new string('a', 40),
          "release/v1");
      SetupSuccess(
          clientMock,
          repository,
          workflow,
          revision);

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await EnsureHostReadyAsync(
          factory,
          cancellationToken);
      using var scope = factory.Services.CreateScope();
      var unitOfWork = scope.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();
      var store = scope.ServiceProvider.GetRequiredService<
          IImageCandidateStore>();
      var request = CreateRequest(
          "pitcrew-default",
          registrationId);

      var created = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          request,
          cancellationToken);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
      clientMock.Reset();
      clientMock.Setup(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(repository));
      clientMock.Setup(client => client.LoadWorkflowAsync(
              1001,
              repository,
              3001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(workflow));
      clientMock.Setup(client => client.LoadWorkflowFileRevisionAsync(
              1001,
              repository,
              workflow.Path,
              "release/v1",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(new GitHubWorkflowFileRevision(
              workflow.Path,
              new string('c', 40),
              "release/v1")));
      var replay = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          request,
          cancellationToken);

      var registrations = await store.ListRecipeRegistrationsAsync(
          "local",
          includeDisabled: true,
          10,
          cancellationToken);

      await Assert.That(created.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Succeeded);
      await Assert.That(replay.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Unchanged);
      await Assert.That(registrations).Count().IsEqualTo(1);
      await Assert.That(registrations[0].WorkflowBlobSha)
          .IsEqualTo(new string('a', 40));
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task CreateAsync_Conflicting_Replay_Does_Not_Call_GitHub_During_Outage(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "outage-replay");
    var now = new DateTimeOffset(
        2026,
        8,
        23,
        19,
        45,
        0,
        TimeSpan.Zero);
    var registrationId = ParseGuid(
        "34333333-3333-3333-3333-333333333333");
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew-dashboard");
      var workflow = new GitHubWorkflowIdentity(
          3001,
          "Build candidate",
          ".github/workflows/image-candidate.yml",
          GitHubWorkflowState.Active);
      var revision = new GitHubWorkflowFileRevision(
          workflow.Path,
          new string('d', 40),
          "release/v1");
      SetupSuccess(
          clientMock,
          repository,
          workflow,
          revision);

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await EnsureHostReadyAsync(
          factory,
          cancellationToken);
      using var scope = factory.Services.CreateScope();
      var unitOfWork = scope.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();
      var request = CreateRequest(
          "pitcrew-default",
          registrationId);

      var created = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          request,
          cancellationToken);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
      clientMock.Reset();
      clientMock.Setup(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Unavailable<GitHubRepositoryIdentity>());

      var changed = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          request with
          {
            RecipeId = "pitcrew-updated",
          },
          cancellationToken);

      await Assert.That(created.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Succeeded);
      await Assert.That(changed.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task CreateAsync_Assigns_Monotonic_Versions_For_Concurrent_Unique_Guids(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "concurrent-versions");
    var now = new DateTimeOffset(
        2026,
        8,
        23,
        20,
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
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew-dashboard");
      var workflow = new GitHubWorkflowIdentity(
          3001,
          "Build candidate",
          ".github/workflows/image-candidate.yml",
          GitHubWorkflowState.Active);
      var revision = new GitHubWorkflowFileRevision(
          workflow.Path,
          new string('d', 40),
          "release/v1");
      SetupSuccess(
          clientMock,
          repository,
          workflow,
          revision);

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await EnsureHostReadyAsync(
          factory,
          cancellationToken);
      await using var scopeOne = factory.Services.CreateAsyncScope();
      await using var scopeTwo = factory.Services.CreateAsyncScope();
      var unitOfWorkOne = scopeOne.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();
      var unitOfWorkTwo = scopeTwo.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();
      var store = scopeOne.ServiceProvider.GetRequiredService<
          IImageCandidateStore>();
      var principal = CreatePrincipal(
          "owner-user",
          "owner-login");

      var results = await Task.WhenAll(
          unitOfWorkOne.CreateAsync(
              principal,
              "local",
              CreateRequest(
                  "pitcrew-default",
                  ParseGuid("44444444-4444-4444-4444-444444444444")),
              cancellationToken),
          unitOfWorkTwo.CreateAsync(
              principal,
              "local",
              CreateRequest(
                  "pitcrew-default",
                  ParseGuid("55555555-5555-5555-5555-555555555555")),
              cancellationToken));

      var versions = await store.ListRecipeVersionsAsync(
          "local",
          "pitcrew-default",
          10,
          cancellationToken);

      await Assert.That(results[0].Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Succeeded);
      await Assert.That(results[1].Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Succeeded);
      await Assert.That(results.Select(
              static result => result.Registration!.Version).Order())
          .IsEquivalentTo([
              1,
              2,
          ]);
      await Assert.That(versions.Select(
              static version => version.Version))
          .IsEquivalentTo([
              2,
              1,
          ]);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task CreateAsync_Rejects_Invalid_Workflow_Definitions_Without_Persistence(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "workflow-definition-validation");
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(new DateTimeOffset(
          2026,
          8,
          23,
          20,
          30,
          0,
          TimeSpan.Zero));
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew-dashboard");
      var workflow = new GitHubWorkflowIdentity(
          3001,
          "Build candidate",
          ".github/workflows/image-candidate.yml",
          GitHubWorkflowState.Active);
      var revision = new GitHubWorkflowFileRevision(
          workflow.Path,
          new string('f', 40),
          "release/v1");
      clientMock.Setup(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(repository));
      clientMock.Setup(client => client.LoadWorkflowAsync(
              1001,
              repository,
              3001,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(workflow));
      clientMock.Setup(client => client.LoadWorkflowFileRevisionAsync(
              1001,
              repository,
              workflow.Path,
              "release/v1",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(revision));
      clientMock.SetupSequence(client => client.LoadWorkflowFileContentAsync(
              1001,
              repository,
              revision,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              "on: push")))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              """
              on:
                workflow_dispatch:
                  inputs:
                    pitcrew_request_id:
                      type: string
                      required: true
                    pitcrew_source_commit:
                      type: boolean
                      required: true
                    pitcrew_recipe_id:
                      type: string
                      required: true
                    channel:
                      type: choice
                      required: true
                      options:
                        - beta
                        - stable
                    enableCache:
                      type: boolean
                      required: false
              """)))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              """
              on:
                workflow_dispatch:
                  inputs:
                    pitcrew_request_id:
                      type: string
                      required: true
                    pitcrew_recipe_id:
                      type: string
                      required: true
                    channel:
                      type: choice
                      required: true
                      options:
                        - beta
                        - stable
                    enableCache:
                      type: boolean
                      required: false
              """)))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              """
              on:
                workflow_dispatch:
                  inputs:
                    pitcrew_request_id:
                      type: string
                      required: true
                    pitcrew_source_commit:
                      type: string
                      required: true
                    pitcrew_recipe_id:
                      type: string
                      required: true
                    channel:
                      type: choice
                      required: true
                      options:
                        - beta
                        - stable
                    enableCache:
                      type: boolean
                      required: false
                    unexpected:
                      type: string
                      required: false
              """)))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              """
              on:
                workflow_dispatch:
                  inputs:
                    pitcrew_request_id:
                      type: string
                      required: true
                    pitcrew_source_commit:
                      type: string
                      required: true
                    pitcrew_recipe_id:
                      type: string
                      required: true
                    channel:
                      type: string
                      required: true
                    enableCache:
                      type: boolean
                      required: false
              """)))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              "on: [")))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              new string('x', 65_537))))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              """
              on:
                workflow_dispatch:
                  inputs:
                    shared: &shared
                      type: string
                      required: true
                    pitcrew_request_id: *shared
                    pitcrew_source_commit:
                      type: string
                      required: true
                    pitcrew_recipe_id:
                      type: string
                      required: true
                    channel:
                      type: choice
                      required: true
                      options:
                        - beta
                        - stable
                    enableCache:
                      type: boolean
                      required: false
              """)))
          .ReturnsAsync(Success(CreateWorkflowContent(
              revision,
              CreateDeepWorkflowYaml())));

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await EnsureHostReadyAsync(
          factory,
          cancellationToken);
      using var scope = factory.Services.CreateScope();
      var unitOfWork = scope.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();
      var store = scope.ServiceProvider.GetRequiredService<
          IImageCandidateStore>();
      var principal = CreatePrincipal(
          "owner-user",
          "owner-login");

      var missingDispatch = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("10101010-1010-1010-1010-101010101010")),
          cancellationToken);
      var wrongReserved = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("11111110-1111-1111-1111-111111111111")),
          cancellationToken);
      var missingReserved = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("12121212-1212-1212-1212-121212121212")),
          cancellationToken);
      var extraCustom = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("13131313-1313-1313-1313-131313131313")),
          cancellationToken);
      var mismatchedCustom = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("14141414-1414-1414-1414-141414141414")),
          cancellationToken);
      var malformed = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("15151515-1515-1515-1515-151515151515")),
          cancellationToken);
      var oversized = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("16161616-1616-1616-1616-161616161616")),
          cancellationToken);
      var aliased = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("17171717-1717-1717-1717-171717171717")),
          cancellationToken);
      var deep = await unitOfWork.CreateAsync(
          principal,
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("18181818-1818-1818-1818-181818181818")),
          cancellationToken);

      var registrations = await store.ListRecipeRegistrationsAsync(
          "local",
          includeDisabled: true,
          10,
          cancellationToken);

      await Assert.That(missingDispatch.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(wrongReserved.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(missingReserved.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(extraCustom.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(mismatchedCustom.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(malformed.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(oversized.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(aliased.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(deep.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(missingDispatch.Error)
          .Contains("workflow_dispatch");
      await Assert.That(wrongReserved.Error)
          .Contains("pitcrew_source_commit");
      await Assert.That(missingReserved.Error)
          .Contains("pitcrew_source_commit");
      await Assert.That(extraCustom.Error)
          .Contains("unexpected");
      await Assert.That(mismatchedCustom.Error)
          .Contains("channel");
      await Assert.That(malformed.Error)
          .Contains("valid YAML");
      await Assert.That(oversized.Error)
          .Contains("supported size");
      await Assert.That(aliased.Error)
          .Contains("anchors, aliases, or tags");
      await Assert.That(deep.Error)
          .Contains("nesting depth");
      await Assert.That(registrations).IsEmpty();
      clientMock.Verify(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()),
          Times.Exactly(9));
      clientMock.Verify(client => client.LoadWorkflowAsync(
              1001,
              repository,
              3001,
              It.IsAny<CancellationToken>()),
          Times.Exactly(9));
      clientMock.Verify(client => client.LoadWorkflowFileRevisionAsync(
              1001,
              repository,
              workflow.Path,
              "release/v1",
              It.IsAny<CancellationToken>()),
          Times.Exactly(9));
      clientMock.Verify(client => client.LoadWorkflowFileContentAsync(
              1001,
              repository,
              revision,
              It.IsAny<CancellationToken>()),
          Times.Exactly(9));
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task CreateAsync_Rejects_Invalid_Request_Before_GitHub_Calls(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "validation");
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(new DateTimeOffset(
          2026,
          8,
          23,
          21,
          0,
          0,
          TimeSpan.Zero));
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await EnsureHostReadyAsync(
          factory,
          cancellationToken);
      using var scope = factory.Services.CreateScope();
      var unitOfWork = scope.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();

      var invalidSchema = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("66666666-6666-6666-6666-666666666666")) with
          {
            CandidateSchemaVersion = 2,
          },
          cancellationToken);
      var invalidPath = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("77777777-7777-7777-7777-777777777777")) with
          {
            WorkflowPath = ".github/not-workflows/image-candidate.yml",
          },
          cancellationToken);
      var invalidReservedInput = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("88888888-8888-8888-8888-888888888888")) with
          {
            Inputs =
            [
                new ImageRecipeInputDefinition(
                    "pitcrew_source_commit",
                    "string",
                    true,
                    40,
                    null),
            ],
          },
          cancellationToken);
      var invalidDispatchRef = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("99999999-9999-9999-9999-999999999999")) with
          {
            DispatchRef = "https://github.com/ncosentino/pitcrew-dashboard",
          },
          cancellationToken);

      await Assert.That(invalidSchema.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Invalid);
      await Assert.That(invalidPath.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Invalid);
      await Assert.That(invalidReservedInput.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Invalid);
      await Assert.That(invalidDispatchRef.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Invalid);
      await Assert.That(invalidSchema.Error)
          .Contains("Candidate schema version");
      await Assert.That(invalidPath.Error)
          .Contains(".github/workflows");
      await Assert.That(invalidReservedInput.Error)
          .Contains("pitcrew_source_commit");
      await Assert.That(invalidDispatchRef.Error)
          .Contains("Dispatch ref");
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task CreateAsync_Conflicts_When_GitHub_Identity_Does_Not_Match_Request(
      CancellationToken cancellationToken)
  {
    var databasePath =
        ImagesFeatureTestEnvironment.CreateDatabasePath(
            "identity-mismatch");
    try
    {
      using var configuration =
          new ImagesFeatureTestConfigurationScope(databasePath);
      var fakeTime = new FakeTimeProvider(new DateTimeOffset(
          2026,
          8,
          23,
          22,
          0,
          0,
          TimeSpan.Zero));
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      var repository = new GitHubRepositoryIdentity(
          2001,
          "ncosentino",
          "pitcrew-dashboard");
      var workflow = new GitHubWorkflowIdentity(
          3001,
          "Build candidate",
          ".github/workflows/other-image-candidate.yml",
          GitHubWorkflowState.Active);
      clientMock.Setup(client => client.LoadRepositoryAsync(
              1001,
              2001,
              cancellationToken))
          .ReturnsAsync(Success(repository))
          .Verifiable();
      clientMock.Setup(client => client.LoadWorkflowAsync(
              1001,
              repository,
              3001,
              cancellationToken))
          .ReturnsAsync(Success(workflow))
          .Verifiable();
      clientMock.Setup(client => client.LoadWorkflowFileRevisionAsync(
              1001,
              repository,
              workflow.Path,
              "release/v1",
              cancellationToken))
          .ReturnsAsync(Success(new GitHubWorkflowFileRevision(
              workflow.Path,
              new string('e', 40),
              "release/v1")))
          .Verifiable();

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await EnsureHostReadyAsync(
          factory,
          cancellationToken);
      using var scope = factory.Services.CreateScope();
      var unitOfWork = scope.ServiceProvider.GetRequiredService<
          IImageRecipeRegistrationUnitOfWork>();

      var result = await unitOfWork.CreateAsync(
          CreatePrincipal(
              "owner-user",
              "owner-login"),
          "local",
          CreateRequest(
              "pitcrew-default",
              ParseGuid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(ImageRecipeRegistrationCommandStatus.Conflict);
      await Assert.That(result.Code)
          .IsEqualTo("github_workflow_path_mismatch");
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      ImagesFeatureTestEnvironment.DeleteDatabase(databasePath);
    }
  }

  private static async Task EnsureHostReadyAsync(
      WebApplicationFactory<Program> factory,
      CancellationToken cancellationToken)
  {
    using var bootstrapClient = factory.CreateClient();
    using var bootstrapResponse = await bootstrapClient.GetAsync(
        "/health",
        cancellationToken);
    bootstrapResponse.EnsureSuccessStatusCode();
  }

  private static void SetupSuccess(
      Mock<IGitHubImageWorkflowClient> clientMock,
      GitHubRepositoryIdentity repository,
      GitHubWorkflowIdentity workflow,
      GitHubWorkflowFileRevision revision,
      string? workflowYaml = null)
  {
    clientMock.Setup(client => client.LoadRepositoryAsync(
            1001,
            2001,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(Success(repository))
        .Verifiable();
    clientMock.Setup(client => client.LoadWorkflowAsync(
            1001,
            repository,
            3001,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(Success(workflow))
        .Verifiable();
    clientMock.Setup(client => client.LoadWorkflowFileRevisionAsync(
            1001,
            repository,
            workflow.Path,
            "release/v1",
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(Success(revision))
        .Verifiable();
    clientMock.Setup(client => client.LoadWorkflowFileContentAsync(
            1001,
            repository,
            revision,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(Success(CreateWorkflowContent(
            revision,
            workflowYaml)))
        .Verifiable();
  }

  private static WebApplicationFactory<Program> CreateFactory(
      FakeTimeProvider fakeTime,
      IGitHubImageWorkflowClient gitHubImageWorkflowClient) =>
      new WebApplicationFactory<Program>()
          .WithWebHostBuilder(
              builder => builder.ConfigureServices(
                  services =>
                  {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(
                        fakeTime);
                    services.RemoveAll<IGitHubImageWorkflowClient>();
                    services.AddSingleton(gitHubImageWorkflowClient);
                  }));

  private static ClaimsPrincipal CreatePrincipal(
      string githubUserId,
      string githubLogin) =>
      new(new ClaimsIdentity(
      [
          new Claim(
              PitCrewClaimTypes.GitHubUserId,
              githubUserId),
          new Claim(
              PitCrewClaimTypes.GitHubLogin,
              githubLogin),
          new Claim(
              ClaimTypes.Name,
              githubLogin),
      ],
      "test"));

  private static Guid ParseGuid(string value) =>
      Guid.Parse(
          value,
          CultureInfo.InvariantCulture);

  private static RegisterImageRecipeInput CreateRequest(
      string recipeId,
      Guid registrationId) =>
      new(
          registrationId,
          "1001",
          "2001",
          "3001",
          ".github/workflows/image-candidate.yml",
          "release/v1",
          recipeId,
          1,
          [
              "refs/tags/v1",
              "refs/heads/main",
          ],
          [
              new ImageRecipeInputDefinition(
                  "enableCache",
                  "boolean",
                  false,
                  null,
                  null),
              new ImageRecipeInputDefinition(
                  "channel",
                  "string",
                  true,
                  12,
                  [
                      "stable",
                      "beta",
                  ]),
          ]);

  private static GitHubWorkflowFileContent CreateWorkflowContent(
      GitHubWorkflowFileRevision revision,
      string? workflowYaml = null) =>
      new(
          revision.Path,
          revision.BlobSha,
          revision.Reference,
          workflowYaml ??
          CreateValidWorkflowYaml());

  private static string CreateValidWorkflowYaml() =>
      """
      on:
        workflow_dispatch:
          inputs:
            pitcrew_request_id:
              type: string
              required: true
            pitcrew_source_commit:
              type: string
              required: true
            pitcrew_recipe_id:
              type: string
              required: true
            channel:
              type: choice
              required: true
              options:
                - beta
                - stable
            enableCache:
              type: boolean
              required: false
      jobs:
        build:
          runs-on: ubuntu-latest
          steps:
            - run: echo build
      """;

  private static string CreateDeepWorkflowYaml()
  {
    var builder = new StringBuilder();
    builder.AppendLine("root:");
    for (var depth = 0; depth < 40; depth++)
    {
      builder.Append(' ', (depth + 1) * 2);
      builder.Append("nested");
      builder.Append(depth);
      builder.AppendLine(":");
    }

    return builder.ToString();
  }

  private static GitHubClientOutcome<T> Success<T>(T value) =>
      new(
          GitHubClientOutcomeKind.Success,
          value,
          null,
          null);

  private static GitHubClientOutcome<T> RateLimited<T>(
      DateTimeOffset retryAt) =>
      new(
          GitHubClientOutcomeKind.RateLimited,
          default,
          retryAt,
          "rate-limited");

  private static GitHubClientOutcome<T> Unavailable<T>() =>
      new(
          GitHubClientOutcomeKind.TransientFailure,
          default,
          null,
          "transport-failure");
}
