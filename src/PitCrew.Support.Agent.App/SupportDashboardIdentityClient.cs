using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using PitCrew.Support.Protocol;

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
    var completion = await ReadResponseOrNullAsync<SupportEnrollmentCompletionResponse>(
        response.Content,
        cancellationToken);
    return completion is null ||
        !Guid.TryParse(
            completion.NodeId,
            CultureInfo.InvariantCulture,
            out var nodeId) ||
        nodeId == Guid.Empty ||
        !HasValue(completion.DisplayName) ||
        completion.TransportCredentialEnvelope is null ||
        !IsEnvelopeComplete(completion.TransportCredentialEnvelope) ||
        !HasValue(completion.RelayUrl) ||
        !HasValue(completion.AuthorizationSigningPublicKeySpki) ||
        !HasValue(completion.ResultEncryptionPublicKeySpki)
            ? null
            : new SupportEnrollmentCompletionData(
                nodeId,
                completion.DisplayName!,
                completion.TransportCredentialEnvelope,
                completion.RelayUrl!,
                completion.AuthorizationSigningPublicKeySpki!,
                completion.ResultEncryptionPublicKeySpki!);
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
    return await ReadIdentityCompletionOrNullAsync(
        response.Content,
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
    return await ReadIdentityCompletionOrNullAsync(
        response.Content,
        cancellationToken);
  }

  private static async Task<SupportIdentityCompletionData?>
      ReadIdentityCompletionOrNullAsync(
          HttpContent content,
          CancellationToken cancellationToken)
  {
    var completion = await ReadResponseOrNullAsync<SupportIdentityCompletionResponse>(
        content,
        cancellationToken);
    return completion is null ||
        !Guid.TryParse(
            completion.NodeId,
            CultureInfo.InvariantCulture,
            out var nodeId) ||
        nodeId == Guid.Empty ||
        !HasValue(completion.DisplayName) ||
        !HasValue(completion.TransportCredential) ||
        !HasValue(completion.RelayUrl) ||
        !HasValue(completion.AuthorizationSigningPublicKeySpki) ||
        !HasValue(completion.ResultEncryptionPublicKeySpki)
            ? null
            : new SupportIdentityCompletionData(
                nodeId,
                completion.DisplayName!,
                completion.TransportCredential!,
                completion.RelayUrl!,
                completion.AuthorizationSigningPublicKeySpki!,
                completion.ResultEncryptionPublicKeySpki!);
  }

  private static async Task<T?> ReadResponseOrNullAsync<T>(
      HttpContent content,
      CancellationToken cancellationToken)
  {
    T? response;
    try
    {
      response = await content.ReadFromJsonAsync<T>(
          cancellationToken: cancellationToken);
    }
    catch (JsonException)
    {
      response = default;
    }
    return response;
  }

  private static bool HasValue(string? value) =>
      !string.IsNullOrWhiteSpace(value);

  private static bool IsEnvelopeComplete(SupportEnvelope envelope) =>
      HasValue(envelope.EnvelopeVersion) &&
      HasValue(envelope.ContentEncryptionAlgorithm) &&
      HasValue(envelope.KeyWrapAlgorithm) &&
      HasValue(envelope.SignatureAlgorithm) &&
      HasValue(envelope.SenderKeyId) &&
      HasValue(envelope.RecipientKeyId) &&
      HasValue(envelope.WrappedKeyBase64Url) &&
      HasValue(envelope.NonceBase64Url) &&
      HasValue(envelope.CiphertextBase64Url) &&
      HasValue(envelope.TagBase64Url) &&
      HasValue(envelope.SignatureBase64Url);

}
