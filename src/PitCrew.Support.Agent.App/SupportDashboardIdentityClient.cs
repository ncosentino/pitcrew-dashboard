using System.Net.Http.Json;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportDashboardIdentityClient(
    IHttpClientFactory _httpClientFactory)
{
  public async Task<SupportEnrollmentCompletionData?> CompleteEnrollmentAsync(
      Uri dashboardUrl,
      string tenantId,
      string enrollmentCode,
      Guid completionId,
      SupportNodeKeyDescriptor keys,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(
        SupportDashboardIdentityHttpClientOptions.ClientName);
    using var response = await client.PostAsJsonAsync(
        new Uri(dashboardUrl, "/api/support-agent/v1/enrollments/complete"),
        new
        {
          TenantId = tenantId,
          EnrollmentCode = enrollmentCode,
          CompletionId = completionId,
          NodeSigningPublicKeySpki = keys.SigningPublicKeySpki,
          NodeEncryptionPublicKeySpki = keys.EncryptionPublicKeySpki,
        },
        cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      return null;
    }
    return await response.Content.ReadFromJsonAsync<SupportEnrollmentCompletionData>(
        cancellationToken);
  }

  public async Task<SupportIdentityCompletionData?> PrepareRotationAsync(
      SupportIdentityRotationPlan rotation,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(
        SupportDashboardIdentityHttpClientOptions.ClientName);
    using var response = await client.PostAsJsonAsync(
        new Uri(
            new Uri(rotation.DashboardUrl, UriKind.Absolute),
            $"/api/support-agent/v1/identities/{rotation.NodeId:D}/rotate"),
        new
        {
          rotation.RotationId,
          rotation.TenantId,
          rotation.CurrentTransportCredential,
          rotation.ReplacementTransportCredential,
          rotation.NodeSigningPublicKeySpki,
          rotation.NodeEncryptionPublicKeySpki,
        },
        cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      return null;
    }
    return await response.Content.ReadFromJsonAsync<SupportIdentityCompletionData>(
        cancellationToken);
  }

  public async Task<SupportIdentityCompletionData?> FinalizeRotationAsync(
      PendingSupportIdentityRotation rotation,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(
        SupportDashboardIdentityHttpClientOptions.ClientName);
    using var response = await client.PostAsJsonAsync(
        new Uri(
            new Uri(rotation.DashboardUrl, UriKind.Absolute),
            $"/api/support-agent/v1/identities/{rotation.NodeId:D}/rotate/finalize"),
        new
        {
          rotation.RotationId,
          rotation.TenantId,
          rotation.CurrentTransportCredential,
        },
        cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      return null;
    }
    return await response.Content.ReadFromJsonAsync<SupportIdentityCompletionData>(
        cancellationToken);
  }
}
