using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Data.Sqlite;

using PitCrew.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

internal static class DashboardTestHelpers
{
  public const string EnrollmentToken =
      "integration-enrollment-token";

  public static async Task<ConnectorEnrollmentResponse> EnrollAsync(
      HttpClient client,
      string connectorInstanceId,
      string displayName,
      string enrollmentToken,
      CancellationToken cancellationToken)
  {
    using var enrollment = new HttpRequestMessage(
        HttpMethod.Post,
        "/api/connectors/v1/enroll")
    {
      Content = JsonContent.Create(new ConnectorEnrollmentRequest(
            connectorInstanceId,
            displayName)),
    };
    enrollment.Headers.Add(
        "X-PitCrew-Enrollment-Token",
        enrollmentToken);
    using var response = await client.SendAsync(
        enrollment,
        cancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content
        .ReadFromJsonAsync<ConnectorEnrollmentResponse>(
            cancellationToken) ??
        throw new InvalidOperationException(
            "Enrollment response was empty.");
  }

  public static async Task SynchronizeAsync(
      HttpClient client,
      ConnectorEnrollmentResponse identity,
      string connectorVersion,
      ManagerObservedState observedState,
      CancellationToken cancellationToken)
  {
    using var synchronization = new HttpRequestMessage(
        HttpMethod.Post,
        "/api/connectors/v1/sync")
    {
      Content = JsonContent.Create(new ConnectorSyncRequest(
            PitCrewProtocol.Version,
            connectorVersion,
            observedState.ObservedAt,
            [observedState])),
    };
    synchronization.Headers.Authorization =
        new AuthenticationHeaderValue(
            "Bearer",
            identity.Credential);
    using var response = await client.SendAsync(
        synchronization,
        cancellationToken);
    response.EnsureSuccessStatusCode();
  }

  public static ManagerObservedState CreateObservedState(
      string profileId,
      string repository)
  {
    var observedAt = DateTimeOffset.UtcNow;
    return new ManagerObservedState(
        1,
        5,
        profileId,
        Guid.NewGuid().ToString("D"),
        "running",
        observedAt,
        "repo",
        3,
        new string('a', 64),
        "accepted",
        1,
        1,
        0,
        [
            new ObservedSlotState(
                    $"repo-{Guid.NewGuid():N}-000001",
                    repository,
                    true,
                    true,
                    "online",
                    0,
                    0,
                    observedAt),
        ]);
  }

  public static string CreateDatabasePath() =>
      Path.Combine(
          Path.GetTempPath(),
          $"pitcrew-dashboard-{Guid.NewGuid():N}.db");

  public static void DeleteDatabase(string databasePath)
  {
    SqliteConnection.ClearAllPools();
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

internal sealed class TestConfigurationScope : IDisposable
{
  private const string EnrollmentTokenKey =
      "PitCrew__Dashboard__EnrollmentToken";
  private const string DatabasePathKey =
      "PitCrew__Sqlite__DatabasePath";
  private readonly string? _previousEnrollmentToken =
      Environment.GetEnvironmentVariable(EnrollmentTokenKey);
  private readonly string? _previousDatabasePath =
      Environment.GetEnvironmentVariable(DatabasePathKey);

  public TestConfigurationScope(string databasePath)
  {
    Environment.SetEnvironmentVariable(
        EnrollmentTokenKey,
        DashboardTestHelpers.EnrollmentToken);
    Environment.SetEnvironmentVariable(
        DatabasePathKey,
        databasePath);
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable(
        EnrollmentTokenKey,
        _previousEnrollmentToken);
    Environment.SetEnvironmentVariable(
        DatabasePathKey,
        _previousDatabasePath);
  }
}
