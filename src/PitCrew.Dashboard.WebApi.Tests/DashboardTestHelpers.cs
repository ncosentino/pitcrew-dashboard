using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Fleet;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

internal static class DashboardTestHelpers
{
  public const string TenantId = "local";
  public const string AntiforgeryHeader =
      "X-PitCrew-Antiforgery";

  public static async Task<DashboardSessionResponse> GetSessionAsync(
      HttpClient client,
      CancellationToken cancellationToken)
  {
    using var response = await client.GetAsync(
        "/api/session",
        cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      var error = await response.Content.ReadAsStringAsync(
          cancellationToken);
      throw new InvalidOperationException(
          $"Session request returned {(int)response.StatusCode}: {error}");
    }
    return await response.Content.ReadFromJsonAsync<
        DashboardSessionResponse>(
            cancellationToken) ??
        throw new InvalidOperationException(
            "Session response was empty.");
  }

  public static async Task<CreateEnrollmentCodeResponse>
      CreateEnrollmentCodeAsync(
          HttpClient client,
          string antiforgeryToken,
          string tenantId,
          string label,
          CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/tenants/{tenantId}/fleet/v1/enrollment-codes")
    {
      Content = JsonContent.Create(
          new CreateEnrollmentCodeRequest(label)),
    };
    request.Headers.Add(
        AntiforgeryHeader,
        antiforgeryToken);
    using var response = await client.SendAsync(
        request,
        cancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<
        CreateEnrollmentCodeResponse>(
            cancellationToken) ??
        throw new InvalidOperationException(
            "Enrollment-code response was empty.");
  }

  public static async Task<ConnectorEnrollmentResponse> EnrollAsync(
      HttpClient client,
      string connectorInstanceId,
      string displayName,
      string enrollmentCode,
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
        "X-PitCrew-Enrollment-Code",
        enrollmentCode);
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

  public static async Task<ConnectorSyncResponse> SynchronizeAsync(
      HttpClient client,
      string credential,
      string connectorVersion,
      ManagerObservedState observedState,
      CancellationToken cancellationToken)
  {
    using var response = await SendSynchronizationAsync(
        client,
        credential,
        connectorVersion,
        observedState,
        cancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<
        ConnectorSyncResponse>(
            cancellationToken) ??
        throw new InvalidOperationException(
            "Synchronization response was empty.");
  }

  public static async Task<HttpResponseMessage> SendSynchronizationAsync(
      HttpClient client,
      string credential,
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
            credential);
    return await client.SendAsync(
        synchronization,
        cancellationToken);
  }

  public static async Task<HttpResponseMessage> PostAuthenticatedAsync(
      HttpClient client,
      string path,
      string antiforgeryToken,
      object? body,
      CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        path);
    request.Headers.Add(
        AntiforgeryHeader,
        antiforgeryToken);
    if (body is not null)
    {
      request.Content = JsonContent.Create(body);
    }
    return await client.SendAsync(
        request,
        cancellationToken);
  }

