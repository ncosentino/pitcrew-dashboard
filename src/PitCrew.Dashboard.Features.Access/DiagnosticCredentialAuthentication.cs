using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Access;

internal static class DiagnosticClaimTypes
{
  public const string Permission = "pitcrew:diagnostic-permission";
  public const string TenantId = "pitcrew:diagnostic-tenant";
  public const string NodeId = "pitcrew:diagnostic-node";
  public const string ProfileId = "pitcrew:diagnostic-profile";
}

internal static class DiagnosticCredentialToken
{
  private const string Prefix = "pcd_";

  public static DiagnosticCredentialTokenValue Create()
  {
    var credentialId = Guid.NewGuid();
    var secret = WebEncoders.Base64UrlEncode(
        RandomNumberGenerator.GetBytes(32));
    var raw = $"{Prefix}{credentialId:N}_{secret}";
    return new DiagnosticCredentialTokenValue(
        credentialId,
        raw,
        Hash(raw));
  }

  public static Guid? ParseOrNull(string raw)
  {
    if (!raw.StartsWith(Prefix, StringComparison.Ordinal) ||
        raw.Length > 128)
    {
      return null;
    }
    var separator = raw.IndexOf(
        '_',
        Prefix.Length);
    if (separator < 0 ||
        separator - Prefix.Length != 32 ||
        raw.Length - separator - 1 < 40)
    {
      return null;
    }
    return Guid.TryParseExact(
        raw.AsSpan(Prefix.Length, 32),
        "N",
        out var credentialId)
        ? credentialId
        : null;
  }

  public static string Hash(string raw) =>
      Convert.ToHexString(
          SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}

internal sealed record DiagnosticCredentialTokenValue(
    Guid CredentialId,
    string Raw,
    string Hash);

[DoNotAutoRegister]
internal sealed class DiagnosticCredentialAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> _schemeOptions,
    ILoggerFactory _loggerFactory,
    UrlEncoder _urlEncoder,
    IDiagnosticCredentialStore _credentialStore,
    TimeProvider _timeProvider) :
    AuthenticationHandler<AuthenticationSchemeOptions>(
        _schemeOptions,
        _loggerFactory,
        _urlEncoder)
{
  protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    var authorization = Request.Headers.Authorization.ToString();
    var prefix =
        $"{DiagnosticAuthenticationDefaults.AuthorizationScheme} ";
    if (!authorization.StartsWith(
        prefix,
        StringComparison.OrdinalIgnoreCase))
    {
      return AuthenticateResult.NoResult();
    }
    var raw = authorization[prefix.Length..].Trim();
    var credentialId = DiagnosticCredentialToken.ParseOrNull(raw);
    if (credentialId is null)
    {
      return AuthenticateResult.Fail(
          "The diagnostic credential is invalid.");
    }
    var scope = await _credentialStore.ResolveOrNullAsync(
        credentialId.Value,
        DiagnosticCredentialToken.Hash(raw),
        _timeProvider.GetUtcNow(),
        Context.RequestAborted);
    if (scope is null)
    {
      return AuthenticateResult.Fail(
          "The diagnostic credential is invalid.");
    }

    var claims = new List<Claim>
    {
        new(
            ClaimTypes.NameIdentifier,
            scope.CredentialId.ToString("N")),
        new(
            DiagnosticClaimTypes.Permission,
            "diagnostics.read"),
        new(
            DiagnosticClaimTypes.TenantId,
            scope.TenantId),
    };
    claims.AddRange(scope.NodeIds.Select(nodeId => new Claim(
        DiagnosticClaimTypes.NodeId,
        nodeId.ToString("N"))));
    claims.AddRange(scope.ProfileIds.Select(profileId => new Claim(
        DiagnosticClaimTypes.ProfileId,
        profileId)));
    var identity = new ClaimsIdentity(
        claims,
        DiagnosticAuthenticationDefaults.Scheme);
    return AuthenticateResult.Success(
        new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            DiagnosticAuthenticationDefaults.Scheme));
  }
}

internal sealed record DiagnosticAccessRequirement :
    IAuthorizationRequirement;

[DoNotAutoRegister]
internal sealed class DiagnosticAccessAuthorizationHandler :
    AuthorizationHandler<DiagnosticAccessRequirement>
{
  protected override Task HandleRequirementAsync(
      AuthorizationHandlerContext context,
      DiagnosticAccessRequirement requirement)
  {
    if (context.Resource is not HttpContext httpContext ||
        context.User.Identity?.AuthenticationType !=
            DiagnosticAuthenticationDefaults.Scheme)
    {
      return Task.CompletedTask;
    }
    var requestedTenant = Convert.ToString(
        httpContext.Request.RouteValues["tenantId"],
        System.Globalization.CultureInfo.InvariantCulture);
    var allowedTenant = context.User.FindFirstValue(
        DiagnosticClaimTypes.TenantId);
    if (!string.IsNullOrWhiteSpace(requestedTenant) &&
        string.Equals(
            requestedTenant,
            allowedTenant,
            StringComparison.Ordinal) &&
        context.User.HasClaim(
            DiagnosticClaimTypes.Permission,
            "diagnostics.read"))
    {
      context.Succeed(requirement);
    }
    return Task.CompletedTask;
  }
}
