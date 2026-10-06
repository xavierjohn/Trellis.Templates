using Microsoft.IdentityModel.Tokens;
using ProjectTrackerTemplate.Gateway;
using Trellis.Yarp;

// Trellis Project Tracker Gateway:
//
// Terminates the inbound user request, identifies the actor through the selected external
// identity provider (DevelopmentActorProvider only in Development), then re-mints a per-cluster internal
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

builder.Services.AddGatewayAuthentication(builder.Environment, builder.Configuration);

var issuer = builder.GetGatewayIssuer();
var signingKey = SigningKeyRegistration.LoadSigningKey(builder.Environment, builder.Configuration);
// Published public keys support both pre-publication and the retiring-key overlap window.
var publishedKeys = SigningKeyRegistration.LoadPublishedKeys(builder.Environment, builder.Configuration, signingKey);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTrellisActorForwarding(o =>
    {
        o.Issuer = issuer;
        o.PublicBaseUrl = new Uri(issuer);
        o.SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        o.PreviousSigningKeys = publishedKeys;
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
app.UseAuthentication();
app.UseAuthorization();
var proxy = app.MapReverseProxy();
if (!app.Environment.IsDevelopment())
    proxy.RequireAuthorization();
app.Lifetime.ApplicationStopped.Register(() =>
{
    signingKey.Rsa.Dispose();
    foreach (var key in publishedKeys)
        key.Rsa.Dispose();
});

app.Run();