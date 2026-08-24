using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PitCrew.Support.Relay.App;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Relay.App.Tests;

[NotInParallel]
public sealed class RelayNodeActivityApiTests
{
  [Test]
  public async Task Activity_Requires_Internal_Bearer_And_Enforces_Bounds(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      await using var factory = CreateFactory(databasePath);
      using var client = factory.CreateClient();
      var nodeId = Guid.NewGuid();

      using var unauthorized = await client.PostAsJsonAsync(
          "/internal/support/v1/nodes/activity",
          new RelayNodeActivityRequest("tenant-a", [nodeId]),
          cancellationToken);
      client.DefaultRequestHeaders.Authorization =
          new AuthenticationHeaderValue("Bearer", InternalBearerSecret);
      using var empty = await client.PostAsJsonAsync(
          "/internal/support/v1/nodes/activity",
          new RelayNodeActivityRequest("tenant-a", []),
          cancellationToken);
      using var oversized = await client.PostAsJsonAsync(
          "/internal/support/v1/nodes/activity",
          new RelayNodeActivityRequest(
              "tenant-a",
              Enumerable.Range(0, 257).Select(_ => Guid.NewGuid()).ToArray()),
          cancellationToken);
      using var duplicate = await client.PostAsJsonAsync(
          "/internal/support/v1/nodes/activity",
          new RelayNodeActivityRequest("tenant-a", [nodeId, nodeId]),
          cancellationToken);

      await Assert.That(unauthorized.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
      await Assert.That(empty.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(oversized.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(duplicate.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Activity_Returns_Only_Requested_Tenant_Node_Timestamps(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      await using var factory = CreateFactory(databasePath);
      using var client = factory.CreateClient();
      client.DefaultRequestHeaders.Authorization =
          new AuthenticationHeaderValue("Bearer", InternalBearerSecret);
      var store = factory.Services.GetRequiredService<SqliteRelayStore>();
      var nodeA = Guid.NewGuid();
      var nodeB = Guid.NewGuid();
      var pollAt = DateTimeOffset.Parse(
          "2026-08-01T00:01:00+00:00",
          CultureInfo.InvariantCulture);
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest(
              "tenant-a",
              nodeA,
              RelayCredentialHash.Hash("credential-a")),
          cancellationToken);
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest(
              "tenant-b",
              nodeB,
              RelayCredentialHash.Hash("credential-b")),
          cancellationToken);
      await store.PollAsync(
          nodeA,
          "credential-a",
          pollAt,
          cancellationToken);

      using var response = await client.PostAsJsonAsync(
          "/internal/support/v1/nodes/activity",
          new RelayNodeActivityRequest("tenant-a", [nodeA, nodeB]),
          cancellationToken);
      using var document = JsonDocument.Parse(
          await response.Content.ReadAsStringAsync(cancellationToken));
      var activity = document.RootElement;
      var projected = activity[0];
      var propertyNames = projected
          .EnumerateObject()
          .Select(property => property.Name)
          .ToArray();

      await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
      await Assert.That(activity.GetArrayLength()).IsEqualTo(1);
      await Assert.That(propertyNames).IsEquivalentTo([
        "nodeId",
        "lastPollAt",
        "lastResultAt",
      ]);
      await Assert.That(projected.GetProperty("nodeId").GetGuid())
          .IsEqualTo(nodeA);
      await Assert.That(projected.GetProperty("lastPollAt").GetDateTimeOffset())
          .IsEqualTo(pollAt);
      await Assert.That(projected.GetProperty("lastResultAt").ValueKind)
          .IsEqualTo(JsonValueKind.Null);
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Request_Outcome_Requires_Node_Credential_And_Closed_Disposition(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      await using var factory = CreateFactory(databasePath);
      using var client = factory.CreateClient();
      var store =
          factory.Services.GetRequiredService<SqliteRelayStore>();
      var nodeId = Guid.NewGuid();
      var sessionId = Guid.NewGuid();
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest(
              "tenant-a",
              nodeId,
              RelayCredentialHash.Hash("credential-a")),
          cancellationToken);
      await store.EnqueueSessionAsync(
          new RelaySessionEnqueueRequest(
              "tenant-a",
              nodeId,
              sessionId,
              DateTimeOffset.Parse(
                  "2026-08-01T00:05:00+00:00",
                  CultureInfo.InvariantCulture),
              "opaque-request"),
          cancellationToken);
      var route =
          $"/api/support-relay/v1/nodes/{nodeId:D}/sessions/{sessionId:D}/outcome";

      using var unauthorized = await client.PostAsJsonAsync(
          route,
          new SupportRelayRequestOutcomeRequest(
              SupportRequestRejectionDispositions
                  .RequestMalformed),
          cancellationToken);
      using var invalidRequest = new HttpRequestMessage(
          HttpMethod.Post,
          route)
      {
        Content = JsonContent.Create(
            new SupportRelayRequestOutcomeRequest(
                "unbounded-reason")),
      };
      invalidRequest.Headers.Authorization =
          new AuthenticationHeaderValue(
              "Bearer",
              "credential-a");
      using var invalid = await client.SendAsync(
          invalidRequest,
          cancellationToken);
      using var validRequest = new HttpRequestMessage(
          HttpMethod.Post,
          route)
      {
        Content = JsonContent.Create(
            new SupportRelayRequestOutcomeRequest(
                SupportRequestRejectionDispositions
                    .RequestMalformed)),
      };
      validRequest.Headers.Authorization =
          new AuthenticationHeaderValue(
              "Bearer",
              "credential-a");
      using var valid = await client.SendAsync(
          validRequest,
          cancellationToken);
      client.DefaultRequestHeaders.Authorization =
          new AuthenticationHeaderValue(
              "Bearer",
              InternalBearerSecret);
      using var state = await client.GetAsync(
          $"/internal/support/v1/sessions/{sessionId:D}",
          cancellationToken);
      using var document = JsonDocument.Parse(
          await state.Content.ReadAsStringAsync(
              cancellationToken));

      await Assert.That(unauthorized.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
      await Assert.That(invalid.StatusCode)
          .IsEqualTo(HttpStatusCode.BadRequest);
      await Assert.That(valid.StatusCode)
          .IsEqualTo(HttpStatusCode.NoContent);
      await Assert.That(state.StatusCode)
          .IsEqualTo(HttpStatusCode.OK);
      await Assert.That(
              document.RootElement
                  .GetProperty("status")
                  .GetString())
          .IsEqualTo("rejected");
      await Assert.That(
              document.RootElement
                  .GetProperty("rejectionDisposition")
                  .GetString())
          .IsEqualTo(
              SupportRequestRejectionDispositions
                  .RequestMalformed);
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  private const string InternalBearerSecret =
      "relay-activity-test-secret";

  private static WebApplicationFactory<Program> CreateFactory(
      string databasePath) =>
      new WebApplicationFactory<Program>()
          .WithWebHostBuilder(builder =>
          {
            builder.UseSetting("SupportRelay:DatabasePath", databasePath);
            builder.UseSetting(
                "SupportRelay:InternalBearerSecret",
                InternalBearerSecret);
          });

  private static string CreateDatabasePath() =>
      Path.Combine(
          AppContext.BaseDirectory,
          $"support-relay-api-{Guid.NewGuid():N}.db");

  private static void DeleteDatabase(string databasePath)
  {
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    foreach (var path in new[]
    {
      databasePath,
      $"{databasePath}-shm",
      $"{databasePath}-wal",
    })
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
  }
}
