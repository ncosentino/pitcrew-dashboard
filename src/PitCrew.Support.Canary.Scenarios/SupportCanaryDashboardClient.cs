using System.Net;
using System.Net.Http.Json;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Support;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class SupportCanaryDashboardClient : IDisposable
{
  private const string TenantId = "local";
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
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/tenants/{TenantId}/support/v1/enrollment-authorizations")
    {
      Content = JsonContent.Create(
          new CreateSupportEnrollmentAuthorizationRequest(
              "Canary support node")),
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
