using Carter;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Maps dashboard login and logout browser endpoints.
/// </summary>
public sealed class AuthenticationCarterModule : ICarterModule
{
  /// <summary>
  /// Adds authentication browser routes to the application.
  /// </summary>
  /// <param name="app">Endpoint route builder.</param>
  public void AddRoutes(IEndpointRouteBuilder app)
  {
    app.MapGet("/auth/login", Login)
        .AllowAnonymous();
    app.MapPost("/auth/logout", LogoutAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization();
  }

  private static IResult Login(
      HttpContext context,
      IOptions<DashboardAuthenticationOptions> options)
  {
    var returnUrl = GetLocalReturnUrl(context);
    if (options.Value.Mode == DashboardAuthenticationMode.Development)
    {
      return Results.LocalRedirect(returnUrl);
    }

    return Results.Challenge(
        new AuthenticationProperties
        {
          RedirectUri = returnUrl,
        },
        [DashboardAuthenticationSchemes.GitHub]);
  }

  private static async Task<IResult> LogoutAsync(
      HttpContext context,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    await context.SignOutAsync(
        DashboardAuthenticationSchemes.Cookie);
    return Results.NoContent();
  }

  private static string GetLocalReturnUrl(HttpContext context)
  {
    var candidate = context.Request.Query["returnUrl"].ToString();
    if (string.IsNullOrWhiteSpace(candidate))
    {
      return "/";
    }
    if (!candidate.StartsWith(
            "/",
            StringComparison.Ordinal) ||
        candidate.StartsWith(
            "//",
            StringComparison.Ordinal) ||
        candidate.Contains('\\'))
    {
      return "/";
    }
    return candidate;
  }
}
