using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NexusLabs.Needlr;

namespace PitCrew.Dashboard.Kernel.Authentication;

internal sealed class DashboardAuthenticationPlugin :
    IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    var authenticationOptions = options.Config
        .GetSection("PitCrew:Authentication")
        .Get<DashboardAuthenticationOptions>() ??
        new DashboardAuthenticationOptions();
    var reverseProxyOptions = options.Config
        .GetSection("PitCrew:ReverseProxy")
        .Get<DashboardReverseProxyOptions>() ??
        new DashboardReverseProxyOptions();
    var secureCookies =
        authenticationOptions.Mode == DashboardAuthenticationMode.GitHub;

    var keyPath = Path.GetFullPath(
        authenticationOptions.DataProtectionKeyPath);
    Directory.CreateDirectory(keyPath);
    options.Services
        .AddDataProtection()
        .SetApplicationName("PitCrew.Dashboard")
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

    var authentication = options.Services.AddAuthentication(
        authenticationConfiguration =>
        {
          if (authenticationOptions.Mode ==
              DashboardAuthenticationMode.Development)
          {
            authenticationConfiguration.DefaultAuthenticateScheme =
                DashboardAuthenticationSchemes.Development;
            authenticationConfiguration.DefaultChallengeScheme =
                DashboardAuthenticationSchemes.Development;
            authenticationConfiguration.DefaultForbidScheme =
                DashboardAuthenticationSchemes.Development;
          }
          else
          {
            authenticationConfiguration.DefaultAuthenticateScheme =
                DashboardAuthenticationSchemes.Cookie;
            authenticationConfiguration.DefaultSignInScheme =
                DashboardAuthenticationSchemes.Cookie;
            authenticationConfiguration.DefaultChallengeScheme =
                DashboardAuthenticationSchemes.Cookie;
            authenticationConfiguration.DefaultForbidScheme =
                DashboardAuthenticationSchemes.Cookie;
          }
        });
    authentication.AddCookie(
        DashboardAuthenticationSchemes.Cookie,
        cookieOptions =>
        {
          cookieOptions.Cookie.Name = secureCookies
              ? "__Host-PitCrew.Auth"
              : "PitCrew.Auth";
          cookieOptions.Cookie.HttpOnly = true;
          cookieOptions.Cookie.IsEssential = true;
          cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
          cookieOptions.Cookie.SecurePolicy = secureCookies
              ? CookieSecurePolicy.Always
              : CookieSecurePolicy.SameAsRequest;
          cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(8);
          cookieOptions.SlidingExpiration = true;
          cookieOptions.Events.OnRedirectToLogin = context =>
              ConvertRedirectToStatusAsync(
                  context,
                  StatusCodes.Status401Unauthorized,
                  context.HttpContext.RequestAborted);
          cookieOptions.Events.OnRedirectToAccessDenied = context =>
              ConvertRedirectToStatusAsync(
                  context,
                  StatusCodes.Status403Forbidden,
                  context.HttpContext.RequestAborted);
        });

    if (authenticationOptions.Mode ==
        DashboardAuthenticationMode.Development)
    {
      authentication.AddScheme<
          AuthenticationSchemeOptions,
          DevelopmentAuthenticationHandler>(
              DashboardAuthenticationSchemes.Development,
              static _ => { });
    }
    else
    {
      options.Services.AddTransient<GitHubOAuthEvents>();
      authentication.AddOAuth(
          DashboardAuthenticationSchemes.GitHub,
          oauthOptions =>
          {
            oauthOptions.SignInScheme =
                DashboardAuthenticationSchemes.Cookie;
            oauthOptions.ClientId =
                authenticationOptions.GitHubClientId;
            oauthOptions.ClientSecret =
                authenticationOptions.GitHubClientSecret;
            oauthOptions.CallbackPath = "/signin-github";
            oauthOptions.AuthorizationEndpoint =
                "https://github.com/login/oauth/authorize";
            oauthOptions.TokenEndpoint =
                "https://github.com/login/oauth/access_token";
            oauthOptions.UserInformationEndpoint =
                "https://api.github.com/user";
            oauthOptions.SaveTokens = false;
            oauthOptions.UsePkce = true;
            oauthOptions.Scope.Clear();
            oauthOptions.EventsType = typeof(GitHubOAuthEvents);
            oauthOptions.ClaimActions.MapJsonKey(
                PitCrewClaimTypes.GitHubUserId,
                "id");
            oauthOptions.ClaimActions.MapJsonKey(
                PitCrewClaimTypes.GitHubLogin,
                "login");
            oauthOptions.ClaimActions.MapJsonKey(
                ClaimTypes.NameIdentifier,
                "id");
            oauthOptions.ClaimActions.MapJsonKey(
                ClaimTypes.Name,
                "name");
            oauthOptions.ClaimActions.MapJsonKey(
                PitCrewClaimTypes.AvatarUrl,
                "avatar_url");
          });
    }

    options.Services.AddAuthorization();
    options.Services.AddAntiforgery(antiforgeryOptions =>
    {
      antiforgeryOptions.HeaderName = "X-PitCrew-Antiforgery";
      antiforgeryOptions.Cookie.Name = secureCookies
          ? "__Host-PitCrew.Antiforgery"
          : "PitCrew.Antiforgery";
      antiforgeryOptions.Cookie.HttpOnly = true;
      antiforgeryOptions.Cookie.IsEssential = true;
      antiforgeryOptions.Cookie.SameSite = SameSiteMode.Strict;
      antiforgeryOptions.Cookie.SecurePolicy = secureCookies
          ? CookieSecurePolicy.Always
          : CookieSecurePolicy.SameAsRequest;
    });
    options.Services.AddSingleton<
        IAuthenticatedDashboardUserAccessor,
        AuthenticatedDashboardUserAccessor>();
    options.Services.Configure<ForwardedHeadersOptions>(
        forwardedHeadersOptions =>
        {
          forwardedHeadersOptions.ForwardedHeaders =
              ForwardedHeaders.XForwardedFor |
              ForwardedHeaders.XForwardedProto;
          forwardedHeadersOptions.ForwardLimit = 1;
          foreach (var address in reverseProxyOptions.KnownProxyAddresses)
          {
            if (IPAddress.TryParse(address, out var knownProxy))
            {
              forwardedHeadersOptions.KnownProxies.Add(knownProxy);
            }
          }
        });
  }

  private static Task ConvertRedirectToStatusAsync(
      RedirectContext<CookieAuthenticationOptions> context,
      int statusCode,
      CancellationToken _)
  {
    if (context.Request.Path.StartsWithSegments("/api"))
    {
      context.Response.StatusCode = statusCode;
    }
    else
    {
      context.Response.Redirect(context.RedirectUri);
    }
    return Task.CompletedTask;
  }
}
