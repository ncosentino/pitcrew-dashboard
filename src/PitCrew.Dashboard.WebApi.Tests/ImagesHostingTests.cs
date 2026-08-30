using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Globalization;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Moq;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Features.Images;
using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Dashboard.Kernel.ImageRollouts;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

[NotInParallel]
public sealed class ImagesHostingTests
{
  private const string SystemAdministratorGitHubUserId = "900";
  private const string AdministratorGitHubUserId = "1001";
  private const string ViewerGitHubUserId = "1002";

  [Test]
  public async Task Administrator_Can_Register_List_Get_And_Disable_Image_Recipes(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    var now = new DateTimeOffset(
        2026,
        8,
        23,
        21,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
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
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);

      using var createdResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
              session.AntiforgeryToken,
              CreateRequest(
                  "pitcrew-default",
                  ParseGuid("11111111-1111-1111-1111-111111111111")),
              cancellationToken);
      var created = await createdResponse.Content.ReadFromJsonAsync<
          ImageRecipeRegistrationResponse>(
              cancellationToken);

      fakeTime.Advance(TimeSpan.FromMinutes(1));
      using var secondCreatedResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
              session.AntiforgeryToken,
              CreateRequest(
                  "pitcrew-secondary",
                  ParseGuid("22222222-2222-2222-2222-222222222222")),
              cancellationToken);
      var secondCreated = await secondCreatedResponse.Content.ReadFromJsonAsync<
          ImageRecipeRegistrationResponse>(
              cancellationToken);

      var bounded = await client.GetFromJsonAsync<
          ImageRecipeRegistrationListResponse>(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations?limit=1",
              cancellationToken);
      var exact = await client.GetFromJsonAsync<
          ImageRecipeRegistrationResponse>(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations/{created!.RegistrationId:D}",
              cancellationToken);

      fakeTime.Advance(TimeSpan.FromMinutes(5));
      using var disabledResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations/{created.RegistrationId:D}/disable",
              session.AntiforgeryToken,
              null,
              cancellationToken);
      using var disabledReplayResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations/{created.RegistrationId:D}/disable",
              session.AntiforgeryToken,
              null,
              cancellationToken);
      var activeOnly = await client.GetFromJsonAsync<
          ImageRecipeRegistrationListResponse>(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations?limit=10",
              cancellationToken);
      var includingDisabled = await client.GetFromJsonAsync<
          ImageRecipeRegistrationListResponse>(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations?limit=10&includeDisabled=true",
              cancellationToken);
      var disabled = await client.GetFromJsonAsync<
          ImageRecipeRegistrationResponse>(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations/{created.RegistrationId:D}",
              cancellationToken);

      await Assert.That(createdResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Created);
      await Assert.That(secondCreatedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Created);
      await Assert.That(created).IsNotNull();
      await Assert.That(secondCreated).IsNotNull();
      await Assert.That(created!.WorkflowPath)
          .IsEqualTo(workflow.Path);
      await Assert.That(created.CandidateSchemaVersion).IsEqualTo(1);
      await Assert.That(secondCreated!.Version).IsEqualTo(1);
      await Assert.That(bounded).IsNotNull();
      await Assert.That(bounded!.Registrations).HasSingleItem();
      await Assert.That(bounded.Truncated).IsTrue()
          .Because("the bounded list omits one older registration");
      await Assert.That(exact).IsNotNull();
      await Assert.That(exact!.RegistrationId)
          .IsEqualTo(created.RegistrationId);
      await Assert.That(disabledResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.NoContent);
      await Assert.That(disabledReplayResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.NoContent);
      await Assert.That(activeOnly).IsNotNull();
      await Assert.That(activeOnly!.Registrations).HasSingleItem();
      await Assert.That(activeOnly.Registrations[0].RegistrationId)
          .IsEqualTo(secondCreated.RegistrationId);
      await Assert.That(includingDisabled).IsNotNull();
      await Assert.That(includingDisabled!.Registrations).Count().IsEqualTo(2);
      await Assert.That(disabled).IsNotNull();
      await Assert.That(disabled!.DisabledByGitHubUserId)
          .IsEqualTo(session.User.GitHubUserId);
      await Assert.That(disabled.DisabledAt)
          .IsEqualTo(now.AddMinutes(6));
      clientMock.Verify(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()),
          Times.Exactly(2));
      clientMock.Verify(client => client.LoadWorkflowAsync(
              1001,
              repository,
              3001,
              It.IsAny<CancellationToken>()),
          Times.Exactly(2));
      clientMock.Verify(client => client.LoadWorkflowFileRevisionAsync(
              1001,
              repository,
              workflow.Path,
              "release/v1",
              It.IsAny<CancellationToken>()),
          Times.Exactly(2));
      clientMock.Verify(client => client.LoadWorkflowFileContentAsync(
              1001,
              repository,
              revision,
              It.IsAny<CancellationToken>()),
          Times.Exactly(2));
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Candidate_List_And_Detail_Are_Bounded_And_Tenant_Authorized(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
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
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          SystemAdministratorGitHubUserId,
          "Production");
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await SeedTenantAccessAsync(factory, cancellationToken);
      var candidateId = await SeedCandidateAsync(
          factory.Services,
          now,
          cancellationToken);

      using var viewerClient = CreateAuthenticatedClient(
          factory,
          ViewerGitHubUserId);
      using var outsiderClient = CreateAuthenticatedClient(
          factory,
          "9999");
      using var anonymousClient = factory.CreateClient();
      using var listResponse = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/candidates?limit=1",
          cancellationToken);
      var list = await listResponse.Content.ReadFromJsonAsync<
          ImageCandidateListResponse>(
              cancellationToken);
      using var detailResponse = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/candidates/{candidateId:D}",
          cancellationToken);
      var detailJson = await detailResponse.Content.ReadAsStringAsync(
          cancellationToken);
      var detail = JsonSerializer.Deserialize<ImageCandidateResponse>(
          detailJson,
          new JsonSerializerOptions(JsonSerializerDefaults.Web));
      using var invalidLimitResponse = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/candidates?limit=101",
          cancellationToken);
      using var anonymousResponse = await anonymousClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/candidates",
          cancellationToken);
      using var outsiderResponse = await outsiderClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/candidates",
          cancellationToken);
      using var crossTenantResponse = await viewerClient.GetAsync(
          $"/api/tenants/tenant-b/images/candidates/{candidateId:D}",
          cancellationToken);

      await Assert.That(listResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(list).IsNotNull();
      await Assert.That(list!.Candidates).HasSingleItem();
      await Assert.That(list.Truncated).IsFalse()
          .Because("the tenant has one candidate");
      await Assert.That(detailResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(detail).IsNotNull();
      await Assert.That(detail!.CandidateId).IsEqualTo(candidateId);
      await Assert.That(detail.GitHubRunId).IsEqualTo("7001");
      await Assert.That(detail.ArtifactId).IsEqualTo("8001");
      await Assert.That(detail.Qualifications).Count().IsEqualTo(4);
      await Assert.That(detailJson.Contains(
              "reportJson",
              StringComparison.Ordinal))
          .IsFalse()
          .Because("candidate responses must not expose raw reports");
      await Assert.That(invalidLimitResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(anonymousResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
      await Assert.That(outsiderResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden);
      await Assert.That(crossTenantResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.NotFound);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Tenant_Viewer_Is_Read_Only_And_Administrator_Can_Mutate(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    var now = new DateTimeOffset(
        2026,
        8,
        23,
        22,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          SystemAdministratorGitHubUserId,
          "Production");
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
      var sourceCommit = new string('e', 40);
      clientMock.Setup(client => client.ResolveCommitAsync(
              1001,
              repository,
              sourceCommit,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(new GitHubCommitIdentity(sourceCommit)))
          .Verifiable();
      clientMock.Setup(client => client.VerifyCommitReachableAsync(
              1001,
              repository,
              sourceCommit,
              "refs/heads/main",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(new GitHubCommitReachability(true)))
          .Verifiable();

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      await SeedTenantAccessAsync(
          factory,
          cancellationToken);

      using var administratorClient = CreateAuthenticatedClient(
          factory,
          AdministratorGitHubUserId);
      using var viewerClient = CreateAuthenticatedClient(
          factory,
          ViewerGitHubUserId);
      var administratorSession = await DashboardTestHelpers.GetSessionAsync(
          administratorClient,
          cancellationToken);
      var viewerSession = await DashboardTestHelpers.GetSessionAsync(
          viewerClient,
          cancellationToken);

      using var createdResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              administratorClient,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
              administratorSession.AntiforgeryToken,
              CreateRequest(
                  "pitcrew-default",
                  ParseGuid("33333333-3333-3333-3333-333333333333")),
              cancellationToken);
      var created = await createdResponse.Content.ReadFromJsonAsync<
          ImageRecipeRegistrationResponse>(
              cancellationToken);
      var viewerListResponse = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations?limit=10",
          cancellationToken);
      var viewerExactResponse = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations/{created!.RegistrationId:D}",
          cancellationToken);
      var buildRequestId = ParseGuid(
          "45454545-4545-4545-4545-454545454545");
      var buildRequest = new RequestImageBuildRequest(
          buildRequestId,
          created.RegistrationId,
          created.Version,
          "refs/heads/main",
          sourceCommit,
          new Dictionary<string, JsonElement>(StringComparer.Ordinal)
          {
            ["channel"] = JsonSerializer.SerializeToElement("stable"),
          });
      using var administratorBuildResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              administratorClient,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests",
              administratorSession.AntiforgeryToken,
              buildRequest,
              cancellationToken);
      using var viewerBuildListResponse = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests?limit=10",
          cancellationToken);
      using var viewerBuildExactResponse = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests/{buildRequestId:D}",
          cancellationToken);
      using var viewerBuildCreateResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              viewerClient,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests",
              viewerSession.AntiforgeryToken,
              buildRequest with { RequestId = Guid.NewGuid() },
              cancellationToken);
      using var viewerCreateResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              viewerClient,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
              viewerSession.AntiforgeryToken,
              CreateRequest(
                  "pitcrew-viewer-forbidden",
                  ParseGuid("44444444-4444-4444-4444-444444444444")),
              cancellationToken);
      using var viewerDisableResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              viewerClient,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations/{created.RegistrationId:D}/disable",
              viewerSession.AntiforgeryToken,
              null,
              cancellationToken);
      using var administratorDisableResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              administratorClient,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations/{created.RegistrationId:D}/disable",
              administratorSession.AntiforgeryToken,
              null,
              cancellationToken);

      await Assert.That(administratorSession.Tenants.Single().Role)
          .IsEqualTo("administrator");
      await Assert.That(viewerSession.Tenants.Single().Role)
          .IsEqualTo("viewer");
      await Assert.That(createdResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Created);
      await Assert.That(viewerListResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(viewerExactResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(administratorBuildResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);
      await Assert.That(viewerBuildListResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(viewerBuildExactResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(viewerBuildCreateResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden);
      await Assert.That(viewerCreateResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden);
      await Assert.That(viewerDisableResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden);
      await Assert.That(administratorDisableResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.NoContent);
      clientMock.Verify(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()),
          Times.Once());
      clientMock.Verify(client => client.LoadWorkflowAsync(
              1001,
              repository,
              3001,
              It.IsAny<CancellationToken>()),
          Times.Once());
      clientMock.Verify(client => client.LoadWorkflowFileRevisionAsync(
              1001,
              repository,
              workflow.Path,
              "release/v1",
              It.IsAny<CancellationToken>()),
          Times.Once());
      clientMock.Verify(client => client.LoadWorkflowFileContentAsync(
              1001,
              repository,
              revision,
              It.IsAny<CancellationToken>()),
          Times.Once());
      clientMock.Verify(client => client.ResolveCommitAsync(
              1001,
              repository,
              sourceCommit,
              It.IsAny<CancellationToken>()),
          Times.Once());
      clientMock.Verify(client => client.VerifyCommitReachableAsync(
              1001,
              repository,
              sourceCommit,
              "refs/heads/main",
              It.IsAny<CancellationToken>()),
          Times.Once());
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Registration_Request_Maps_GitHub_Outcomes_And_Retry_Evidence(
      CancellationToken cancellationToken)
  {
    var scenarios = new[]
    {
      new GitHubFailureScenario(
          GitHubClientOutcomeKind.NotConfigured,
          HttpStatusCode.ServiceUnavailable,
          "github_image_integration_not_configured",
          null),
      new GitHubFailureScenario(
          GitHubClientOutcomeKind.NotFound,
          HttpStatusCode.NotFound,
          "github_repository_not_found",
          null),
      new GitHubFailureScenario(
          GitHubClientOutcomeKind.UnauthorizedOrForbidden,
          HttpStatusCode.Forbidden,
          "github_repository_forbidden",
          null),
      new GitHubFailureScenario(
          GitHubClientOutcomeKind.RateLimited,
          HttpStatusCode.TooManyRequests,
          "github_image_integration_rate_limited",
          "300"),
      new GitHubFailureScenario(
          GitHubClientOutcomeKind.TransientFailure,
          HttpStatusCode.ServiceUnavailable,
          "github_image_integration_unavailable",
          null),
      new GitHubFailureScenario(
          GitHubClientOutcomeKind.InvalidResponse,
          HttpStatusCode.ServiceUnavailable,
          "github_image_integration_unavailable",
          null),
      new GitHubFailureScenario(
          GitHubClientOutcomeKind.InvalidRequest,
          HttpStatusCode.BadRequest,
          "invalid_image_recipe_registration",
          null),
    };

    foreach (var scenario in scenarios)
    {
      var databasePath = DashboardTestHelpers.CreateDatabasePath();
      var now = new DateTimeOffset(
          2026,
          8,
          23,
          23,
          0,
          0,
          TimeSpan.Zero);
      try
      {
        using var configuration = new TestConfigurationScope(
            databasePath);
        var fakeTime = new FakeTimeProvider(now);
        var mocks = new MockRepository(MockBehavior.Strict);
        var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
        clientMock.Setup(client => client.LoadRepositoryAsync(
                1001,
                2001,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Failure<GitHubRepositoryIdentity>(
                scenario.Kind,
                now.AddMinutes(5)))
            .Verifiable();

        await using var factory = CreateFactory(
            fakeTime,
            clientMock.Object);
        using var client = factory.CreateClient();
        var session = await DashboardTestHelpers.GetSessionAsync(
            client,
            cancellationToken);
        using var response =
            await DashboardTestHelpers.PostAuthenticatedAsync(
                client,
                $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
                session.AntiforgeryToken,
                CreateRequest(
                    "pitcrew-default",
                    Guid.NewGuid()),
                cancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        await Assert.That(response.StatusCode)
            .IsEqualTo(scenario.StatusCode);
        await Assert.That(body).Contains(scenario.ErrorCode);
        await Assert.That(
                body.Contains(
                    "never-expose-this-detail",
                    StringComparison.Ordinal))
            .IsFalse()
            .Because("transport detail stays server-side");
        if (scenario.RetryAfter is null)
        {
          await Assert.That(response.Headers.Contains("Retry-After"))
              .IsFalse()
              .Because("only rate-limited responses carry retry evidence");
        }
        else
        {
          await Assert.That(
                  response.Headers.TryGetValues(
                      "Retry-After",
                      out var retryAfterValues))
              .IsTrue()
              .Because("rate-limited responses preserve bounded retry evidence");
          await Assert.That(retryAfterValues!.Single())
              .IsEqualTo(scenario.RetryAfter);
        }

        mocks.VerifyAll();
        clientMock.VerifyNoOtherCalls();
      }
      finally
      {
        DashboardTestHelpers.DeleteDatabase(databasePath);
      }
    }
  }

  [Test]
  public async Task Image_Recipe_Mutations_Require_Antiforgery_And_Are_Tenant_Scoped_Rate_Limited(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        0,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
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
          new string('c', 40),
          "release/v1");
      SetupSuccess(
          clientMock,
          repository,
          workflow,
          revision);

      await using var factory = CreateFactory(
          fakeTime,
          clientMock.Object);
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);

      using var missingAntiforgeryResponse = await client.PostAsJsonAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
          CreateRequest(
              "pitcrew-missing-antiforgery",
              ParseGuid("55555555-5555-5555-5555-555555555555")),
          cancellationToken);

      HttpStatusCode limitedStatus = default;
      string? retryAfter = null;
      var createdCount = 0;
      for (var requestIndex = 0;
          requestIndex < 25 &&
          limitedStatus != HttpStatusCode.TooManyRequests;
          requestIndex++)
      {
        using var response =
            await DashboardTestHelpers.PostAuthenticatedAsync(
                client,
                $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
                session.AntiforgeryToken,
                CreateRequest(
                    $"pitcrew-rate-{requestIndex}",
                    Guid.NewGuid()),
                cancellationToken);
        limitedStatus = response.StatusCode;
        if (limitedStatus == HttpStatusCode.TooManyRequests)
        {
          retryAfter = response.Headers.TryGetValues(
                  "Retry-After",
                  out var values)
              ? values.Single()
              : null;
          break;
        }

        createdCount++;
        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Created);
      }

      using var createTenant =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              "/api/tenants",
              session.AntiforgeryToken,
              new CreateTenantRequest(
                  "secondary",
                  "Secondary"),
              cancellationToken);
      using var otherTenant =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              "/api/tenants/secondary/images/v1/recipes/registrations",
              session.AntiforgeryToken,
              CreateRequest(
                  "pitcrew-secondary",
                  ParseGuid("66666666-6666-6666-6666-666666666666")),
              cancellationToken);
      fakeTime.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
      using var recovered =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
              session.AntiforgeryToken,
              CreateRequest(
                  "pitcrew-after-window",
                  ParseGuid("77777777-7777-7777-7777-777777777777")),
              cancellationToken);

      await Assert.That(missingAntiforgeryResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(limitedStatus)
          .IsEqualTo(HttpStatusCode.TooManyRequests);
      await Assert.That(retryAfter)
          .IsEqualTo("60");
      await Assert.That(createTenant.StatusCode)
          .IsEqualTo(HttpStatusCode.Created);
      await Assert.That(otherTenant.StatusCode)
          .IsEqualTo(HttpStatusCode.Created);
      await Assert.That(recovered.StatusCode)
          .IsEqualTo(HttpStatusCode.Created);
      clientMock.Verify(client => client.LoadRepositoryAsync(
              1001,
              2001,
              It.IsAny<CancellationToken>()),
          Times.Exactly(createdCount + 2));
      clientMock.Verify(client => client.LoadWorkflowAsync(
              1001,
              repository,
              3001,
              It.IsAny<CancellationToken>()),
          Times.Exactly(createdCount + 2));
      clientMock.Verify(client => client.LoadWorkflowFileRevisionAsync(
              1001,
              repository,
              workflow.Path,
              "release/v1",
              It.IsAny<CancellationToken>()),
          Times.Exactly(createdCount + 2));
      clientMock.Verify(client => client.LoadWorkflowFileContentAsync(
              1001,
              repository,
              revision,
              It.IsAny<CancellationToken>()),
          Times.Exactly(createdCount + 2));
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Administrator_Creates_Idempotent_Exact_Build_Request_Without_Dispatch(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        1,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
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
      SetupSuccess(clientMock, repository, workflow, revision);
      var sourceCommit = new string('a', 40);
      clientMock.Setup(client => client.ResolveCommitAsync(
              1001,
              repository,
              sourceCommit,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(new GitHubCommitIdentity(sourceCommit)))
          .Verifiable();
      clientMock.Setup(client => client.VerifyCommitReachableAsync(
              1001,
              repository,
              sourceCommit,
              "refs/heads/main",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(Success(new GitHubCommitReachability(true)))
          .Verifiable();

      await using var factory = CreateFactory(fakeTime, clientMock.Object);
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var registrationId = ParseGuid(
          "66666666-6666-6666-6666-666666666666");
      using var registrationResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/v1/recipes/registrations",
              session.AntiforgeryToken,
              CreateRequest("pitcrew-default", registrationId),
              cancellationToken);
      var requestId = ParseGuid(
          "77777777-7777-7777-7777-777777777777");
      var request = new RequestImageBuildRequest(
          requestId,
          registrationId,
          1,
          "refs/heads/main",
          sourceCommit,
          new Dictionary<string, JsonElement>(StringComparer.Ordinal)
          {
            ["channel"] = JsonSerializer.SerializeToElement("stable"),
            ["enableCache"] = JsonSerializer.SerializeToElement(true),
          });
      using var createdResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests",
              session.AntiforgeryToken,
              request,
              cancellationToken);
      using var replayResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests",
              session.AntiforgeryToken,
              request,
              cancellationToken);
      using var conflictResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests",
              session.AntiforgeryToken,
              request with { SourceCommit = new string('b', 40) },
              cancellationToken);
      var exact = await client.GetFromJsonAsync<ImageBuildRequestResponse>(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests/{requestId:D}",
          cancellationToken);
      var list = await client.GetFromJsonAsync<ImageBuildRequestListResponse>(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/requests?limit=1",
          cancellationToken);

      await Assert.That(registrationResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Created);
      await Assert.That(createdResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);
      await Assert.That(replayResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(conflictResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Conflict);
      await Assert.That(exact).IsNotNull();
      await Assert.That(exact!.RequestId).IsEqualTo(requestId);
      await Assert.That(exact.SourceRef).IsEqualTo("refs/heads/main");
      await Assert.That(exact.Status).IsEqualTo("requested");
      await Assert.That(list).IsNotNull();
      await Assert.That(list!.Requests).HasSingleItem();
      await Assert.That(list.Truncated).IsFalse()
          .Because("one durable request fits the requested bound");
      clientMock.Verify(client => client.ResolveCommitAsync(
              1001,
              repository,
              sourceCommit,
              It.IsAny<CancellationToken>()),
          Times.Once());
      clientMock.Verify(client => client.VerifyCommitReachableAsync(
              1001,
              repository,
              sourceCommit,
              "refs/heads/main",
              It.IsAny<CancellationToken>()),
          Times.Once());
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Campaign_Endpoints_Freeze_Approve_And_Dispatch_One_Profile_Command(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    var now = new DateTimeOffset(
        2026,
        9,
        17,
        12,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          SystemAdministratorGitHubUserId,
          "Production");
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      await using var factory = CreateFactory(fakeTime, clientMock.Object);
      await SeedTenantAccessAsync(factory, cancellationToken);
      var candidateId = await SeedCandidateAsync(
          factory.Services,
          now,
          cancellationToken);
      var (nodeId, profileId) = await SeedRolloutCapableNodeAsync(
          factory.Services,
          now,
          cancellationToken);
      using var administratorClient = CreateAuthenticatedClient(
          factory,
          AdministratorGitHubUserId);
      using var viewerClient = CreateAuthenticatedClient(
          factory,
          ViewerGitHubUserId);
      var administratorSession = await DashboardTestHelpers.GetSessionAsync(
          administratorClient,
          cancellationToken);
      var viewerSession = await DashboardTestHelpers.GetSessionAsync(
          viewerClient,
          cancellationToken);

      using var viewerCreate = await PostImageMutationAsync(
          viewerClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/campaigns",
          viewerSession.AntiforgeryToken,
          "viewer-campaign-create-1",
          new CreateImageRolloutCampaignRequest(candidateId),
          cancellationToken);
      using var createdResponse = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/campaigns",
          administratorSession.AntiforgeryToken,
          "admin-campaign-create-1",
          new CreateImageRolloutCampaignRequest(candidateId),
          cancellationToken);
      var created = await createdResponse.Content
          .ReadFromJsonAsync<ImageRolloutCampaignResponse>(
              cancellationToken);
      var target = created!.Targets.Single();
      using var invalidConfigurationResponse = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/campaigns/{created.CampaignId:D}/configure",
          administratorSession.AntiforgeryToken,
          "admin-campaign-configure-invalid",
          new ConfigureImageRolloutCampaignRequest(
              null,
              ImageRolloutCampaignConfiguration.MaximumWaveSize + 1,
              created.Revision,
              created.TargetSetHash),
          cancellationToken);
      using var invalidConfigurationBody = JsonDocument.Parse(
          await invalidConfigurationResponse.Content.ReadAsStringAsync(
              cancellationToken));
      using var configuredResponse = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/campaigns/{created.CampaignId:D}/configure",
          administratorSession.AntiforgeryToken,
          "admin-campaign-configure-1",
          new ConfigureImageRolloutCampaignRequest(
              null,
              10,
              created.Revision,
              created.TargetSetHash),
          cancellationToken);
      var configured = await configuredResponse.Content
          .ReadFromJsonAsync<ImageRolloutCampaignResponse>(
              cancellationToken);
      using var approvedResponse = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/campaigns/{created.CampaignId:D}/waves/0/approve",
          administratorSession.AntiforgeryToken,
          "admin-campaign-approve-1",
          new ApproveImageRolloutCampaignWaveRequest(
              configured!.Revision,
              configured.TargetSetHash),
          cancellationToken);
      var approved = await approvedResponse.Content
          .ReadFromJsonAsync<ImageRolloutCampaignResponse>(
              cancellationToken);
      var processed = await factory.Services
          .GetRequiredService<IImageRolloutCampaignProcessor>()
          .ProcessOnceAsync(cancellationToken);
      using var detailResponse = await administratorClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/campaigns/{created.CampaignId:D}",
          cancellationToken);
      var detail = await detailResponse.Content
          .ReadFromJsonAsync<ImageRolloutCampaignResponse>(
              cancellationToken);
      var profileControl = await factory.Services
          .GetRequiredService<IImageRolloutCommandStore>()
          .GetProfileControlOrNullAsync(
              DashboardTestHelpers.TenantId,
              nodeId,
              profileId,
              120,
              cancellationToken);

      await Assert.That(viewerCreate.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden);
      await Assert.That(createdResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Created);
      await Assert.That(created.Status).IsEqualTo("draft");
      await Assert.That(created.Targets).HasSingleItem();
      await Assert.That(target.NodeId).IsEqualTo(nodeId);
      await Assert.That(target.ProfileId).IsEqualTo(profileId);
      await Assert.That(target.ExclusionCategory).IsNull();
      await Assert.That(invalidConfigurationResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(
              invalidConfigurationBody.RootElement
                  .GetProperty("error")
                  .GetProperty("code")
                  .GetString())
          .IsEqualTo("invalid_image_campaign_configuration");
      await Assert.That(configuredResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(configured.Status).IsEqualTo("awaiting-approval");
      await Assert.That(configured.Waves).HasSingleItem();
      await Assert.That(approvedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(approved!.Status).IsEqualTo("running");
      await Assert.That(processed).IsEqualTo(1);
      await Assert.That(detailResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(detail).IsNotNull();
      await Assert.That(detail!.Targets[0].CommandId).IsNotNull();
      await Assert.That(profileControl).IsNotNull();
      await Assert.That(profileControl!.LatestCommand).IsNotNull();
      await Assert.That(profileControl.LatestCommand!.CommandId)
          .IsEqualTo(detail.Targets[0].CommandId);
      await Assert.That(profileControl.LatestCommand.CandidateId)
          .IsEqualTo(candidateId);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Rollout_Endpoints_Enforce_Authentication_Roles_And_Antiforgery(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    var now = new DateTimeOffset(
        2026,
        9,
        15,
        12,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          SystemAdministratorGitHubUserId,
          "Production");
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      await using var factory = CreateFactory(fakeTime, clientMock.Object);
      await SeedTenantAccessAsync(factory, cancellationToken);
      var candidateId = await SeedCandidateAsync(
          factory.Services,
          now,
          cancellationToken);
      var (nodeId, profileId) = await SeedRolloutCapableNodeAsync(
          factory.Services,
          now,
          cancellationToken);

      using var administratorClient = CreateAuthenticatedClient(
          factory,
          AdministratorGitHubUserId);
      using var viewerClient = CreateAuthenticatedClient(
          factory,
          ViewerGitHubUserId);
      using var anonymousClient = factory.CreateClient(
          new WebApplicationFactoryClientOptions
          {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(
                "https://pitcrew.example.com",
                UriKind.Absolute),
          });
      var administratorSession = await DashboardTestHelpers.GetSessionAsync(
          administratorClient,
          cancellationToken);
      var viewerSession = await DashboardTestHelpers.GetSessionAsync(
          viewerClient,
          cancellationToken);

      using var anonymousGet = await anonymousClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts/{nodeId:D}/{profileId}",
          cancellationToken);
      using var anonymousPost = await anonymousClient.PostAsJsonAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          CreateRolloutRequest(nodeId, profileId, candidateId),
          cancellationToken);

      using var viewerGet = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts/{nodeId:D}/{profileId}",
          cancellationToken);
      using var viewerPost = await PostImageMutationAsync(
          viewerClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          viewerSession.AntiforgeryToken,
          "viewer-post-attempt-01",
          CreateRolloutRequest(nodeId, profileId, candidateId),
          cancellationToken);

      using var missingAntiforgeryRequest = new HttpRequestMessage(
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts")
      {
        Content = JsonContent.Create(
            CreateRolloutRequest(nodeId, profileId, candidateId)),
      };
      missingAntiforgeryRequest.Headers.Add(
          RolloutIdempotencyKeyHeader,
          "admin-missing-antiforgery-1");
      using var missingAntiforgery = await administratorClient.SendAsync(
          missingAntiforgeryRequest,
          cancellationToken);

      using var invalidAntiforgery = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          "not-a-real-token",
          "admin-invalid-antiforgery-1",
          CreateRolloutRequest(nodeId, profileId, candidateId),
          cancellationToken);

      using var missingIdempotencyRequest = new HttpRequestMessage(
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts")
      {
        Content = JsonContent.Create(
            CreateRolloutRequest(nodeId, profileId, candidateId)),
      };
      missingIdempotencyRequest.Headers.Add(
          DashboardTestHelpers.AntiforgeryHeader,
          administratorSession.AntiforgeryToken);
      using var missingIdempotency = await administratorClient.SendAsync(
          missingIdempotencyRequest,
          cancellationToken);

      using var invalidIdempotency = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          administratorSession.AntiforgeryToken,
          "bad key with spaces",
          CreateRolloutRequest(nodeId, profileId, candidateId),
          cancellationToken);

      using var administratorPost = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          administratorSession.AntiforgeryToken,
          "admin-first-success-01",
          CreateRolloutRequest(nodeId, profileId, candidateId),
          cancellationToken);

      using var crossTenantGet = await administratorClient.GetAsync(
          $"/api/tenants/other-tenant/images/profile-rollouts/{nodeId:D}/{profileId}",
          cancellationToken);

      await Assert.That(anonymousGet.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
      await Assert.That(anonymousPost.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
      await Assert.That(viewerGet.StatusCode).IsEqualTo(HttpStatusCode.OK);
      var viewerBody = await viewerGet.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);
      await Assert.That(
              viewerBody.GetProperty("nodeId").GetGuid())
          .IsEqualTo(nodeId);
      await Assert.That(
              viewerBody.GetProperty("profileId").GetString())
          .IsEqualTo(profileId);
      await Assert.That(viewerBody.TryGetProperty("reportJson", out _))
          .IsFalse()
          .Because("bounded response never surfaces raw connector reportJson");
      await Assert.That(viewerPost.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden);
      await Assert.That(missingAntiforgery.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(invalidAntiforgery.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(missingIdempotency.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      var missingIdempotencyBody = await missingIdempotency.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);
      await Assert.That(
              missingIdempotencyBody.GetProperty("error")
                  .GetProperty("code").GetString())
          .IsEqualTo("invalid_image_rollout_idempotency_key");
      await Assert.That(invalidIdempotency.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      var invalidIdempotencyBody = await invalidIdempotency.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);
      await Assert.That(
              invalidIdempotencyBody.GetProperty("error")
                  .GetProperty("code").GetString())
          .IsEqualTo("invalid_image_rollout_idempotency_key");
      await Assert.That(administratorPost.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);
      var administratorBody = await administratorPost.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);
      await Assert.That(
              administratorBody.GetProperty("status").GetString())
          .IsEqualTo("queued");
      var commandLocation = administratorBody
          .GetProperty("statusLocation").GetString()!;
      await Assert.That(commandLocation)
          .IsEqualTo(
              $"/api/tenants/{DashboardTestHelpers.TenantId}"
              + $"/images/profile-rollouts/{nodeId:D}/{profileId}");
      var administratorHeader = administratorPost.Headers.Location?.ToString();
      await Assert.That(administratorHeader).IsEqualTo(commandLocation);
      await Assert.That(crossTenantGet.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden)
          .Because(
              "cross-tenant access is denied by the TenantViewer policy "
              + "before the handler runs, hiding the resource without a 404");

      fakeTime.Advance(TimeSpan.FromSeconds(91));
      using var staleViewerGet = await viewerClient.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts/{nodeId:D}/{profileId}",
          cancellationToken);
      var staleViewerBody = await staleViewerGet.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);
      await Assert.That(staleViewerBody.GetProperty(
              "observedStateAgeSeconds").GetInt32())
          .IsEqualTo(121);
      await Assert.That(staleViewerBody.GetProperty(
              "observedStateFresh").GetBoolean())
          .IsFalse()
          .Because(
              "capability freshness must include elapsed dashboard time "
              + "since the connector last synchronized");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  [Arguments(
      "recipe",
      HttpStatusCode.Forbidden,
      "image_rollout_recipe_not_allowed")]
  [Arguments(
      "registry",
      HttpStatusCode.Forbidden,
      "image_rollout_registry_not_allowed")]
  [Arguments(
      "topology",
      HttpStatusCode.Conflict,
      "image_rollout_unsupported_topology")]
  [Arguments(
      "architecture",
      HttpStatusCode.Conflict,
      "image_rollout_architecture_mismatch")]
  [Arguments(
      "stale-fence",
      HttpStatusCode.Conflict,
      "image_rollout_stale_fence")]
  public async Task Rollout_Post_Preserves_Typed_Rejection_Contracts(
      string scenario,
      HttpStatusCode expectedStatus,
      string expectedCode,
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    var now = new DateTimeOffset(
        2026,
        9,
        16,
        6,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          SystemAdministratorGitHubUserId,
          "Production");
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      await using var factory = CreateFactory(fakeTime, clientMock.Object);
      await SeedTenantAccessAsync(factory, cancellationToken);
      var candidateId = await SeedCandidateAsync(
          factory.Services,
          now,
          cancellationToken);
      IReadOnlyList<string>? allowedRecipeIds = scenario == "recipe"
          ? ["other-recipe"]
          : null;
      var localFailureCategory = scenario switch
      {
        "registry" => "registry-not-allowed",
        "topology" => "unsupported-topology",
        _ => null,
      };
      var (nodeId, profileId) = await SeedRolloutCapableNodeAsync(
          factory.Services,
          now,
          cancellationToken,
          allowedRecipeIds,
          rolloutAllowed: localFailureCategory is null,
          localFailureCategory: localFailureCategory,
          architecture: scenario == "architecture"
              ? "linux/arm64"
              : "linux/amd64");
      var rolloutRequest = CreateRolloutRequest(
          nodeId,
          profileId,
          candidateId);
      if (scenario == "stale-fence")
      {
        rolloutRequest = rolloutRequest with
        {
          ExpectedStaticFingerprint = new string('f', 64),
        };
      }
      using var administratorClient = CreateAuthenticatedClient(
          factory,
          AdministratorGitHubUserId);
      var administratorSession = await DashboardTestHelpers.GetSessionAsync(
          administratorClient,
          cancellationToken);

      using var response = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          administratorSession.AntiforgeryToken,
          $"typed-rejection-{scenario}",
          rolloutRequest,
          cancellationToken);
      var body = await response.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);

      await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
      await Assert.That(
              body.GetProperty("error").GetProperty("code").GetString())
          .IsEqualTo(expectedCode);
      mocks.VerifyAll();
      clientMock.VerifyNoOtherCalls();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Rollout_Post_Handles_Idempotency_Replay_Conflict_And_Rate_Limit(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    var now = new DateTimeOffset(
        2026,
        9,
        16,
        12,
        0,
        0,
        TimeSpan.Zero);
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          SystemAdministratorGitHubUserId,
          "Production");
      var fakeTime = new FakeTimeProvider(now);
      var mocks = new MockRepository(MockBehavior.Strict);
      var clientMock = mocks.Create<IGitHubImageWorkflowClient>();
      await using var factory = CreateFactory(fakeTime, clientMock.Object);
      await SeedTenantAccessAsync(factory, cancellationToken);
      var candidateId = await SeedCandidateAsync(
          factory.Services,
          now,
          cancellationToken);
      var otherCandidateId = await SeedSecondaryCandidateAsync(
          factory.Services,
          now,
          cancellationToken);
      var (nodeId, profileId) = await SeedRolloutCapableNodeAsync(
          factory.Services,
          now,
          cancellationToken);
      using var administratorClient = CreateAuthenticatedClient(
          factory,
          AdministratorGitHubUserId);
      var administratorSession = await DashboardTestHelpers.GetSessionAsync(
          administratorClient,
          cancellationToken);

      using var initialPost = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          administratorSession.AntiforgeryToken,
          "primary-key-alpha-01",
          CreateRolloutRequest(nodeId, profileId, candidateId),
          cancellationToken);
      var initialBody = await initialPost.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);
      var initialCommandId = initialBody.GetProperty("commandId").GetGuid();

      using var replayPost = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          administratorSession.AntiforgeryToken,
          "primary-key-alpha-01",
          CreateRolloutRequest(nodeId, profileId, candidateId),
          cancellationToken);
      var replayBody = await replayPost.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);

      using var conflictPost = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          administratorSession.AntiforgeryToken,
          "primary-key-alpha-01",
          CreateRolloutRequest(nodeId, profileId, otherCandidateId),
          cancellationToken);
      var conflictBody = await conflictPost.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);

      using var cooldownPost = await PostImageMutationAsync(
          administratorClient,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
          administratorSession.AntiforgeryToken,
          "primary-key-beta-02",
          CreateRolloutRequest(nodeId, profileId, candidateId),
          cancellationToken);
      var cooldownBody = await cooldownPost.Content
          .ReadFromJsonAsync<JsonElement>(cancellationToken);

      HttpStatusCode limitedStatus = default;
      string? retryAfter = null;
      for (var requestIndex = 0;
          requestIndex < 30 &&
          limitedStatus != HttpStatusCode.TooManyRequests;
          requestIndex++)
      {
        using var rateResponse = await PostImageMutationAsync(
            administratorClient,
            $"/api/tenants/{DashboardTestHelpers.TenantId}/images/profile-rollouts",
            administratorSession.AntiforgeryToken,
            $"rate-limit-probe-key-{requestIndex:D2}",
            CreateRolloutRequest(nodeId, profileId, candidateId),
            cancellationToken);
        limitedStatus = rateResponse.StatusCode;
        if (limitedStatus == HttpStatusCode.TooManyRequests)
        {
          retryAfter = rateResponse.Headers.TryGetValues(
                  "Retry-After",
                  out var values)
              ? values.Single()
              : null;
          break;
        }
      }

      await Assert.That(initialPost.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);
      await Assert.That(replayPost.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted)
          .Because("replay must be idempotent and return 202 like the original");
      await Assert.That(replayBody.GetProperty("commandId").GetGuid())
          .IsEqualTo(initialCommandId);
      await Assert.That(replayBody.GetProperty("status").GetString())
          .IsEqualTo("queued");
      var initialLocation = initialBody
          .GetProperty("statusLocation").GetString();
      var replayLocation = replayBody
          .GetProperty("statusLocation").GetString();
      await Assert.That(replayLocation).IsEqualTo(initialLocation);
      await Assert.That(replayPost.Headers.Location?.ToString())
          .IsEqualTo(initialPost.Headers.Location?.ToString());
      await Assert.That(conflictPost.StatusCode)
          .IsEqualTo(HttpStatusCode.Conflict);
      await Assert.That(
              conflictBody.GetProperty("error")
                  .GetProperty("code").GetString())
          .IsEqualTo("image_rollout_idempotency_key_conflict");
      await Assert.That(cooldownPost.StatusCode)
          .IsNotEqualTo(HttpStatusCode.Accepted)
          .Because("cooldown or profile conflict is never an idempotent replay");
      var cooldownCode = cooldownBody.GetProperty("error")
          .GetProperty("code").GetString();
      await Assert.That(cooldownCode)
          .IsNotEqualTo("image_rollout_idempotency_key_conflict")
          .Because("a fresh key with same authority is not an idempotency reuse conflict");
      await Assert.That(limitedStatus)
          .IsEqualTo(HttpStatusCode.TooManyRequests);
      await Assert.That(retryAfter).IsEqualTo("60");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  private const string RolloutIdempotencyKeyHeader = "Idempotency-Key";

  private static async Task<HttpResponseMessage> PostImageMutationAsync(
      HttpClient client,
      string path,
      string antiforgeryToken,
      string idempotencyKey,
      object body,
      CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, path)
    {
      Content = JsonContent.Create(body),
    };
    request.Headers.Add(
        DashboardTestHelpers.AntiforgeryHeader,
        antiforgeryToken);
    request.Headers.Add(RolloutIdempotencyKeyHeader, idempotencyKey);
    return await client.SendAsync(request, cancellationToken);
  }

  private static RollOutProfileImageRequestBody CreateRolloutRequest(
      Guid nodeId,
      string profileId,
      Guid candidateId) =>
      new(
          nodeId,
          profileId,
          candidateId,
          "ghcr.io/example/runner:main",
          "sha256:1111111111111111111111111111111111111111111111111111111111111111",
          "sha256:2222222222222222222222222222222222222222222222222222222222222222",
          RolloutCurrentWorkerRevision,
          RolloutStaticFingerprint,
          RolloutPreservedFingerprint,
          RolloutRoutingFingerprint,
          7,
          RolloutDesiredStateHash);

  private const string RolloutCurrentWorkerRevision =
      "a3b4c5d6e7f80912132435465768798a9bacbdcedfe0f102030405060708090a";
  private const string RolloutStaticFingerprint =
      "a1b2c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff0";
  private const string RolloutPreservedFingerprint =
      "b2c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001";
  private const string RolloutRoutingFingerprint =
      "c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff00112";
  private const string RolloutDesiredStateHash =
      "e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001a1b2c3";
  private const string RolloutRecipeId = "pitcrew-default";

  private static async Task<(Guid NodeId, string ProfileId)>
      SeedRolloutCapableNodeAsync(
          IServiceProvider services,
          DateTimeOffset now,
          CancellationToken cancellationToken,
          IReadOnlyList<string>? allowedRecipeIds = null,
          bool rolloutAllowed = true,
          string? localFailureCategory = null,
          string architecture = "linux/amd64")
  {
    var fleetStore = services.GetRequiredService<IFleetStore>();
    var rolloutStore =
        services.GetRequiredService<IImageRolloutCommandStore>();
    const string enrollmentCodeHash = "rollout-http-code-hash";
    await fleetStore.CreateEnrollmentCodeAsync(
        Guid.NewGuid(),
        DashboardTestHelpers.TenantId,
        enrollmentCodeHash,
        "rollout-http-connector",
        AdministratorGitHubUserId,
        now,
        now.AddMinutes(10),
        cancellationToken);
    var enrollment = await fleetStore.RedeemEnrollmentCodeAsync(
        enrollmentCodeHash,
        "connector-instance",
        "Connector",
        "credential-hash",
        now,
        cancellationToken);
    var nodeId = enrollment.NodeId ??
        throw new InvalidOperationException(
            "Failed to enroll HTTP integration test node.");
    var transactionFactory =
        services.GetRequiredService<IFleetStorageTransactionFactory>();
    await using (var transaction = await transactionFactory.BeginAsync(
        cancellationToken))
    {
      await fleetStore.ApplySyncAsync(
          transaction,
          nodeId,
          "0.14.0",
          now,
          [],
          new HashSet<string>(StringComparer.OrdinalIgnoreCase),
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    }
    var capability = new ImageRolloutOperatorCapability(
        [
            new ImageRolloutOperatorProfile(
                "default",
                architecture,
                "ghcr.io/example/runner:main",
                "sha256:1111111111111111111111111111111111111111111111111111111111111111",
                "sha256:2222222222222222222222222222222222222222222222222222222222222222",
                RolloutCurrentWorkerRevision,
                RolloutStaticFingerprint,
                RolloutPreservedFingerprint,
                RolloutRoutingFingerprint,
                7,
                RolloutDesiredStateHash,
                allowedRecipeIds ?? [RolloutRecipeId],
                rolloutAllowed,
                true,
                localFailureCategory,
                false,
                30,
                600,
                1800,
                "current",
                4,
                0),
        ]);
    await rolloutStore.ApplyConnectorSyncAsync(
        nodeId,
        capability,
        null,
        null,
        now,
        now.AddMinutes(1),
        cancellationToken);
    return (nodeId, "default");
  }

  private static async Task<Guid> SeedSecondaryCandidateAsync(
      IServiceProvider services,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    var registrationId = Guid.Parse(
        "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
        CultureInfo.InvariantCulture);
    var store = services.GetRequiredService<IImageCandidateStore>();
    var requestId = Guid.Parse(
        "bbbbbbbb-1111-2222-3333-555555555555",
        CultureInfo.InvariantCulture);
    const string inputs = "{}";
    var request = new ImageBuildRequest(
        DashboardTestHelpers.TenantId,
        requestId,
        registrationId,
        1,
        "pitcrew-default",
        "ncosentino/pitcrew-dashboard",
        new string('f', 40),
        inputs,
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(inputs))),
        AdministratorGitHubUserId,
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
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Requested,
            ImageBuildRequestStatus.Dispatching,
            null,
            null,
            null,
            null,
            now.AddMinutes(1)),
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Dispatching,
            ImageBuildRequestStatus.Building,
            8001,
            "https://github.com/ncosentino/pitcrew-dashboard/actions/runs/8001",
            null,
            null,
            now.AddMinutes(2)),
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Building,
            ImageBuildRequestStatus.Qualifying,
            8001,
            "https://github.com/ncosentino/pitcrew-dashboard/actions/runs/8001",
            null,
            null,
            now.AddMinutes(3)),
        cancellationToken);
    const string report = """{"schemaVersion":1,"status":"ready"}""";
    var candidate = new ReadyImageCandidate(
        requestId,
        request.TenantId,
        request.RequestId,
        request.RecipeId,
        request.SourceRepository,
        request.SourceCommit,
        8001,
        9001,
        "pitcrew-image-candidate",
        $"sha256:{new string('a', 64)}",
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(report))),
        report,
        "ghcr.io/ncosentino/pitcrew-dashboard:secondary",
        ImageCandidatePlatform.LinuxAmd64,
        ImageCandidateOutputMode.Registry,
        now.AddMinutes(4),
        now.AddMinutes(5),
        $"sha256:{new string('b', 64)}",
        $"ghcr.io/ncosentino/pitcrew-dashboard@sha256:{new string('b', 64)}");
    var qualifications = new[]
    {
      ImageCandidateQualificationName.ImageBuild,
      ImageCandidateQualificationName.BuildKitDigest,
      ImageCandidateQualificationName.RegistryDigest,
      ImageCandidateQualificationName.BuilderCleanup,
    }.Select(name => new ImageCandidateQualification(
        candidate.CandidateId,
        name,
        ImageCandidateQualificationStatus.Passed))
        .ToArray();
    var storeResult = await store.StoreCandidateAsync(
        request.TenantId,
        candidate,
        qualifications,
        cancellationToken);
    if (storeResult != ImageCandidateMutationResult.Succeeded &&
        storeResult != ImageCandidateMutationResult.Unchanged)
    {
      throw new InvalidOperationException(
          $"Secondary candidate seed failed with {storeResult}.");
    }
    return candidate.CandidateId;
  }

  private static async Task SeedTenantAccessAsync(
      WebApplicationFactory<Program> factory,
      CancellationToken cancellationToken)
  {
    using var systemAdministratorClient = CreateAuthenticatedClient(
        factory,
        SystemAdministratorGitHubUserId);
    using var administratorClient = CreateAuthenticatedClient(
        factory,
        AdministratorGitHubUserId);
    using var viewerClient = CreateAuthenticatedClient(
        factory,
        ViewerGitHubUserId);
    var systemAdministratorSession = await DashboardTestHelpers.GetSessionAsync(
        systemAdministratorClient,
        cancellationToken);
    await DashboardTestHelpers.GetSessionAsync(
        administratorClient,
        cancellationToken);
    await DashboardTestHelpers.GetSessionAsync(
        viewerClient,
        cancellationToken);
    using var createTenantResponse =
        await DashboardTestHelpers.PostAuthenticatedAsync(
            systemAdministratorClient,
            "/api/tenants",
            systemAdministratorSession.AntiforgeryToken,
            new CreateTenantRequest(
                DashboardTestHelpers.TenantId,
                "Local"),
            cancellationToken);
    await Assert.That(createTenantResponse.StatusCode)
        .IsEqualTo(HttpStatusCode.Created);

    using var administratorMembershipResponse =
        await PutAuthenticatedAsync(
            systemAdministratorClient,
            $"/api/tenants/{DashboardTestHelpers.TenantId}/members/{AdministratorGitHubUserId}",
            systemAdministratorSession.AntiforgeryToken,
            new SetTenantMembershipRequest("administrator"),
            cancellationToken);
    using var viewerMembershipResponse =
        await PutAuthenticatedAsync(
            systemAdministratorClient,
            $"/api/tenants/{DashboardTestHelpers.TenantId}/members/{ViewerGitHubUserId}",
            systemAdministratorSession.AntiforgeryToken,
            new SetTenantMembershipRequest("viewer"),
            cancellationToken);

    await Assert.That(administratorMembershipResponse.StatusCode)
        .IsEqualTo(HttpStatusCode.NoContent);
    await Assert.That(viewerMembershipResponse.StatusCode)
        .IsEqualTo(HttpStatusCode.NoContent);
  }

  private static async Task<HttpResponseMessage> PutAuthenticatedAsync(
      HttpClient client,
      string path,
      string antiforgeryToken,
      object body,
      CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Put,
        path)
    {
      Content = JsonContent.Create(body),
    };
    request.Headers.Add(
        DashboardTestHelpers.AntiforgeryHeader,
        antiforgeryToken);
    return await client.SendAsync(
        request,
        cancellationToken);
  }

  private static HttpClient CreateAuthenticatedClient(
      WebApplicationFactory<Program> factory,
      string githubUserId)
  {
    var client = factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
          AllowAutoRedirect = false,
          BaseAddress = new Uri(
              "https://pitcrew.example.com",
              UriKind.Absolute),
        });
    AddAuthenticationCookie(
        factory.Services,
        client,
        githubUserId);
    return client;
  }

  private static void SetupSuccess(
      Mock<IGitHubImageWorkflowClient> clientMock,
      GitHubRepositoryIdentity repository,
      GitHubWorkflowIdentity workflow,
      GitHubWorkflowFileRevision revision)
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
        .ReturnsAsync(Success(new GitHubWorkflowFileContent(
            revision.Path,
            revision.BlobSha,
            revision.Reference,
            CreateValidWorkflowYaml())))
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

  private static RegisterImageRecipeRequest CreateRequest(
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

  private static Guid ParseGuid(string value) =>
      Guid.Parse(
          value,
          CultureInfo.InvariantCulture);

  private static GitHubClientOutcome<T> Success<T>(T value) =>
      new(
          GitHubClientOutcomeKind.Success,
          value,
          null,
          null);

  private static GitHubClientOutcome<T> Failure<T>(
      GitHubClientOutcomeKind kind,
      DateTimeOffset retryAt) =>
      new(
          kind,
          default,
          kind == GitHubClientOutcomeKind.RateLimited
              ? retryAt
              : null,
          "never-expose-this-detail");

  private static void AddAuthenticationCookie(
      IServiceProvider services,
      HttpClient client,
      string githubUserId)
  {
    var cookieOptions = services
        .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
        .Get(DashboardAuthenticationSchemes.Cookie);
    var principal = new ClaimsPrincipal(
        new ClaimsIdentity(
            [
                new Claim(PitCrewClaimTypes.GitHubUserId, githubUserId),
                new Claim(PitCrewClaimTypes.GitHubLogin, $"user-{githubUserId}"),
                new Claim(ClaimTypes.NameIdentifier, githubUserId),
                new Claim(ClaimTypes.Name, $"User {githubUserId}"),
            ],
            DashboardAuthenticationSchemes.Cookie));
    var ticket = new AuthenticationTicket(
        principal,
        DashboardAuthenticationSchemes.Cookie);
    var protectedTicket = cookieOptions.TicketDataFormat.Protect(ticket);
    client.DefaultRequestHeaders.Add(
        "Cookie",
        $"{cookieOptions.Cookie.Name}={protectedTicket}");
  }

  private static async Task<Guid> SeedCandidateAsync(
      IServiceProvider services,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    var accessStore = services.GetRequiredService<IAccessStore>();
    await accessStore.EnsureTenantOwnerAsync(
        "tenant-b",
        "Tenant B",
        new DashboardUser(
            ViewerGitHubUserId,
            "viewer",
            "Viewer",
            null),
        now,
        cancellationToken);
    var store = services.GetRequiredService<IImageCandidateStore>();
    var registration = new ImageRecipeRegistration(
        DashboardTestHelpers.TenantId,
        Guid.Parse(
            "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
            CultureInfo.InvariantCulture),
        1,
        1001,
        2001,
        3001,
        "ncosentino",
        "pitcrew-dashboard",
        ".github/workflows/image-candidate.yml",
        new string('a', 40),
        "release/v1",
        "pitcrew-default",
        1,
        """{"allowedSourceRefs":["refs/heads/main"]}""",
        """{"type":"object","additionalProperties":false}""",
        AdministratorGitHubUserId,
        now,
        null,
        null);
    await store.CreateRecipeVersionAsync(registration, cancellationToken);
    var requestId = Guid.Parse(
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
        CultureInfo.InvariantCulture);
    const string inputs = "{}";
    var request = new ImageBuildRequest(
        DashboardTestHelpers.TenantId,
        requestId,
        registration.RegistrationId,
        registration.Version,
        registration.RecipeId,
        "ncosentino/pitcrew-dashboard",
        new string('b', 40),
        inputs,
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(inputs))),
        AdministratorGitHubUserId,
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
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Requested,
            ImageBuildRequestStatus.Dispatching,
            null,
            null,
            null,
            null,
            now.AddMinutes(1)),
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Dispatching,
            ImageBuildRequestStatus.Building,
            7001,
            "https://github.com/ncosentino/pitcrew-dashboard/actions/runs/7001",
            null,
            null,
            now.AddMinutes(2)),
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Building,
            ImageBuildRequestStatus.Qualifying,
            7001,
            "https://github.com/ncosentino/pitcrew-dashboard/actions/runs/7001",
            null,
            null,
            now.AddMinutes(3)),
        cancellationToken);
    const string report = """{"schemaVersion":1,"status":"ready"}""";
    var candidate = new ReadyImageCandidate(
        requestId,
        request.TenantId,
        request.RequestId,
        request.RecipeId,
        request.SourceRepository,
        request.SourceCommit,
        7001,
        8001,
        "pitcrew-image-candidate",
        $"sha256:{new string('c', 64)}",
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(report))),
        report,
        "ghcr.io/ncosentino/pitcrew-dashboard:test",
        ImageCandidatePlatform.LinuxAmd64,
        ImageCandidateOutputMode.Registry,
        now.AddMinutes(4),
        now.AddMinutes(5),
        $"sha256:{new string('d', 64)}",
        $"ghcr.io/ncosentino/pitcrew-dashboard@sha256:{new string('d', 64)}");
    var qualifications = new[]
    {
      ImageCandidateQualificationName.ImageBuild,
      ImageCandidateQualificationName.BuildKitDigest,
      ImageCandidateQualificationName.RegistryDigest,
      ImageCandidateQualificationName.BuilderCleanup,
    }.Select(name => new ImageCandidateQualification(
        candidate.CandidateId,
        name,
        ImageCandidateQualificationStatus.Passed))
        .ToArray();
    await store.StoreCandidateAsync(
        request.TenantId,
        candidate,
        qualifications,
        cancellationToken);
    return candidate.CandidateId;
  }

  private sealed record GitHubFailureScenario(
      GitHubClientOutcomeKind Kind,
      HttpStatusCode StatusCode,
      string ErrorCode,
      string? RetryAfter);
}
