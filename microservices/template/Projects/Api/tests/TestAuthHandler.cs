using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Projects.Api.Tests;

// Hermetic-test authentication scheme. The production host validates a gateway-minted internal JWT
// (JwtBearer); a WebApplicationFactory test has no gateway to mint one, so this scheme authenticates the
// request so RequireAuthorization() passes. The ACTOR (id + permissions + tenant) still comes from the
// development actor provider reading X-Test-Actor, so authorization outcomes are exercised for real.
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
