using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

namespace PitCrew.Dashboard.Kernel.Authentication;

[DoNotAutoRegister]
internal sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> _schemeOptions,
    ILoggerFactory _loggerFactory,
    UrlEncoder _urlEncoder,
    IOptions<DashboardAuthenticationOptions> _dashboardOptions) :
    AuthenticationHandler<AuthenticationSchemeOptions>(
        _schemeOptions,
        _loggerFactory,
        _urlEncoder)
{
  protected override Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    var options = _dashboardOptions.Value;
    var claims = new List<Claim>
    {
        new(PitCrewClaimTypes.GitHubUserId, options.DevelopmentGitHubUserId),
        new(PitCrewClaimTypes.GitHubLogin, options.DevelopmentGitHubLogin),
        new(ClaimTypes.NameIdentifier, options.DevelopmentGitHubUserId),
        new(ClaimTypes.Name, options.DevelopmentDisplayName),
    };
    if (!string.IsNullOrWhiteSpace(options.DevelopmentAvatarUrl))
    {
      claims.Add(new Claim(
          PitCrewClaimTypes.AvatarUrl,
          options.DevelopmentAvatarUrl));
    }

    var identity = new ClaimsIdentity(
        claims,
        DashboardAuthenticationSchemes.Development);
    return Task.FromResult(AuthenticateResult.Success(
        new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            DashboardAuthenticationSchemes.Development)));
  }
}
