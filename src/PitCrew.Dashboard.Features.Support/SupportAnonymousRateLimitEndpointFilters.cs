using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Http;

namespace PitCrew.Dashboard.Features.Support;

internal abstract class SupportAnonymousRateLimitEndpointFilter(
    SupportAnonymousRequestLimiter _limiter,
    string _operation) : IEndpointFilter
{
  protected abstract string GetFunctionalPartition(
      EndpointFilterInvocationContext context);

  public ValueTask<object?> InvokeAsync(
      EndpointFilterInvocationContext context,
      EndpointFilterDelegate next)
  {
    var networkIdentity =
        context.HttpContext.Connection.RemoteIpAddress?.ToString() ??
        "unavailable";
    return !_limiter.Allow(
            _operation,
            networkIdentity,
            GetFunctionalPartition(context))
        ? new ValueTask<object?>(Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Support identity request rate exceeded."))
        : next(context);
  }

  protected static string CreateTenantPartition(string? tenantId)
  {
    if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 128)
    {
      return "invalid-tenant";
    }
    return Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(tenantId)));
  }
}

internal sealed class SupportEnrollmentRateLimitEndpointFilter(
    SupportAnonymousRequestLimiter limiter) :
    SupportAnonymousRateLimitEndpointFilter(limiter, "enrollment")
{
  protected override string GetFunctionalPartition(
      EndpointFilterInvocationContext context) =>
      CreateTenantPartition(
          context.Arguments
              .OfType<CompleteSupportEnrollmentRequest>()
              .FirstOrDefault()
              ?.TenantId);
}

internal sealed class SupportRotationRateLimitEndpointFilter(
    SupportAnonymousRequestLimiter limiter) :
    SupportAnonymousRateLimitEndpointFilter(limiter, "rotation")
{
  protected override string GetFunctionalPartition(
      EndpointFilterInvocationContext context)
  {
    var tenantId = context.Arguments
        .OfType<RotateSupportIdentityRequest>()
        .Select(static request => request.TenantId)
        .Concat(
            context.Arguments
                .OfType<FinalizeSupportIdentityRotationRequest>()
                .Select(static request => request.TenantId))
        .FirstOrDefault();
    var nodeId = context.Arguments.OfType<Guid>().FirstOrDefault();
    return string.Concat(
        CreateTenantPartition(tenantId),
        "|",
        nodeId == Guid.Empty ? "invalid-node" : nodeId.ToString("N"));
  }
}
