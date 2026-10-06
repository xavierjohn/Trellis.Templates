#pragma warning disable IDE0047
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ProjectTrackerTemplate.Gateway;
using Trellis.Authorization;

namespace Gateway.Tests;

public class AuthenticationTests
{
#if (UseEntra)
    private const string IdentityClaim = "oid";
    private const string TenantClaim = "tid";
    private const string Tenant = "00000000-0000-0000-0000-000000000001";
    private const string Authority = "https://login.microsoftonline.com/" + Tenant + "/v2.0";
    private const string Audience = "00000000-0000-0000-0000-000000000002";
#else
    private const string IdentityClaim = "sub";
    private const string TenantClaim = "tenant_id";
    private const string Tenant = "acme";
    private const string Authority = "https://identity.example";
    private const string Audience = "billing";
#endif

    [Fact]
    public void Production_requires_identity_configuration()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        var register = () => builder.Services.AddGatewayAuthentication(builder.Environment,
            new ConfigurationBuilder().Build());

        register.Should().Throw<InvalidOperationException>().WithMessage("*Authentication:*required*");
    }

    [Theory]
    [InlineData("valid", HttpStatusCode.OK)]
    [InlineData("wrong-issuer", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-audience", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("unsigned", HttpStatusCode.Unauthorized)]
    [InlineData("missing-identity", HttpStatusCode.Unauthorized)]
    [InlineData("missing-tenant", HttpStatusCode.Unauthorized)]
    public async Task Production_validates_external_tokens_and_projects_tenant(string scenario, HttpStatusCode expected)
    {
        using var rsa = RSA.Create(2048);
        using var otherRsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings());
        builder.Services.AddGatewayAuthentication(builder.Environment, builder.Configuration);
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            // Keep discovery hermetic without relaxing the production token-validation policy.
            options.Configuration = new OpenIdConnectConfiguration { Issuer = Authority };
            options.Configuration.SigningKeys.Add(key);
        });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/actor", async (IActorProvider provider) =>
        {
            var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
            if (!actor.TryGetValue(out var value))
                return Results.Unauthorized();
            return Results.Text(value.Attributes["tenant_id"]);
        }).RequireAuthorization();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        var claims = new Dictionary<string, object>
        {
            [IdentityClaim] = "alice",
            [TenantClaim] = Tenant,
        };
        if (scenario == "missing-identity")
            claims.Remove(IdentityClaim);
        if (scenario == "missing-tenant")
            claims.Remove(TenantClaim);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = scenario == "wrong-issuer" ? "https://other.example" : Authority,
            Audience = scenario == "wrong-audience" ? "other-api" : Audience,
            Claims = claims,
            NotBefore = DateTime.UtcNow.AddHours(-1),
            Expires = scenario == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = scenario == "unsigned" ? null : new SigningCredentials(
                scenario == "wrong-signature" ? new RsaSecurityKey(otherRsa) { KeyId = key.KeyId } : key,
                SecurityAlgorithms.RsaSha256),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JsonWebTokenHandler().CreateToken(descriptor));

        using var response = await client.GetAsync("/actor", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.OK)
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be(Tenant);
    }

    [Fact]
    public async Task Production_does_not_trust_development_actor_headers()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings());
        builder.Services.AddGatewayAuthentication(builder.Environment, builder.Configuration);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/protected", () => Results.Ok()).RequireAuthorization();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Actor", """{"Id":"admin","Permissions":["*"],"Attributes":{"tenant_id":"acme"}}""");

        using var response = await client.GetAsync("/protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Authentication:Authority"] = Authority,
        ["Authentication:Audience"] = Audience,
        ["Authentication:TenantId"] = Tenant,
        ["Authentication:ClientId"] = Audience,
    };
}