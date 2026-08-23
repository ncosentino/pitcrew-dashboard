using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Globalization;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Moq;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Images;
using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

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

  private sealed record GitHubFailureScenario(
      GitHubClientOutcomeKind Kind,
      HttpStatusCode StatusCode,
      string ErrorCode,
      string? RetryAfter);
}
