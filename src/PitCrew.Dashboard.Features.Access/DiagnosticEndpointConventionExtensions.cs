using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace PitCrew.Dashboard.Features.Access;

/// <summary>
/// Adds the credential-scoped request bound to diagnostic endpoints.
/// </summary>
public static class DiagnosticEndpointConventionExtensions
{
  /// <summary>
  /// Applies the per-credential diagnostic request limiter.
  /// </summary>
  /// <param name="builder">Diagnostic route group.</param>
  /// <returns>The same route group for further conventions.</returns>
  public static RouteGroupBuilder AddDiagnosticRateLimit(
      this RouteGroupBuilder builder) =>
      builder.AddEndpointFilter<DiagnosticRateLimitEndpointFilter>();
}
