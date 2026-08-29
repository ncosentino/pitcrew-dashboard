using System.Buffers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub;

internal static class GitHubHttpResponseReader
{
  internal const int MaximumJsonBytes = 1_048_576;

  public static async Task<GitHubClientOutcome<T>> ReadJsonAsync<T>(
      HttpResponseMessage response,
      JsonTypeInfo<T> jsonTypeInfo,
      int maximumBytes,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    if (!response.IsSuccessStatusCode)
    {
      return Failure<T>(response, timeProvider);
    }
    if (response.Content.Headers.ContentLength is > 0 and var contentLength &&
        contentLength > maximumBytes)
    {
      return InvalidResponse<T>("response-body-oversized");
    }

    var rented = ArrayPool<byte>.Shared.Rent(maximumBytes + 1);
    try
    {
      await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
      var total = 0;
      while (total < rented.Length)
      {
        var read = await stream.ReadAsync(
            rented.AsMemory(total, rented.Length - total),
            cancellationToken);
        if (read == 0)
        {
          break;
        }
        total += read;
      }
      if (total == 0 || total > maximumBytes)
      {
        return InvalidResponse<T>(
            total == 0 ? "response-body-empty" : "response-body-oversized");
      }

      try
      {
        var value = JsonSerializer.Deserialize(
            rented.AsSpan(0, total),
            jsonTypeInfo);
        return value is null
            ? InvalidResponse<T>("response-json-null")
            : new(
                GitHubClientOutcomeKind.Success,
                value,
                null,
                null);
      }
      catch (JsonException)
      {
        return InvalidResponse<T>("response-json-malformed");
      }
      catch (NotSupportedException)
      {
        return InvalidResponse<T>("response-json-unsupported");
      }
    }
    finally
    {
      CryptographicOperations.ZeroMemory(rented);
      ArrayPool<byte>.Shared.Return(rented);
    }
  }

  public static async Task<GitHubClientOutcome<byte[]>> ReadBytesAsync(
      HttpResponseMessage response,
      int maximumBytes,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    if (!response.IsSuccessStatusCode)
    {
      return Failure<byte[]>(response, timeProvider);
    }
    if (response.Content.Headers.ContentLength is > 0 and var contentLength &&
        contentLength > maximumBytes)
    {
      return InvalidResponse<byte[]>("response-body-oversized");
    }

    var rented = ArrayPool<byte>.Shared.Rent(maximumBytes + 1);
    try
    {
      await using var stream =
          await response.Content.ReadAsStreamAsync(cancellationToken);
      var total = 0;
      var readLimit = maximumBytes + 1;
      while (total < readLimit)
      {
        var read = await stream.ReadAsync(
            rented.AsMemory(total, readLimit - total),
            cancellationToken);
        if (read == 0)
        {
          break;
        }
        total += read;
      }
      if (total == 0 || total > maximumBytes)
      {
        return InvalidResponse<byte[]>(
            total == 0 ? "response-body-empty" : "response-body-oversized");
      }

      return new(
          GitHubClientOutcomeKind.Success,
          rented.AsSpan(0, total).ToArray(),
          null,
          null);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(rented);
      ArrayPool<byte>.Shared.Return(rented);
    }
  }

  public static GitHubClientOutcome<T> Failure<T>(
      HttpResponseMessage response,
      TimeProvider timeProvider)
  {
    var statusCode = response.StatusCode;
    if (statusCode == HttpStatusCode.NotFound)
    {
      return new(GitHubClientOutcomeKind.NotFound, default, null, "not-found");
    }
    if (IsRateLimited(response))
    {
      return new(
          GitHubClientOutcomeKind.RateLimited,
          default,
          GetRetryAt(response, timeProvider),
          "rate-limited");
    }
    if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
    {
      return new(
          GitHubClientOutcomeKind.UnauthorizedOrForbidden,
          default,
          null,
          "unauthorized-or-forbidden");
    }
    if (statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500)
    {
      return new(
          GitHubClientOutcomeKind.TransientFailure,
          default,
          null,
          "github-transient-failure");
    }
    if (statusCode is HttpStatusCode.BadRequest or
        HttpStatusCode.Conflict or
        HttpStatusCode.UnprocessableEntity)
    {
      return new(
          GitHubClientOutcomeKind.InvalidRequest,
          default,
          null,
          "github-rejected-request");
    }

    return InvalidResponse<T>("unexpected-http-status");
  }

  private static bool IsRateLimited(HttpResponseMessage response)
  {
    if ((int)response.StatusCode == 429)
    {
      return true;
    }
    if (response.StatusCode != HttpStatusCode.Forbidden)
    {
      return false;
    }
    return response.Headers.RetryAfter is not null ||
        response.Headers.TryGetValues(
            "X-RateLimit-Remaining",
            out var values) &&
        values.Any(static value => value == "0");
  }

  private static DateTimeOffset? GetRetryAt(
      HttpResponseMessage response,
      TimeProvider timeProvider)
  {
    var now = timeProvider.GetUtcNow();
    DateTimeOffset? retryAt = null;
    if (response.Headers.RetryAfter?.Delta is { } delta)
    {
      retryAt = now + delta;
    }
    else if (response.Headers.RetryAfter?.Date is { } date)
    {
      retryAt = date;
    }
    else if (response.Headers.TryGetValues(
                 "X-RateLimit-Reset",
                 out var resetValues))
    {
      var reset = resetValues.FirstOrDefault();
      if (long.TryParse(
              reset,
              NumberStyles.None,
              CultureInfo.InvariantCulture,
              out var seconds))
      {
        try
        {
          retryAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
          retryAt = null;
        }
      }
    }

    if (retryAt is null)
    {
      return null;
    }
    if (retryAt < now)
    {
      return now;
    }
    var maximum = now.AddDays(1);
    return retryAt > maximum ? maximum : retryAt;
  }

  private static GitHubClientOutcome<T> InvalidResponse<T>(string detail) =>
      new(GitHubClientOutcomeKind.InvalidResponse, default, null, detail);
}
