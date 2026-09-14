using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Support;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

[NotInParallel]
public sealed class SupportHostingTests
{
  [Test]
  public async Task GitHub_Mode_Cookie_Administrator_Can_List_Support_Sessions(
      CancellationToken cancellationToken)
  {
    const string githubUserId = "123";
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "GitHub",
          "test-client",
          "test-secret",
          githubUserId,
          "Production");
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient(
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

      using var response = await client.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
          cancellationToken);

      await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Diagnostic_Credential_Can_Create_And_Read_Support_Session(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(client, cancellationToken);
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var enrollment = await SupportEnrollmentTestHelper.EnrollAsync(
          client,
          session.AntiforgeryToken,
          nodeKeys,
          cancellationToken);
      var credential = await DashboardTestHelpers.CreateDiagnosticCredentialAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "support diagnostic credential",
          DateTimeOffset.Parse("2027-08-01T00:00:00+00:00", CultureInfo.InvariantCulture),
          [],
          [],
          cancellationToken);

      using var createResponse = await DashboardTestHelpers.SendDiagnosticAsync(
          client,
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
          credential.Value,
          new CreateSupportDiagnosticSessionRequest(
              Guid.NewGuid(),
              Guid.Parse(enrollment.NodeId, CultureInfo.InvariantCulture),
              SupportDiagnosticModes.ConnectorOffline,
              null,
              300),
          cancellationToken);
      var created = await createResponse.Content.ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
          cancellationToken) ??
          throw new InvalidOperationException("Support session response was empty.");
      using var getResponse = await DashboardTestHelpers.SendDiagnosticAsync(
          client,
          HttpMethod.Get,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions/{created.SessionId}",
          credential.Value,
          null,
          cancellationToken);
      var fetched = await getResponse.Content.ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
          cancellationToken) ??
          throw new InvalidOperationException("Support session fetch response was empty.");

      await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
      await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
      await Assert.That(created.Capability).IsEqualTo(SupportCapability.DiagnosticsSnapshotV1);
      await Assert.That(created.RequestDigest).Matches("^[a-f0-9]{64}$");
      await Assert.That(created.NodeSigningKeyFingerprint).Matches("^[a-f0-9]{64}$");
      await Assert.That(created.ExpiresAt).IsEqualTo(fetched.ExpiresAt);
      await Assert.That(fetched.Capability).IsEqualTo(created.Capability);
      await Assert.That(fetched.RequestDigest).IsEqualTo(created.RequestDigest);
      await Assert.That(fetched.NodeSigningKeyFingerprint)
          .IsEqualTo(created.NodeSigningKeyFingerprint);
      await Assert.That(fetched.Status).IsEqualTo("Queued");
      await Assert.That(fetched.DiagnosticMode).IsEqualTo(SupportDiagnosticModes.ConnectorOffline);
      await Assert.That(fetched.Result).IsNull();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Uncertain_Relay_Acceptance_Retries_The_Same_Support_Intent(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 60,
          relayInternalUrl:
              "http://support-relay-internal:8080/");
      var relayHandler = new SupportSessionRelayHandler();
      relayHandler.EnqueueStatuses.Enqueue(
          HttpStatusCode.ServiceUnavailable);
      relayHandler.EnqueueStatuses.Enqueue(
          HttpStatusCode.Accepted);
      await using var factory =
          new WebApplicationFactory<Program>()
              .WithWebHostBuilder(
                  builder => builder.ConfigureServices(
                      services => services
                          .AddHttpClient(
                              SupportRelayManagementHttpClientOptions
                                  .ClientName)
                          .ConfigurePrimaryHttpMessageHandler(
                              () => relayHandler)));
      using var client = factory.CreateClient();
      var browserSession =
          await DashboardTestHelpers.GetSessionAsync(
              client,
              cancellationToken);
      var enrollment =
          await SupportEnrollmentTestHelper.EnrollAsync(
              client,
              browserSession.AntiforgeryToken,
              SupportKeyFactory.CreateNodeKeys(),
              cancellationToken);
      var intentId = Guid.NewGuid();
      var body = new
      {
        IntentId = intentId,
        NodeId = Guid.Parse(
            enrollment.NodeId,
            CultureInfo.InvariantCulture),
        DiagnosticMode =
            SupportDiagnosticModes.ConnectorOffline,
        ProfileId = (string?)null,
        ExpiresInSeconds = 300,
      };

      using var firstResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              browserSession.AntiforgeryToken,
              body,
              cancellationToken);
      using var secondResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              browserSession.AntiforgeryToken,
              body,
              cancellationToken);
      using var changedResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              browserSession.AntiforgeryToken,
              new
              {
                IntentId = intentId,
                NodeId = Guid.Parse(
                    enrollment.NodeId,
                    CultureInfo.InvariantCulture),
                DiagnosticMode =
                    SupportDiagnosticModes.CapacityMismatch,
                ProfileId = "default",
                ExpiresInSeconds = 300,
              },
              cancellationToken);
      var second = await secondResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
              cancellationToken);

      await Assert.That(firstResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.ServiceUnavailable);
      await Assert.That(secondResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);
      await Assert.That(changedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Conflict);
      await Assert.That(second).IsNotNull();
      await Assert.That(relayHandler.EnqueuedSessionIds)
          .Count().IsEqualTo(1);
      await Assert.That(second!.SessionId)
          .IsEqualTo(
              relayHandler.EnqueuedSessionIds[0].ToString("D"));
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Exact_Intent_Retry_Returns_Every_Existing_Lifecycle_Without_Reenqueue(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 60,
          relayInternalUrl:
              "http://support-relay-internal:8080/");
      var relayHandler = new SupportSessionRelayHandler();
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var fakeTime = new FakeTimeProvider(now);
      await using var factory =
          new WebApplicationFactory<Program>()
              .WithWebHostBuilder(
                  builder => builder.ConfigureServices(
                      services =>
                      {
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton<TimeProvider>(fakeTime);
                        services
                            .AddHttpClient(
                                SupportRelayManagementHttpClientOptions
                                    .ClientName)
                            .ConfigurePrimaryHttpMessageHandler(
                                () => relayHandler);
                      }));
      using var client = factory.CreateClient();
      var browserSession =
          await DashboardTestHelpers.GetSessionAsync(
              client,
              cancellationToken);
      var enrollment =
          await SupportEnrollmentTestHelper.EnrollAsync(
              client,
              browserSession.AntiforgeryToken,
              SupportKeyFactory.CreateNodeKeys(),
              cancellationToken);
      var expectedStatuses = new[]
      {
        ("queued", "Queued"),
        ("dispatched", "Dispatched"),
        ("completed", "Completed"),
        ("rejected", "Rejected"),
        ("cancelled", "Cancelled"),
        ("expired", "Expired"),
      };

      foreach (var (storedStatus, expectedStatus) in expectedStatuses)
      {
        var intentId = Guid.NewGuid();
        var body = new
        {
          IntentId = intentId,
          NodeId = Guid.Parse(
              enrollment.NodeId,
              CultureInfo.InvariantCulture),
          DiagnosticMode =
              SupportDiagnosticModes.ConnectorOffline,
          ProfileId = (string?)null,
          ExpiresInSeconds = 300,
        };
        using var createdResponse =
            await DashboardTestHelpers.PostAuthenticatedAsync(
                client,
                $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
                browserSession.AntiforgeryToken,
                body,
                cancellationToken);
        var created = await createdResponse.Content
            .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
                cancellationToken) ??
            throw new InvalidOperationException(
                "Expected a created support session.");
        await SetSupportSessionStatusAsync(
            databasePath,
            Guid.Parse(
                created.SessionId,
                CultureInfo.InvariantCulture),
            storedStatus,
            now,
            cancellationToken);

        using var retryResponse =
            await DashboardTestHelpers.PostAuthenticatedAsync(
                client,
                $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
                browserSession.AntiforgeryToken,
                body,
                cancellationToken);
        var retried = await retryResponse.Content
            .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
                cancellationToken);

        await Assert.That(retryResponse.StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(retried).IsNotNull();
        await Assert.That(retried!.SessionId)
            .IsEqualTo(created.SessionId);
        await Assert.That(retried.Status)
            .IsEqualTo(expectedStatus);
      }

      var elapsedIntentId = Guid.NewGuid();
      var elapsedBody = new
      {
        IntentId = elapsedIntentId,
        NodeId = Guid.Parse(
            enrollment.NodeId,
            CultureInfo.InvariantCulture),
        DiagnosticMode =
            SupportDiagnosticModes.ConnectorOffline,
        ProfileId = (string?)null,
        ExpiresInSeconds = 300,
      };
      using var elapsedCreatedResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              browserSession.AntiforgeryToken,
              elapsedBody,
              cancellationToken);
      var elapsedCreated = await elapsedCreatedResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
              cancellationToken) ??
          throw new InvalidOperationException(
              "Expected an elapsed support session.");
      fakeTime.Advance(TimeSpan.FromSeconds(301));

      using var elapsedRetryResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              browserSession.AntiforgeryToken,
              elapsedBody,
              cancellationToken);
      var elapsedRetry = await elapsedRetryResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
              cancellationToken);

      await Assert.That(elapsedRetryResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);
      await Assert.That(elapsedRetry).IsNotNull();
      await Assert.That(elapsedRetry!.SessionId)
          .IsEqualTo(elapsedCreated.SessionId);
      await Assert.That(elapsedRetry.Status)
          .IsEqualTo("Expired");
      await Assert.That(relayHandler.EnqueuedSessionIds)
          .Count().IsEqualTo(7);
      await using var connection =
          new SqliteConnection($"Data Source={databasePath}");
      await connection.OpenAsync(cancellationToken);
      await using var countCommand = connection.CreateCommand();
      countCommand.CommandText =
          "SELECT COUNT(*) FROM support_sessions;";
      await Assert.That(
              Convert.ToInt32(
                  await countCommand.ExecuteScalarAsync(
                      cancellationToken),
                  CultureInfo.InvariantCulture))
          .IsEqualTo(7);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Relay_Enqueue_Conflict_Is_A_Stable_Client_Outcome(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 60,
          relayInternalUrl:
              "http://support-relay-internal:8080/");
      var relayHandler = new SupportSessionRelayHandler();
      relayHandler.EnqueueStatuses.Enqueue(
          HttpStatusCode.Conflict);
      await using var factory =
          new WebApplicationFactory<Program>()
              .WithWebHostBuilder(
                  builder => builder.ConfigureServices(
                      services => services
                          .AddHttpClient(
                              SupportRelayManagementHttpClientOptions
                                  .ClientName)
                          .ConfigurePrimaryHttpMessageHandler(
                              () => relayHandler)));
      using var client = factory.CreateClient();
      var browserSession =
          await DashboardTestHelpers.GetSessionAsync(
              client,
              cancellationToken);
      var enrollment =
          await SupportEnrollmentTestHelper.EnrollAsync(
              client,
              browserSession.AntiforgeryToken,
              SupportKeyFactory.CreateNodeKeys(),
              cancellationToken);

      using var response =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              browserSession.AntiforgeryToken,
              new
              {
                IntentId = Guid.NewGuid(),
                NodeId = Guid.Parse(
                    enrollment.NodeId,
                    CultureInfo.InvariantCulture),
                DiagnosticMode =
                    SupportDiagnosticModes.ConnectorOffline,
                ProfileId = (string?)null,
                ExpiresInSeconds = 300,
              },
              cancellationToken);
      using var error = JsonDocument.Parse(
          await response.Content.ReadAsStringAsync(
              cancellationToken));

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.Conflict);
      await Assert.That(
              error.RootElement
                  .GetProperty("error")
                  .GetProperty("code")
                  .GetString())
          .IsEqualTo("support_session_enqueue_conflict");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Explicit_Read_Projects_Dispatched_Then_Rejected_Relay_State(
      CancellationToken cancellationToken)
  {
    var databasePath =
        DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 60,
          relayInternalUrl:
              "http://support-relay-internal:8080/");
      var relayHandler = new SupportSessionRelayHandler();
      await using var factory =
          new WebApplicationFactory<Program>()
              .WithWebHostBuilder(
                  builder => builder.ConfigureServices(
                      services => services
                          .AddHttpClient(
                              SupportRelayManagementHttpClientOptions
                                  .ClientName)
                          .ConfigurePrimaryHttpMessageHandler(
                              () => relayHandler)));
      using var client = factory.CreateClient();
      var browserSession =
          await DashboardTestHelpers.GetSessionAsync(
              client,
              cancellationToken);
      var enrollment =
          await SupportEnrollmentTestHelper.EnrollAsync(
              client,
              browserSession.AntiforgeryToken,
              SupportKeyFactory.CreateNodeKeys(),
              cancellationToken);
      using var createRequest = new HttpRequestMessage(
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions")
      {
        Content = JsonContent.Create(
            new CreateSupportDiagnosticSessionRequest(
                Guid.NewGuid(),
                Guid.Parse(
                    enrollment.NodeId,
                    CultureInfo.InvariantCulture),
                SupportDiagnosticModes.ConnectorOffline,
                null,
                300)),
      };
      createRequest.Headers.Add(
          DashboardTestHelpers.AntiforgeryHeader,
          browserSession.AntiforgeryToken);
      using var createResponse = await client.SendAsync(
          createRequest,
          cancellationToken);
      var created = await createResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
              cancellationToken) ??
          throw new InvalidOperationException(
              "Support session response was empty.");
      var dispatchedAt = created.RequestedAt.AddSeconds(1);
      relayHandler.Status = "dispatched";
      using var dispatchedResponse = await client.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions/{created.SessionId}",
          cancellationToken);
      var dispatched = await dispatchedResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
              cancellationToken) ??
          throw new InvalidOperationException(
              "Dispatched session response was empty.");
      relayHandler.Status = "rejected";
      relayHandler.DispatchedAt = dispatchedAt;
      relayHandler.RejectedAt =
          dispatchedAt.AddSeconds(1);
      relayHandler.RejectionDisposition =
          SupportRequestRejectionDispositions
              .UnsupportedCapability;
      using var rejectedResponse = await client.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions/{created.SessionId}",
          cancellationToken);
      var rejected = await rejectedResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
              cancellationToken) ??
          throw new InvalidOperationException(
              "Rejected session response was empty.");

      await Assert.That(createResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Accepted);
      await Assert.That(created.Status).IsEqualTo("Queued");
      await Assert.That(created.DispatchedAt).IsNull();
      await Assert.That(dispatchedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(dispatched.Status)
          .IsEqualTo("Dispatched");
      await Assert.That(dispatched.DispatchedAt)
          .IsNull();
      await Assert.That(rejectedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(rejected.Status)
          .IsEqualTo("Rejected");
      await Assert.That(rejected.DispatchedAt)
          .IsEqualTo(dispatchedAt);
      await Assert.That(rejected.RejectionDisposition)
          .IsEqualTo(
              SupportRequestRejectionDispositions
                  .UnsupportedCapability);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Browser_Support_Session_Post_Requires_Antiforgery(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      await DashboardTestHelpers.GetSessionAsync(client, cancellationToken);
      using var response = await client.PostAsJsonAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
          new CreateSupportDiagnosticSessionRequest(
              Guid.NewGuid(),
              Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture),
              SupportDiagnosticModes.Full,
              null,
              300),
          cancellationToken);

      await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Profile_Scoped_Diagnostic_Credential_Cannot_Request_Unscoped_Support_Session(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(client, cancellationToken);
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var enrollment = await SupportEnrollmentTestHelper.EnrollAsync(
          client,
          session.AntiforgeryToken,
          nodeKeys,
          cancellationToken);
      var credential = await DashboardTestHelpers.CreateDiagnosticCredentialAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "profile support diagnostic credential",
          DateTimeOffset.Parse("2027-08-01T00:00:00+00:00", CultureInfo.InvariantCulture),
          [],
          ["default"],
          cancellationToken);

      using var createResponse = await DashboardTestHelpers.SendDiagnosticAsync(
          client,
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
          credential.Value,
          new CreateSupportDiagnosticSessionRequest(
              Guid.NewGuid(),
              Guid.Parse(enrollment.NodeId, CultureInfo.InvariantCulture),
              SupportDiagnosticModes.Full,
              null,
              300),
          cancellationToken);

      await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Connector_Node_Restriction_Does_Not_Authorize_A_Support_Node_With_The_Same_Guid(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var enrollment = await SupportEnrollmentTestHelper.EnrollAsync(
          client,
          session.AntiforgeryToken,
          SupportKeyFactory.CreateNodeKeys(),
          cancellationToken);
      var nodeId = Guid.Parse(
          enrollment.NodeId,
          CultureInfo.InvariantCulture);
      var createBody = new
      {
        IntentId = Guid.NewGuid(),
        NodeId = nodeId,
        DiagnosticMode =
            SupportDiagnosticModes.ConnectorOffline,
        ProfileId = (string?)null,
        ExpiresInSeconds = 300,
      };
      using var adminCreateResponse =
          await DashboardTestHelpers.PostAuthenticatedAsync(
              client,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              session.AntiforgeryToken,
              createBody,
              cancellationToken);
      var adminCreated = await adminCreateResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
              cancellationToken) ??
          throw new InvalidOperationException(
              "Expected an administrator-created support session.");
      var credential =
          await DashboardTestHelpers.CreateDiagnosticCredentialAsync(
              client,
              session.AntiforgeryToken,
              DashboardTestHelpers.TenantId,
              "connector node restricted support credential",
              DateTimeOffset.Parse(
                  "2027-08-01T00:00:00+00:00",
                  CultureInfo.InvariantCulture),
              [],
              [],
              cancellationToken);
      await using (var connection =
          new SqliteConnection($"Data Source={databasePath}"))
      {
        await connection.OpenAsync(cancellationToken);
        await using (var disableForeignKeys =
            connection.CreateCommand())
        {
          disableForeignKeys.CommandText =
              "PRAGMA foreign_keys = OFF;";
          await disableForeignKeys.ExecuteNonQueryAsync(
              cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO diagnostic_credential_nodes (
                credential_id,
                node_id)
            VALUES (
                $credentialId,
                $nodeId);
            """;
        command.Parameters.AddWithValue(
            "$credentialId",
            credential.Credential.CredentialId.ToString("D"));
        command.Parameters.AddWithValue(
            "$nodeId",
            nodeId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
      }

      using var createResponse =
          await DashboardTestHelpers.SendDiagnosticAsync(
              client,
              HttpMethod.Post,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              credential.Value,
              createBody,
              cancellationToken);
      using var restrictedListResponse =
          await DashboardTestHelpers.SendDiagnosticAsync(
              client,
              HttpMethod.Get,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              credential.Value,
              body: null,
              cancellationToken);
      var restrictedList = await restrictedListResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse[]>(
              cancellationToken);
      using var restrictedExactResponse =
          await DashboardTestHelpers.SendDiagnosticAsync(
              client,
              HttpMethod.Get,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions/{adminCreated.SessionId}",
              credential.Value,
              body: null,
              cancellationToken);
      using var adminListResponse =
          await client.GetAsync(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              cancellationToken);
      var adminList = await adminListResponse.Content
          .ReadFromJsonAsync<SupportDiagnosticSessionResponse[]>(
              cancellationToken);
      using var adminExactResponse =
          await client.GetAsync(
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions/{adminCreated.SessionId}",
              cancellationToken);
      var unrestrictedCredential =
          await DashboardTestHelpers.CreateDiagnosticCredentialAsync(
              client,
              session.AntiforgeryToken,
              DashboardTestHelpers.TenantId,
              "tenant support diagnostic credential",
              DateTimeOffset.Parse(
                  "2027-08-01T00:00:00+00:00",
                  CultureInfo.InvariantCulture),
              [],
              [],
              cancellationToken);
      using var unrestrictedListResponse =
          await DashboardTestHelpers.SendDiagnosticAsync(
              client,
              HttpMethod.Get,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
              unrestrictedCredential.Value,
              body: null,
              cancellationToken);
      var unrestrictedList =
          await unrestrictedListResponse.Content
              .ReadFromJsonAsync<SupportDiagnosticSessionResponse[]>(
                  cancellationToken);
      using var unrestrictedExactResponse =
          await DashboardTestHelpers.SendDiagnosticAsync(
              client,
              HttpMethod.Get,
              $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions/{adminCreated.SessionId}",
              unrestrictedCredential.Value,
              body: null,
              cancellationToken);

      await Assert.That(createResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden);
      await Assert.That(restrictedListResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(restrictedList).IsEmpty();
      await Assert.That(restrictedExactResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Forbidden);
      await Assert.That(adminList).Contains(
          candidate => candidate.SessionId == adminCreated.SessionId);
      await Assert.That(adminExactResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(unrestrictedList).Contains(
          candidate => candidate.SessionId == adminCreated.SessionId);
      await Assert.That(unrestrictedExactResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Node_Enrollment_Is_Tenant_Bound_One_Time_And_Contains_No_Private_Material(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var enrollment = await SupportEnrollmentTestHelper.CreateAuthorizationAsync(
          client,
          session.AntiforgeryToken,
          cancellationToken);
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var completionId = Guid.NewGuid();
      var request = new CompleteSupportEnrollmentRequest(
          DashboardTestHelpers.TenantId,
          enrollment.EnrollmentCode,
          completionId,
          nodeKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          nodeKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      using var crossTenant = await client.PostAsJsonAsync(
          "/api/support-agent/v1/enrollments/complete",
          request with { TenantId = "another-tenant" },
          cancellationToken);
      using var completedResponse = await client.PostAsJsonAsync(
          "/api/support-agent/v1/enrollments/complete",
          request,
          cancellationToken);
      var responseJson = await completedResponse.Content.ReadAsStringAsync(
          cancellationToken);
      var completed = JsonSerializer.Deserialize<SupportEnrollmentCompletionResponse>(
          responseJson,
          new JsonSerializerOptions(JsonSerializerDefaults.Web));
      using var exactRetry = await client.PostAsJsonAsync(
          "/api/support-agent/v1/enrollments/complete",
          request,
          cancellationToken);
      var exactRetryCompletion = await exactRetry.Content
          .ReadFromJsonAsync<SupportEnrollmentCompletionResponse>(
              cancellationToken);
      using var replay = await client.PostAsJsonAsync(
          "/api/support-agent/v1/enrollments/complete",
          request with { CompletionId = Guid.NewGuid() },
          cancellationToken);

      await Assert.That(crossTenant.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(completedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(completed).IsNotNull();
      await Assert.That(exactRetry.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(exactRetryCompletion).IsEqualTo(completed);
      await Assert.That(replay.StatusCode)
          .IsEqualTo(HttpStatusCode.Conflict);
      await Assert.That(responseJson)
          .DoesNotContain(enrollment.EnrollmentCode);
      await Assert.That(responseJson)
          .DoesNotContain("\"transportCredential\":");
      await Assert.That(responseJson.ToLowerInvariant())
          .DoesNotContain("private");
      await Assert.That(responseJson.ToLowerInvariant())
          .DoesNotContain("pkcs8");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Rotation_Commits_New_Identity_And_Rejects_Old_Credential(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var original = await SupportEnrollmentTestHelper.EnrollAsync(
          client,
          session.AntiforgeryToken,
          SupportKeyFactory.CreateNodeKeys(),
          cancellationToken);
      var replacementKeys = SupportKeyFactory.CreateNodeKeys();
      const string replacementCredential =
          "pcs_node_replacement-credential-abcdefghijklmnopqrstuvwxyz";
      var rotationId = Guid.NewGuid();
      var rotationRequest = new RotateSupportIdentityRequest(
          rotationId,
          DashboardTestHelpers.TenantId,
          original.TransportCredential,
          replacementCredential,
          replacementKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          replacementKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      using var rotatedResponse = await client.PostAsJsonAsync(
          $"/api/support-agent/v1/identities/{original.NodeId}/rotate",
          rotationRequest,
          cancellationToken);
      using var prepareRetry = await client.PostAsJsonAsync(
          $"/api/support-agent/v1/identities/{original.NodeId}/rotate",
          rotationRequest,
          cancellationToken);
      using var finalizedResponse = await client.PostAsJsonAsync(
          $"/api/support-agent/v1/identities/{original.NodeId}/rotate/finalize",
          new FinalizeSupportIdentityRotationRequest(
              rotationId,
              DashboardTestHelpers.TenantId,
              replacementCredential),
          cancellationToken);
      var thirdKeys = SupportKeyFactory.CreateNodeKeys();
      using var oldCredentialResponse = await client.PostAsJsonAsync(
          $"/api/support-agent/v1/identities/{original.NodeId}/rotate",
          new RotateSupportIdentityRequest(
              Guid.NewGuid(),
              DashboardTestHelpers.TenantId,
              original.TransportCredential,
              "pcs_node_another-replacement-credential-abcdefghijklmnop",
              thirdKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
              thirdKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url),
          cancellationToken);

      await Assert.That(rotatedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(prepareRetry.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(finalizedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(oldCredentialResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Anonymous_Enrollment_Completion_Is_Rate_Limited(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var keys = SupportKeyFactory.CreateNodeKeys();
      var request = new CompleteSupportEnrollmentRequest(
          DashboardTestHelpers.TenantId,
          "invalid-enrollment-code-that-is-long-enough",
          Guid.NewGuid(),
          keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      HttpStatusCode lastStatus = default;
      for (var index = 0; index < 31; index++)
      {
        using var response = await client.PostAsJsonAsync(
            "/api/support-agent/v1/enrollments/complete",
            request,
            cancellationToken);
        lastStatus = response.StatusCode;
      }

      await Assert.That(lastStatus)
          .IsEqualTo(HttpStatusCode.TooManyRequests);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Anonymous_Functional_Rate_Limits_Isolate_Tenants_And_Nodes(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var keys = SupportKeyFactory.CreateNodeKeys();
      var enrollmentRequest = new CompleteSupportEnrollmentRequest(
          "tenant-rate-a",
          "invalid-enrollment-code-that-is-long-enough",
          Guid.NewGuid(),
          keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      for (var index = 0; index < 30; index++)
      {
        using var response = await client.PostAsJsonAsync(
            "/api/support-agent/v1/enrollments/complete",
            enrollmentRequest,
            cancellationToken);
      }
      using var otherTenant = await client.PostAsJsonAsync(
          "/api/support-agent/v1/enrollments/complete",
          enrollmentRequest with { TenantId = "tenant-rate-b" },
          cancellationToken);

      var firstNodeId = Guid.NewGuid();
      var rotationRequest = new RotateSupportIdentityRequest(
          Guid.NewGuid(),
          "tenant-rate-a",
          "pcs_node_current-credential-abcdefghijklmnopqrstuvwxyz",
          "pcs_node_replacement-credential-abcdefghijklmnopqrstuvwxyz",
          keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      for (var index = 0; index < 30; index++)
      {
        using var response = await client.PostAsJsonAsync(
            $"/api/support-agent/v1/identities/{firstNodeId:D}/rotate",
            rotationRequest,
            cancellationToken);
      }
      using var otherNode = await client.PostAsJsonAsync(
          $"/api/support-agent/v1/identities/{Guid.NewGuid():D}/rotate",
          rotationRequest,
          cancellationToken);

      await Assert.That(otherTenant.StatusCode)
          .IsNotEqualTo(HttpStatusCode.TooManyRequests);
      await Assert.That(otherNode.StatusCode)
          .IsNotEqualTo(HttpStatusCode.TooManyRequests);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Anonymous_Rate_Limit_Retains_Source_Abuse_Ceiling(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var keys = SupportKeyFactory.CreateNodeKeys();
      var request = new CompleteSupportEnrollmentRequest(
          "tenant-rate-0",
          "invalid-enrollment-code-that-is-long-enough",
          Guid.NewGuid(),
          keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      for (var tenantIndex = 0; tenantIndex < 8; tenantIndex++)
      {
        for (var requestIndex = 0; requestIndex < 30; requestIndex++)
        {
          using var response = await client.PostAsJsonAsync(
              "/api/support-agent/v1/enrollments/complete",
              request with { TenantId = $"tenant-rate-{tenantIndex}" },
              cancellationToken);
        }
      }
      using var limited = await client.PostAsJsonAsync(
          "/api/support-agent/v1/enrollments/complete",
          request with { TenantId = "tenant-rate-overflow" },
          cancellationToken);

      await Assert.That(limited.StatusCode)
          .IsEqualTo(HttpStatusCode.TooManyRequests);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Identity_List_Projects_And_Preserves_Relay_Activity(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 60,
          relayInternalUrl: "http://support-relay-internal:8080/");
      var pollAt = DateTimeOffset.Parse(
          "2026-08-01T00:01:00+00:00",
          CultureInfo.InvariantCulture);
      var resultAt = pollAt.AddMinutes(1);
      var now = resultAt.AddMinutes(1);
      var fakeTime = new FakeTimeProvider(now);
      var relayHandler = new SupportActivityRelayHandler(
          pollAt,
          resultAt);
      await using var factory = new WebApplicationFactory<Program>()
          .WithWebHostBuilder(
              builder => builder.ConfigureServices(
                  services =>
                  {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(fakeTime);
                    services
                        .AddHttpClient(
                          SupportRelayManagementHttpClientOptions.ClientName)
                        .ConfigurePrimaryHttpMessageHandler(
                            () => relayHandler);
                  }));
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var enrollment = await SupportEnrollmentTestHelper.EnrollAsync(
          client,
          session.AntiforgeryToken,
          SupportKeyFactory.CreateNodeKeys(),
          cancellationToken);

      using var projectedResponse = await client.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/identities",
          cancellationToken);
      using var projectedDocument = JsonDocument.Parse(
          await projectedResponse.Content.ReadAsStringAsync(cancellationToken));
      var projected = projectedDocument.RootElement[0];
      relayHandler.FailActivity = true;
      using var preservedResponse = await client.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/identities",
          cancellationToken);
      using var preservedDocument = JsonDocument.Parse(
          await preservedResponse.Content.ReadAsStringAsync(cancellationToken));
      var preserved = preservedDocument.RootElement[0];
      relayHandler.FailActivity = false;
      relayHandler.SetActivity(
          now.AddMinutes(6),
          now.AddMinutes(7));
      using var invalidResponse = await client.GetAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/identities",
          cancellationToken);
      using var invalidDocument = JsonDocument.Parse(
          await invalidResponse.Content.ReadAsStringAsync(cancellationToken));
      var invalidPreserved = invalidDocument.RootElement[0];

      await Assert.That(projectedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(relayHandler.ActivityRequestCount).IsEqualTo(3);
      await Assert.That(relayHandler.LastTenantId)
          .IsEqualTo(DashboardTestHelpers.TenantId);
      await Assert.That(relayHandler.LastNodeIds)
          .IsEquivalentTo([
            Guid.Parse(
                enrollment.NodeId,
                CultureInfo.InvariantCulture),
          ]);
      await Assert.That(projected.GetProperty("lastPollAt").GetDateTimeOffset())
          .IsEqualTo(pollAt);
      await Assert.That(projected.GetProperty("lastResultAt").GetDateTimeOffset())
          .IsEqualTo(resultAt);
      await Assert.That(preservedResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(preserved.GetProperty("lastPollAt").GetDateTimeOffset())
          .IsEqualTo(pollAt);
      await Assert.That(preserved.GetProperty("lastResultAt").GetDateTimeOffset())
          .IsEqualTo(resultAt);
      await Assert.That(invalidResponse.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(invalidPreserved.GetProperty("lastPollAt").GetDateTimeOffset())
          .IsEqualTo(pollAt);
      await Assert.That(invalidPreserved.GetProperty("lastResultAt").GetDateTimeOffset())
          .IsEqualTo(resultAt);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Hosted_Maintenance_Retries_Orphan_Relay_Cleanup(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 1,
          relayInternalUrl: "http://support-relay-internal:8080/");
      var relayHandler = new RecordingRelayHandler();
      await using var factory = new WebApplicationFactory<Program>()
          .WithWebHostBuilder(
              builder => builder.ConfigureServices(
                  services => services
                      .AddHttpClient(
                          SupportRelayManagementHttpClientOptions.ClientName)
                      .ConfigurePrimaryHttpMessageHandler(
                          () => relayHandler)));
      using var client = factory.CreateClient();
      using var health = await client.GetAsync("/health", cancellationToken);
      var nodeId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;
      await using (var connection = new SqliteConnection(
          $"Data Source={databasePath}"))
      {
        await connection.OpenAsync(cancellationToken);
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO support_relay_cleanup (
                node_id,
                created_at,
                next_attempt_at,
                lease_id,
                lease_expires_at)
            VALUES (
                $nodeId,
                $createdAt,
                $nextAttemptAt,
                $leaseId,
                $leaseExpiresAt);
            """;
        insert.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
        insert.Parameters.AddWithValue("$createdAt", now.AddMinutes(-5).ToString("O"));
        insert.Parameters.AddWithValue("$nextAttemptAt", now.AddMinutes(-4).ToString("O"));
        insert.Parameters.AddWithValue("$leaseId", Guid.NewGuid().ToString("D"));
        insert.Parameters.AddWithValue("$leaseExpiresAt", now.AddMinutes(-3).ToString("O"));
        await insert.ExecuteNonQueryAsync(cancellationToken);
      }

      for (var index = 0;
          index < 50 && relayHandler.RequestCount == 0;
          index++)
      {
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
      }

      long remaining;
      await using (var connection = new SqliteConnection(
          $"Data Source={databasePath}"))
      {
        await connection.OpenAsync(cancellationToken);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM support_relay_cleanup;";
        remaining = (long)(await count.ExecuteScalarAsync(cancellationToken) ?? -1L);
      }

      await Assert.That(relayHandler.RequestCount).IsEqualTo(1);
      await Assert.That(
              relayHandler.LastRequestUri?.GetLeftPart(
                  UriPartial.Authority))
              .IsEqualTo("http://support-relay-internal:8080");
      await Assert.That(remaining).IsEqualTo(0);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Hosted_Maintenance_Defers_Failed_Cleanup_With_Backoff(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 1);
      var relayHandler = new RecordingRelayHandler(
          HttpStatusCode.InternalServerError);
      await using var factory = new WebApplicationFactory<Program>()
          .WithWebHostBuilder(
              builder => builder.ConfigureServices(
                  services => services
                      .AddHttpClient(
                          SupportRelayManagementHttpClientOptions.ClientName)
                      .ConfigurePrimaryHttpMessageHandler(
                          () => relayHandler)));
      using var client = factory.CreateClient();
      var nodeId = Guid.NewGuid();
      var insertedAt = DateTimeOffset.UtcNow;
      await using (var connection = new SqliteConnection(
          $"Data Source={databasePath}"))
      {
        await connection.OpenAsync(cancellationToken);
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO support_relay_cleanup (
                node_id,
                created_at,
                next_attempt_at,
                lease_id,
                lease_expires_at)
            VALUES (
                $nodeId,
                $createdAt,
                $nextAttemptAt,
                $leaseId,
                $leaseExpiresAt);
            """;
        insert.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
        insert.Parameters.AddWithValue(
            "$createdAt",
            insertedAt.AddMinutes(-5).ToString("O"));
        insert.Parameters.AddWithValue(
            "$nextAttemptAt",
            insertedAt.AddMinutes(-4).ToString("O"));
        insert.Parameters.AddWithValue("$leaseId", Guid.NewGuid().ToString("D"));
        insert.Parameters.AddWithValue(
            "$leaseExpiresAt",
            insertedAt.AddMinutes(-3).ToString("O"));
        await insert.ExecuteNonQueryAsync(cancellationToken);
      }

      for (var index = 0;
          index < 50 && relayHandler.RequestCount == 0;
          index++)
      {
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
      }

      int attemptCount;
      DateTimeOffset nextAttemptAt;
      string? leaseId;
      string? leaseExpiresAt;
      await using (var connection = new SqliteConnection(
          $"Data Source={databasePath}"))
      {
        await connection.OpenAsync(cancellationToken);
        await using var read = connection.CreateCommand();
        read.CommandText =
            """
            SELECT
                attempt_count,
                next_attempt_at,
                lease_id,
                lease_expires_at
            FROM support_relay_cleanup
            WHERE node_id = $nodeId;
            """;
        read.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        attemptCount = reader.GetInt32(0);
        nextAttemptAt = DateTimeOffset.Parse(
            reader.GetString(1),
            CultureInfo.InvariantCulture);
        leaseId = await reader.IsDBNullAsync(2, cancellationToken)
            ? null
            : reader.GetString(2);
        leaseExpiresAt = await reader.IsDBNullAsync(3, cancellationToken)
            ? null
            : reader.GetString(3);
      }

      await Assert.That(relayHandler.RequestCount).IsEqualTo(1);
      await Assert.That(attemptCount).IsEqualTo(1);
      await Assert.That(nextAttemptAt)
          .IsGreaterThanOrEqualTo(insertedAt.AddSeconds(25));
      await Assert.That(nextAttemptAt)
          .IsLessThanOrEqualTo(insertedAt.AddSeconds(40));
      await Assert.That(leaseId).IsNull();
      await Assert.That(leaseExpiresAt).IsNull();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Hosted_Maintenance_Preserves_Invalid_Relay_Configuration_And_Retries_Later(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      var nodeId = Guid.NewGuid();
      var originalLeaseId = Guid.NewGuid();
      var originalLeaseExpiry = DateTimeOffset.UtcNow.AddMinutes(-3);
      using (var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 1,
          relayInternalUrl: "http://relay.example.com/"))
      {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        await using (var connection = new SqliteConnection(
            $"Data Source={databasePath}"))
        {
          await connection.OpenAsync(cancellationToken);
          await using var insert = connection.CreateCommand();
          insert.CommandText =
              """
              INSERT INTO support_relay_cleanup (
                  node_id,
                  created_at,
                  next_attempt_at,
                  lease_id,
                  lease_expires_at)
              VALUES (
                  $nodeId,
                  $createdAt,
                  $nextAttemptAt,
                  $leaseId,
                  $leaseExpiresAt);
              """;
          insert.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
          insert.Parameters.AddWithValue(
              "$createdAt",
              now.AddMinutes(-5).ToString("O"));
          insert.Parameters.AddWithValue(
              "$nextAttemptAt",
              now.AddMinutes(-4).ToString("O"));
          insert.Parameters.AddWithValue(
              "$leaseId",
              originalLeaseId.ToString("D"));
          insert.Parameters.AddWithValue(
              "$leaseExpiresAt",
              originalLeaseExpiry.ToString("O"));
          await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);

        await using var preservedConnection = new SqliteConnection(
            $"Data Source={databasePath}");
        await preservedConnection.OpenAsync(cancellationToken);
        await using var read = preservedConnection.CreateCommand();
        read.CommandText =
            """
            SELECT
                attempt_count,
                lease_id,
                lease_expires_at
            FROM support_relay_cleanup
            WHERE node_id = $nodeId;
            """;
        read.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        await Assert.That(reader.GetInt32(0)).IsEqualTo(0);
        await Assert.That(reader.GetString(1))
            .IsEqualTo(originalLeaseId.ToString("D"));
        await Assert.That(
                DateTimeOffset.Parse(
                    reader.GetString(2),
                    CultureInfo.InvariantCulture))
            .IsEqualTo(originalLeaseExpiry);
      }

      var relayHandler = new RecordingRelayHandler();
      using (var configuration = new TestConfigurationScope(
          databasePath,
          "https://relay.test/",
          "relay-secret-for-tests",
          relayCleanupIntervalSeconds: 1))
      {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(
                builder => builder.ConfigureServices(
                    services => services
                        .AddHttpClient(
                            SupportRelayManagementHttpClientOptions.ClientName)
                        .ConfigurePrimaryHttpMessageHandler(
                            () => relayHandler)));
        using var client = factory.CreateClient();
        for (var index = 0;
            index < 50 && relayHandler.RequestCount == 0;
            index++)
        {
          await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        await using var connection = new SqliteConnection(
            $"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM support_relay_cleanup;";
        var remaining =
            (long)(await count.ExecuteScalarAsync(cancellationToken) ?? -1L);
        await Assert.That(relayHandler.RequestCount).IsEqualTo(1);
        await Assert.That(remaining).IsEqualTo(0);
      }
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Anonymous_Rotation_And_Finalization_Share_Rate_Limit(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var nodeId = Guid.NewGuid();
      var rotationId = Guid.NewGuid();
      var keys = SupportKeyFactory.CreateNodeKeys();
      var request = new RotateSupportIdentityRequest(
          rotationId,
          DashboardTestHelpers.TenantId,
          "pcs_node_current-credential-abcdefghijklmnopqrstuvwxyz",
          "pcs_node_replacement-credential-abcdefghijklmnopqrstuvwxyz",
          keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      for (var index = 0; index < 30; index++)
      {
        using var response = await client.PostAsJsonAsync(
            $"/api/support-agent/v1/identities/{nodeId:D}/rotate",
            request,
            cancellationToken);
      }
      using var limited = await client.PostAsJsonAsync(
          $"/api/support-agent/v1/identities/{nodeId:D}/rotate/finalize",
          new FinalizeSupportIdentityRotationRequest(
              rotationId,
              DashboardTestHelpers.TenantId,
              request.ReplacementTransportCredential),
          cancellationToken);

      await Assert.That(limited.StatusCode)
          .IsEqualTo(HttpStatusCode.TooManyRequests);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Legacy_Manual_Enrollment_Is_Disabled_By_Default(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(
          client,
          cancellationToken);
      var keys = SupportKeyFactory.CreateNodeKeys();
      using var request = new HttpRequestMessage(
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/enrollments")
      {
        Content = JsonContent.Create(new CreateSupportEnrollmentRequest(
            "Support node",
            keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
            keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url)),
      };
      request.Headers.Add(
          DashboardTestHelpers.AntiforgeryHeader,
          session.AntiforgeryToken);
      using var response = await client.SendAsync(request, cancellationToken);

      await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  private static async Task SetSupportSessionStatusAsync(
      string databasePath,
      Guid sessionId,
      string status,
      DateTimeOffset transitionedAt,
      CancellationToken cancellationToken)
  {
    await using var connection =
        new SqliteConnection($"Data Source={databasePath}");
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE support_sessions
        SET status = $status,
            dispatched_at =
                CASE
                    WHEN $status IN ('dispatched', 'completed', 'rejected')
                    THEN $transitionedAt
                    ELSE dispatched_at
                END,
            rejection_disposition =
                CASE
                    WHEN $status = 'rejected'
                    THEN 'broker-evidence-access-denied'
                    ELSE NULL
                END,
            completed_at =
                CASE
                    WHEN $status IN ('completed', 'rejected', 'expired')
                    THEN $transitionedAt
                    ELSE completed_at
                END,
            cancelled_at =
                CASE
                    WHEN $status = 'cancelled'
                    THEN $transitionedAt
                    ELSE cancelled_at
                END
        WHERE session_id = $sessionId;
        """;
    command.Parameters.AddWithValue("$status", status);
    command.Parameters.AddWithValue(
        "$transitionedAt",
        transitionedAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue(
        "$sessionId",
        sessionId.ToString("D", CultureInfo.InvariantCulture));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

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
                new Claim(PitCrewClaimTypes.GitHubLogin, "hosted-operator"),
                new Claim(ClaimTypes.NameIdentifier, githubUserId),
                new Claim(ClaimTypes.Name, "Hosted operator"),
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

  private sealed class RecordingRelayHandler(
      HttpStatusCode _statusCode = HttpStatusCode.NotFound) : HttpMessageHandler
  {
    private int _requestCount;
    private Uri? _lastRequestUri;

    public int RequestCount => Volatile.Read(ref _requestCount);

    public Uri? LastRequestUri => Volatile.Read(ref _lastRequestUri);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
      Interlocked.Increment(ref _requestCount);
      Interlocked.Exchange(ref _lastRequestUri, request.RequestUri);
      return Task.FromResult(new HttpResponseMessage(_statusCode));
    }
  }

}
