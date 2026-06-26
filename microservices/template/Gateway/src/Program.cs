using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Trellis.Asp.Authorization;
using Trellis.Yarp;

// Trellis Project Tracker Gateway:
//
// Terminates the inbound user request, identifies the actor (via DevelopmentActorProvider
// in development; swap for ClaimsActorProvider / EntraActorProvider / your real IdP in
// production — see the Trellis cookbook), then re-mints a fresh per-cluster internal
// JWT carrying the full Actor surface (id + permissions + forbidden permissions + ABAC
// attributes including tenant_id) and forwards downstream via YARP.
//
// Routes (see appsettings.json):
//   /api/projects/{**catch-all} -> cluster "projects" -> Projects service (audience="projects")
//   /api/members/{**catch-all}  -> cluster "members"  -> Members service  (audience="members")
//
// AudiencePerCluster = cluster => cluster.ClusterId pins each downstream's audience so
// a token minted for /api/projects/* fails closed at /api/members/* (and vice versa).
// That cross-audience reject is one of the framework's invariants on display.
//
// Destination URLs in appsettings.json use https+http://projects / https+http://members,
// which Microsoft.Extensions.ServiceDiscovery.Yarp resolves at request time via the env
// vars Aspire AppHost injects through WithReference(projects).WithReference(members).

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Dev-mode actor provider: reads the X-Test-Actor header for easy curl-based testing.
// Throws if ASPNETCORE_ENVIRONMENT is not Development.
//
// PRODUCTION TODO: replace with one of the production actor providers from
// Trellis.Asp.Authorization (ClaimsActorProvider, EntraActorProvider,
// NestedJsonPathClaimsActorProvider) wired to your real IdP.
builder.Services.AddDevelopmentActorProvider(o =>
{
    o.DefaultActorId = "guest";
    o.DefaultPermissions = new HashSet<string>(StringComparer.Ordinal);
});

// Fresh per-startup RSA signing key. The JWKS endpoint publishes the public component.
//
// kid is derived from a hash of the public key bytes so a gateway restart that
// regenerates the key material also gets a fresh kid. The dynamic kid prevents the
// failure mode where consumers' cached JWKS holds the old (kid, public-key) pair,
// a new JWT carries the same kid header, the cached old key MATCHES on kid but FAILS
// on signature, and the request is rejected until the JWKS cache TTL expires. With a
// dynamic kid, the consumer's cache MISSES on kid and JwtBearer falls back to a JWKS
// refresh (driven by SecurityTokenSignatureKeyNotFoundException), pulling the new key.
//
// PRODUCTION TODO: this is a HOT-RESTART recovery property, NOT a production
// key-rotation procedure. Real key rotation is two-phase:
//   1. Persist the new key in your key vault (e.g. Azure Key Vault, AWS KMS).
//   2. Pre-publish the new key in JWKS while still signing with the old key.
//   3. Probe every consumer to confirm JWKS convergence (curl /.well-known/jwks.json
//      on each replica; assert the new kid is present in the response).
//   4. Flip SigningCredentials to the new key while keeping the old key in
//      PreviousSigningKeys for the (token_lifetime + ClockSkew + safety_margin)
//      overlap window.
//   5. Drop the retired key.
// Bypassing the pre-publish/probe phase by flipping signers directly causes rejected
// requests for any consumer whose JWKS cache hasn't been forced to refresh yet.
var rsa = RSA.Create(2048);
var publicKeyHash = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
var kid = Convert.ToHexString(publicKeyHash, 0, 8); // first 16 hex chars = 64 bits of pubkey hash
var signingKey = new RsaSecurityKey(rsa) { KeyId = $"dev-key-{kid}" };

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTrellisActorForwarding(o =>
    {
        o.Issuer = "TEMPLATE_GATEWAY_ISSUER_URL";
        o.PublicBaseUrl = new Uri("TEMPLATE_GATEWAY_ISSUER_URL");
        o.SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        // Per-cluster audience pinning: each downstream pins its own ValidAudience.
        // Cross-audience confusion (token minted for cluster A used at cluster B) is
        // rejected by the downstream's JwtBearer ValidAudience check.
        o.AudiencePerCluster = cluster => cluster.ClusterId;
        o.Lifetime = TimeSpan.FromMinutes(5);
    });

var app = builder.Build();
app.MapDefaultEndpoints();

// Publish the OIDC discovery + JWKS endpoints so downstream services can use
// AddJwtBearer(o.Authority = "TEMPLATE_GATEWAY_ISSUER_URL") to auto-discover
// the signing key — no manual key-distribution required.
app.MapTrellisDiscoveryEndpoint();
app.MapReverseProxy();

app.Run();
