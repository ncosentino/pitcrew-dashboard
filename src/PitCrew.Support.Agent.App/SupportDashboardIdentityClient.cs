using System.Buffers;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportDashboardIdentityClient(
    IHttpClientFactory _httpClientFactory,
    TimeProvider _timeProvider)
{
  private const int MaximumErrorResponseBytes = 1024;

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
      CancellationToken cancellationToken) =>
      (await PrepareRotationWithOutcomeAsync(rotation, cancellationToken)).Completion;

  internal Task<SupportIdentityRotationResponse> PrepareRotationWithOutcomeAsync(
      SupportIdentityRotationPlan rotation,
      CancellationToken cancellationToken) =>
      PostRotationAsync(
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

  public async Task<SupportIdentityCompletionData?> FinalizeRotationAsync(
      PendingSupportIdentityRotation rotation,
      CancellationToken cancellationToken) =>
      (await FinalizeRotationWithOutcomeAsync(rotation, cancellationToken)).Completion;

  internal Task<SupportIdentityRotationResponse> FinalizeRotationWithOutcomeAsync(
      PendingSupportIdentityRotation rotation,
      CancellationToken cancellationToken) =>
      PostRotationAsync(
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

  private async Task<SupportIdentityRotationResponse> PostRotationAsync<T>(
      Uri endpoint,
      T request,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(
        SupportDashboardIdentityHttpClientOptions.ClientName);
    using var deadline = new CancellationTokenSource(client.Timeout, _timeProvider);
    using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken, deadline.Token);
    try
    {
      using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
      {
        Content = JsonContent.Create(request),
      };
      using var response = await client.SendAsync(
          message, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token);
      if (!response.IsSuccessStatusCode)
      {
        var statusCode = (int)response.StatusCode;
        var disposition = statusCode is >= 100 and <= 599
            ? $"http-rejected-{statusCode.ToString(CultureInfo.InvariantCulture)}"
            : "http-rejected";
        var knownCode = await ReadKnownRotationErrorAsync(
            response.Content, statusCode, requestCancellation.Token, cancellationToken);
        return new(null, knownCode is null ? disposition : $"{disposition}-{knownCode}");
      }
      var completion = await ReadIdentityCompletionOrNullAsync(
          response.Content, requestCancellation.Token);
      return new(completion, completion is null ? "response-invalid" : null);
    }
    catch (HttpRequestException)
    {
      return new(null, "transport-failed");
    }
    catch (IOException)
    {
      return new(null, "transport-failed");
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      return new(null, "http-timeout");
    }
  }

  private static async Task<string?> ReadKnownRotationErrorAsync(
      HttpContent content,
      int statusCode,
      CancellationToken requestCancellation,
      CancellationToken callerCancellation)
  {
    if (content.Headers.ContentLength is > MaximumErrorResponseBytes)
    {
      return null;
    }
    using var buffer = MemoryPool<byte>.Shared.Rent(MaximumErrorResponseBytes + 1);
    var memory = buffer.Memory[..(MaximumErrorResponseBytes + 1)];
    try
    {
      await using var stream = await content.ReadAsStreamAsync(requestCancellation);
      var count = 0;
      while (count < memory.Length)
      {
        var read = await stream.ReadAsync(memory[count..], requestCancellation);
        if (read == 0)
        {
          break;
        }
        count += read;
      }
      if (count > MaximumErrorResponseBytes)
      {
        return null;
      }
      using var document = JsonDocument.Parse(
          memory[..count], new JsonDocumentOptions { MaxDepth = 4 });
      var root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object ||
          !root.TryGetProperty("error", out var error) ||
          error.ValueKind != JsonValueKind.Object)
      {
        return null;
      }
      using var rootProperties = root.EnumerateObject();
      using var errorProperties = error.EnumerateObject();
      if (rootProperties.Count(property => property.NameEquals("error")) != 1 ||
          errorProperties.Count(property => property.NameEquals("code")) != 1 ||
          !error.TryGetProperty("code", out var code) ||
          code.ValueKind != JsonValueKind.String)
      {
        return null;
      }
      return statusCode switch
      {
        409 when code.ValueEquals("support_identity_revoked") => "identity-revoked",
        409 when code.ValueEquals("support_identity_conflict") => "identity-conflict",
        400 when code.ValueEquals("invalid_support_identity_request") => "identity-request-invalid",
        _ => null,
      };
    }
    catch (JsonException)
    {
      return null;
    }
    catch (IOException)
    {
      return null;
    }
    catch (HttpRequestException)
    {
      return null;
    }
    catch (OperationCanceledException) when (!callerCancellation.IsCancellationRequested)
    {
      // Headers already establish rejection; failed optional metadata cannot erase it.
      return null;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(buffer.Memory.Span);
    }
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
