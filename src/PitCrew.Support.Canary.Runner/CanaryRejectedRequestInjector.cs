using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Canary.Runner;

internal static class CanaryRejectedRequestInjector
{
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  public static async Task RunAsync(
      string runRoot,
      string runId,
      Uri relayInternalUrl,
      string relaySecret,
      string dashboardAuthorizationKey,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    var requestPath = Path.Combine(
        runRoot,
        CanaryRejectedRequestControlFile.RequestFileName);
    var resultPath = Path.Combine(
        runRoot,
        CanaryRejectedRequestControlFile.ResultFileName);
    using var client = new HttpClient
    {
      BaseAddress = relayInternalUrl,
      Timeout = TimeSpan.FromSeconds(10),
    };
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue(
            "Bearer",
            relaySecret);
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(100),
        timeProvider);
    while (await timer.WaitForNextTickAsync(cancellationToken))
    {
      if (!File.Exists(requestPath))
      {
        continue;
      }
      var request =
          CanaryRejectedRequestControlFile.ReadRequest(
              requestPath);
      if (!string.Equals(
              request.RunId,
              runId,
              StringComparison.Ordinal) ||
          File.Exists(resultPath))
      {
        throw new InvalidDataException(
            "The rejected-request control does not match the active run.");
      }
      CanaryRejectedRequestControlResult result;
      try
      {
        result = await ExecuteAsync(
            request,
            client,
            dashboardAuthorizationKey,
            timeProvider,
            cancellationToken);
      }
      catch (CryptographicException)
      {
        result = CreateResult(
            request,
            succeeded: false,
            "request-control-rejected");
      }
      catch (HttpRequestException)
      {
        result = CreateResult(
            request,
            succeeded: false,
            "request-control-rejected");
      }
      catch (TaskCanceledException)
          when (!cancellationToken.IsCancellationRequested)
      {
        result = CreateResult(
            request,
            succeeded: false,
            "request-control-rejected");
      }
      CanaryRejectedRequestControlFile.WriteResult(
          resultPath,
          result);
      File.Delete(requestPath);
    }
  }

  private static async Task<CanaryRejectedRequestControlResult>
      ExecuteAsync(
          CanaryRejectedRequestControlRequest request,
          HttpClient client,
          string dashboardAuthorizationKey,
          TimeProvider timeProvider,
          CancellationToken cancellationToken)
  {
    var disposition =
        CanaryRejectedRequestControlFile.IsEnqueue(request)
            ? await EnqueueAsync(
                request,
                client,
                dashboardAuthorizationKey,
                timeProvider,
                cancellationToken)
            : await CancelAsync(
                request.SessionId,
                client,
                cancellationToken);
    return CreateResult(
        request,
        disposition is not null,
        disposition ?? "request-control-rejected");
  }

  private static async Task<string?> EnqueueAsync(
      CanaryRejectedRequestControlRequest request,
      HttpClient client,
      string dashboardAuthorizationKey,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    using var dashboardSigning =
        SupportKeyFactory.ImportEcdsaPrivateKey(
            dashboardAuthorizationKey);
    var envelope =
        CanaryRejectedRequestEnvelopeFactory.Create(
            request,
            dashboardSigning,
            timeProvider.GetUtcNow());
    using var response = await client.PostAsJsonAsync(
        "/internal/support/v1/sessions",
        new
        {
          TenantId = "local",
          NodeId = request.NodeId!.Value,
          request.SessionId,
          ExpiresAt =
              timeProvider.GetUtcNow().AddMinutes(5),
          RequestEnvelope = JsonSerializer.Serialize(
              envelope,
              _jsonOptions),
        },
        _jsonOptions,
        cancellationToken);
    return response.StatusCode == HttpStatusCode.Accepted
        ? "request-enqueued"
        : null;
  }

  private static async Task<string?> CancelAsync(
      Guid sessionId,
      HttpClient client,
      CancellationToken cancellationToken)
  {
    using var response = await client.PostAsync(
        $"/internal/support/v1/sessions/{sessionId:D}/cancel",
        null,
        cancellationToken);
    return response.StatusCode == HttpStatusCode.NoContent
        ? "request-cancelled"
        : null;
  }

  private static CanaryRejectedRequestControlResult CreateResult(
      CanaryRejectedRequestControlRequest request,
      bool succeeded,
      string disposition) =>
      new(
          CanaryRejectedRequestControlFile.SchemaVersion,
          request.RunId,
          request.RequestId,
          succeeded ? "succeeded" : "failed",
          disposition);
}