  public static ManagerObservedState CreateObservedState(
      string profileId,
      string repository)
  {
    var observedAt = DateTimeOffset.UtcNow;
    return new ManagerObservedState(
        1,
        7,
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
                    observedAt,
                    new ResourceUsage(
                        1.25,
                        1_073_741_824,
                        48)),
        ],
        new ManagerResourceTelemetry(
            observedAt,
            "available",
            new HostResourceCapacity(
                8,
                34_359_738_368),
            new ResourceUsage(
                0.5,
                201_326_592,
                11)));
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
  private const string AuthenticationModeKey =
      "PitCrew__Authentication__Mode";
  private const string DataProtectionKeyPathKey =
      "PitCrew__Authentication__DataProtectionKeyPath";
  private const string GitHubClientIdKey =
      "PitCrew__Authentication__GitHubClientId";
  private const string GitHubClientSecretKey =
      "PitCrew__Authentication__GitHubClientSecret";
  private const string SystemAdministratorKey =
      "PitCrew__Authentication__SystemAdministratorGitHubIds__0";
  private const string DatabasePathKey =
      "PitCrew__Sqlite__DatabasePath";
  private const string EnvironmentKey =
      "ASPNETCORE_ENVIRONMENT";
  private readonly string? _previousAuthenticationMode =
      Environment.GetEnvironmentVariable(AuthenticationModeKey);
  private readonly string? _previousDataProtectionKeyPath =
      Environment.GetEnvironmentVariable(DataProtectionKeyPathKey);
  private readonly string? _previousGitHubClientId =
      Environment.GetEnvironmentVariable(GitHubClientIdKey);
  private readonly string? _previousGitHubClientSecret =
      Environment.GetEnvironmentVariable(GitHubClientSecretKey);
  private readonly string? _previousSystemAdministrator =
      Environment.GetEnvironmentVariable(SystemAdministratorKey);
  private readonly string? _previousDatabasePath =
      Environment.GetEnvironmentVariable(DatabasePathKey);
  private readonly string? _previousEnvironment =
      Environment.GetEnvironmentVariable(EnvironmentKey);
  private readonly string _dataProtectionKeyPath;

  public TestConfigurationScope(string databasePath)
      : this(
          databasePath,
          "Development",
          string.Empty,
          string.Empty,
          string.Empty)
  {
  }

  public TestConfigurationScope(
      string databasePath,
      string authenticationMode,
      string githubClientId,
      string githubClientSecret,
      string systemAdministratorGitHubId)
      : this(
          databasePath,
          authenticationMode,
          githubClientId,
          githubClientSecret,
          systemAdministratorGitHubId,
          "Development")
  {
  }

  public TestConfigurationScope(
      string databasePath,
      string authenticationMode,
      string githubClientId,
      string githubClientSecret,
      string systemAdministratorGitHubId,
      string hostEnvironment)
  {
    _dataProtectionKeyPath = $"{databasePath}.keys";
    Environment.SetEnvironmentVariable(
        AuthenticationModeKey,
        authenticationMode);
    Environment.SetEnvironmentVariable(
        DataProtectionKeyPathKey,
        _dataProtectionKeyPath);
    Environment.SetEnvironmentVariable(
        GitHubClientIdKey,
        string.IsNullOrWhiteSpace(githubClientId)
            ? null
            : githubClientId);
    Environment.SetEnvironmentVariable(
        GitHubClientSecretKey,
        string.IsNullOrWhiteSpace(githubClientSecret)
            ? null
            : githubClientSecret);
    Environment.SetEnvironmentVariable(
        SystemAdministratorKey,
        string.IsNullOrWhiteSpace(systemAdministratorGitHubId)
            ? null
            : systemAdministratorGitHubId);
    Environment.SetEnvironmentVariable(
        DatabasePathKey,
        databasePath);
    Environment.SetEnvironmentVariable(
        EnvironmentKey,
        hostEnvironment);
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable(
        AuthenticationModeKey,
        _previousAuthenticationMode);
    Environment.SetEnvironmentVariable(
        DataProtectionKeyPathKey,
        _previousDataProtectionKeyPath);
    Environment.SetEnvironmentVariable(
        GitHubClientIdKey,
        _previousGitHubClientId);
    Environment.SetEnvironmentVariable(
        GitHubClientSecretKey,
        _previousGitHubClientSecret);
    Environment.SetEnvironmentVariable(
        SystemAdministratorKey,
        _previousSystemAdministrator);
    Environment.SetEnvironmentVariable(
        DatabasePathKey,
        _previousDatabasePath);
    Environment.SetEnvironmentVariable(
        EnvironmentKey,
        _previousEnvironment);
    if (Directory.Exists(_dataProtectionKeyPath))
    {
      Directory.Delete(
          _dataProtectionKeyPath,
          true);
    }
  }
}
