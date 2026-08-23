using System.Globalization;
using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text.Json;

using PitCrew.Dashboard.Features.Support;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

internal static class SupportEnrollmentTestHelper
{
  public static async Task<SupportIdentityCompletionResponse> EnrollAsync(
      HttpClient client,
      string antiforgeryToken,
      SupportNodeKeySet nodeKeys,
      CancellationToken cancellationToken)
  {
    var enrollment = await CreateAuthorizationAsync(
        client,
        antiforgeryToken,
        cancellationToken);
    var completionId = Guid.NewGuid();
    using var completeResponse = await client.PostAsJsonAsync(
        "/api/support-agent/v1/enrollments/complete",
        new CompleteSupportEnrollmentRequest(
            DashboardTestHelpers.TenantId,
            enrollment.EnrollmentCode,
            completionId,
            nodeKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
            nodeKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url),
        cancellationToken);
    completeResponse.EnsureSuccessStatusCode();
    var completed = await completeResponse.Content
        .ReadFromJsonAsync<SupportEnrollmentCompletionResponse>(cancellationToken) ??
        throw new InvalidOperationException(
            "Support enrollment completion response was empty.");
    using var dashboardSigning = SupportKeyFactory.ImportEcdsaPublicKey(
        completed.AuthorizationSigningPublicKeySpki);
    using var nodeEncryption = SupportKeyFactory.ImportRsaPrivateKey(
        nodeKeys.Encryption.PrivateKeyPkcs8Base64Url);
    var payloadBytes = SupportEnvelopeCryptography.OpenOrNull(
        completed.TransportCredentialEnvelope,
        dashboardSigning,
        nodeEncryption) ?? throw new InvalidOperationException(
            "Support enrollment credential envelope was invalid.");
    try
    {
      var payload = JsonSerializer.Deserialize<EnrollmentCredentialPayload>(
          payloadBytes,
          new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
          throw new InvalidOperationException(
              "Support enrollment credential payload was invalid.");
      if (payload.NodeId != Guid.Parse(
              completed.NodeId,
              CultureInfo.InvariantCulture) ||
          payload.CompletionId != completionId)
      {
        throw new InvalidOperationException(
            "Support enrollment credential payload was not bound to the completion.");
      }
      return new SupportIdentityCompletionResponse(
          completed.NodeId,
          completed.DisplayName,
          payload.TransportCredential,
          completed.RelayUrl,
          completed.AuthorizationSigningPublicKeySpki,
          completed.ResultEncryptionPublicKeySpki);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(payloadBytes);
    }
  }

  public static async Task<CreatedSupportEnrollmentAuthorizationResponse>
      CreateAuthorizationAsync(
          HttpClient client,
          string antiforgeryToken,
          CancellationToken cancellationToken)
  {
    using var createRequest = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/enrollment-authorizations")
    {
      Content = JsonContent.Create(
          new CreateSupportEnrollmentAuthorizationRequest("Support node")),
    };
    createRequest.Headers.Add(
        DashboardTestHelpers.AntiforgeryHeader,
        antiforgeryToken);
    using var createResponse = await client.SendAsync(
        createRequest,
        cancellationToken);
    createResponse.EnsureSuccessStatusCode();
    return await createResponse.Content
        .ReadFromJsonAsync<CreatedSupportEnrollmentAuthorizationResponse>(
            cancellationToken) ??
        throw new InvalidOperationException(
            "Support enrollment authorization response was empty.");
  }

  private sealed record EnrollmentCredentialPayload(
      string Schema,
      Guid NodeId,
      Guid CompletionId,
      string TransportCredential);
}
