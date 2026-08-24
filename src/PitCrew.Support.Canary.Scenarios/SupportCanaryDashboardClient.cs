using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Support;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class SupportCanaryDashboardClient : IDisposable
{
  private const string TenantId = "local";
  internal const string EnrollmentDisplayName = "Canary support node";
  private const string AntiforgeryHeader =
      "X-PitCrew-Antiforgery";
  private readonly HttpClient _client;

  public SupportCanaryDashboardClient(string dashboardUrl)
  {
    var handler = new HttpClientHandler
    {
      AllowAutoRedirect = false,
      CookieContainer = new CookieContainer(),
      UseCookies = true,
    };
    _client = new HttpClient(
        handler,
        disposeHandler: true)
    {
      BaseAddress = new Uri(
          dashboardUrl,
          UriKind.Absolute),
      Timeout = TimeSpan.FromSeconds(30),
      MaxResponseContentBufferSize = 4_194_304,
    };
  }

  public async Task<string> GetAntiforgeryTokenAsync(
      CancellationToken cancellationToken)
  {
    using var response = await _client.GetAsync(
        "/api/session",
        cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      throw new CanaryScenarioFailureException(
          "dashboard-session-rejected");
    }
    var session = await response.Content
        .ReadFromJsonAsync<DashboardSessionResponse>(
            cancellationToken) ??
        throw new CanaryScenarioFailureException(
            "dashboard-session-empty");
    return session.AntiforgeryToken;
  }

  public async Task<CreatedSupportEnrollmentAuthorizationResponse>
      CreateEnrollmentAuthorizationAsync(
          string antiforgeryToken,
          CancellationToken cancellationToken)
      => await CreateEnrollmentAuthorizationAsync(
          antiforgeryToken,
          EnrollmentDisplayName,
          cancellationToken);

  public async Task<CreatedSupportEnrollmentAuthorizationResponse>
      CreateEnrollmentAuthorizationAsync(
          string antiforgeryToken,
          string displayName,
          CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/tenants/{TenantId}/support/v1/enrollment-authorizations")
    {
      Content = JsonContent.Create(
          new CreateSupportEnrollmentAuthorizationRequest(
              displayName)),
    };
    request.Headers.Add(
        AntiforgeryHeader,
        antiforgeryToken);
    using var response = await _client.SendAsync(
        request,
        cancellationToken);
    if (response.StatusCode != HttpStatusCode.Created)
    {
      throw new CanaryScenarioFailureException(
          "enrollment-authorization-rejected");
    }
    return await response.Content.ReadFromJsonAsync<
        CreatedSupportEnrollmentAuthorizationResponse>(
            cancellationToken) ??
        throw new CanaryScenarioFailureException(
            "enrollment-authorization-empty");
  }

  public async Task<SupportEnrollmentCompletionResponse>
      CompleteEnrollmentAsync(
          string enrollmentCode,
          Guid completionId,
          string signingPublicKey,
          string encryptionPublicKey,
          CancellationToken cancellationToken)
  {
    using var response = await _client.PostAsJsonAsync(
        "/api/support-agent/v1/enrollments/complete",
        new CompleteSupportEnrollmentRequest(
            TenantId,
            enrollmentCode,
            completionId,
            signingPublicKey,
            encryptionPublicKey),
        cancellationToken);
    if (response.StatusCode != HttpStatusCode.OK)
    {
      throw new CanaryScenarioFailureException(
          "enrollment-authorization-rejected");
    }
    return await response.Content.ReadFromJsonAsync<
        SupportEnrollmentCompletionResponse>(
            cancellationToken) ??
        throw new CanaryScenarioFailureException(
            "enrollment-authorization-empty");
  }

  public async Task<SupportDiagnosticSessionResponse>
      CreateSupportSessionAsync(
          string antiforgeryToken,
          Guid nodeId,
          CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/tenants/{TenantId}/support/v1/sessions")
    {
      Content = JsonContent.Create(
          new CreateSupportDiagnosticSessionRequest(
              nodeId,
              SupportDiagnosticModes.ConnectorOffline,
              null,
              300)),
    };
    request.Headers.Add(
        AntiforgeryHeader,
        antiforgeryToken);
    using var response = await _client.SendAsync(
        request,
        cancellationToken);
    if (response.StatusCode != HttpStatusCode.Accepted)
    {
      throw new CanaryScenarioFailureException(
          "dashboard-session-rejected");
    }
    return await response.Content.ReadFromJsonAsync<
        SupportDiagnosticSessionResponse>(
            cancellationToken) ??
        throw new CanaryScenarioFailureException(
            "dashboard-session-empty");
  }

  public async Task<SupportDiagnosticSessionResponse>
      GetSupportSessionAsync(
          Guid sessionId,
          CancellationToken cancellationToken)
  {
    using var response = await _client.GetAsync(
        $"/api/tenants/{TenantId}/support/v1/sessions/{sessionId:D}",
        cancellationToken);
    if (response.StatusCode != HttpStatusCode.OK)
    {
      throw new CanaryScenarioFailureException(
          "dashboard-session-rejected");
    }
    return await response.Content.ReadFromJsonAsync<
        SupportDiagnosticSessionResponse>(
            cancellationToken) ??
        throw new CanaryScenarioFailureException(
            "dashboard-session-empty");
  }

  public async Task CancelSupportSessionAsync(
      string antiforgeryToken,
      Guid sessionId,
      CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/tenants/{TenantId}/support/v1/sessions/{sessionId:D}/cancel");
    request.Headers.Add(
        AntiforgeryHeader,
        antiforgeryToken);
    using var response = await _client.SendAsync(
        request,
        cancellationToken);
    if (response.StatusCode != HttpStatusCode.NoContent)
    {
      throw new CanaryScenarioFailureException(
          "dashboard-session-rejected");
    }
  }

  public async Task<DiagnosticCredentialCreatedResponse>
      CreateDiagnosticCredentialAsync(
          string antiforgeryToken,
          DateTimeOffset expiresAt,
          CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/tenants/{TenantId}/diagnostic-credentials")
    {
      Content = JsonContent.Create(
          new CreateDiagnosticCredentialRequest(
              "support canary",
              expiresAt,
              [],
              [])),
    };
    request.Headers.Add(
        AntiforgeryHeader,
        antiforgeryToken);
    using var response = await _client.SendAsync(
        request,
        cancellationToken);
    if (response.StatusCode != HttpStatusCode.Created)
    {
      throw new CanaryScenarioFailureException(
          "diagnostic-credential-rejected");
    }

    return await response.Content.ReadFromJsonAsync<
        DiagnosticCredentialCreatedResponse>(
            cancellationToken) ??
        throw new CanaryScenarioFailureException(
            "diagnostic-credential-empty");
  }

  public async Task<Guid> GetEnrolledNodeIdWithActivityAsync(
      bool requireResult,
      CancellationToken cancellationToken)
  {
    var identity = await GetEnrolledIdentityAsync(cancellationToken);
    if (!Guid.TryParse(
            identity.NodeId,
            CultureInfo.InvariantCulture,
            out var nodeId) ||
        nodeId == Guid.Empty)
    {
      throw new CanaryScenarioFailureException(
          "support-identity-inventory-invalid");
    }
    RequireActivity(identity, requireResult);
    return nodeId;
  }

  public async Task RequireIdentityActivityAsync(
      Guid nodeId,
      bool requireResult,
      CancellationToken cancellationToken)
  {
    var identity = await GetEnrolledIdentityAsync(cancellationToken);
    if (!Guid.TryParse(
            identity.NodeId,
            CultureInfo.InvariantCulture,
            out var enrolledNodeId) ||
        enrolledNodeId != nodeId)
    {
      throw new CanaryScenarioFailureException(
          "support-identity-inventory-invalid");
    }
    RequireActivity(identity, requireResult);
  }

  private static void RequireActivity(
      SupportIdentityResponse identity,
      bool requireResult)
  {
    if (identity.LastPollAt is null)
    {
      throw new CanaryScenarioFailureException(
          "support-identity-last-poll-missing");
    }
    if (requireResult && identity.LastResultAt is null)
    {
      throw new CanaryScenarioFailureException(
          "support-identity-last-result-missing");
    }
  }

  private async Task<SupportIdentityResponse> GetEnrolledIdentityAsync(
      CancellationToken cancellationToken)
  {
    using var response = await _client.GetAsync(
        $"/api/tenants/{TenantId}/support/v1/identities",
        cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      throw new CanaryScenarioFailureException(
          "support-identity-inventory-rejected");
    }
    var identities = await response.Content
        .ReadFromJsonAsync<List<SupportIdentityResponse>>(
            cancellationToken) ?? [];
    var matches = identities
        .Where(identity =>
            string.Equals(
                identity.DisplayName,
                EnrollmentDisplayName,
                StringComparison.Ordinal) &&
            string.Equals(
                identity.Status,
                "Active",
                StringComparison.Ordinal))
        .ToArray();
    return matches.Length == 1
                ? matches[0]
                : throw new CanaryScenarioFailureException(
                    "support-identity-inventory-invalid");
  }

  public async Task RevokeAsync(
      string antiforgeryToken,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/tenants/{TenantId}/support/v1/identities/{nodeId:D}/revoke");
    request.Headers.Add(
        AntiforgeryHeader,
        antiforgeryToken);
    using var response = await _client.SendAsync(
        request,
        cancellationToken);
    if (response.StatusCode != HttpStatusCode.NoContent)
    {
      throw new CanaryScenarioFailureException(
          "support-revocation-rejected");
    }
  }

  public void Dispose() => _client.Dispose();
}
