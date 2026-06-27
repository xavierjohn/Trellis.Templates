using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ProjectTrackerTemplate.Projects.Acl;
using ProjectTrackerTemplate.Projects.Api;
using ProjectTrackerTemplate.Projects.Application;
using Scalar.AspNetCore;
using Trellis.Asp;
using Trellis.Microservices.AspNetCore;
using Trellis.ResourceNaming.Azure;
using Trellis.ServiceLevelIndicators;

// Projects microservice — operational cluster (CRUD on Project aggregate).
//
// Audience: "projects" (matches the YARP cluster name → AudiencePerCluster).
// Path:     /api/projects (list), /api/projects/{id} (get + put)
//
// Auth pipeline (top to bottom):
//   1. JwtBearer validates the gateway-minted internal JWT (issuer + audience +
//      lifetime + signature, via JWKS auto-discovery from the gateway).
//   2. TrellisInternalJwtActorProvider hydrates the full Actor (id + permissions
//      + forbidden permissions + ABAC attributes including tenant_id) from the
//      strict sentinel + count claim contract.
//   3. Mediator pipeline: static auth → resource auth → handler.
//      → Static auth checks RequiredPermissions (e.g., projects:read/write).
//      → Resource auth loads the Project via ProjectResourceLoader, then calls
//        Authorize(actor, project) — enforces tenant_id match + owner check.
//      → Handler reads the SAME instance via IAuthorizedResource (load-once).

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Surface the ProjectTracker.Projects meter (projects.resource_loads) in the
// Aspire dashboard Metrics tab. ServiceDefaults already wired the OTLP exporter
// + the stock instrumentation; this adds the per-service meter.
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(ProjectsMetrics.MeterName));

// === API surface =========================================================
//
// Date-based API versioning (clients pass ?api-version=2026-03-26), OpenAPI + Scalar, RFC 9457
// ProblemDetails, scalar value-object validation, and Service Level Indicators. The endpoints —
// versioned route groups — live in Endpoints/ProjectEndpoints.cs.

builder.Services.AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer()
    .AddOpenApi(options => options.Document.AddScalarTransformers());

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
{
    // Always surface the active trace id so clients can correlate the error with server spans.
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;

    // Never leak raw exception detail on a 500.
    if (ctx.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
        ctx.ProblemDetails.Detail = "An error occurred. Please share the trace id with support.";

    // RFC 9110 §15.5.6: surface the supported methods from the Allow header as a structured array.
    if (ctx.ProblemDetails.Status == StatusCodes.Status405MethodNotAllowed &&
        ctx.HttpContext.Response.Headers.TryGetValue("Allow", out var allow))
    {
        ctx.ProblemDetails.Extensions["allow"] = allow.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
});

// Trellis ASP integration + scalar value-object validation. The UseScalarValueValidation
// middleware (below) rewrites a failed value-object bind — e.g. a malformed {id} route value —
// into a 422 ProblemDetails before the handler runs. Add .WithScalarValueValidation() to an
// endpoint only when its request BODY carries value objects (none here yet).
builder.Services.AddTrellisAspWithScalarValidation();

// Bind the deployed-environment options once; the SLI location id's region comes from configuration.
var deployedEnvironmentSection = builder.Configuration.GetSection("DeployedEnvironment");
builder.Services.Configure<DeployedEnvironmentOptions>(deployedEnvironmentSection);
var deployedEnvironment = deployedEnvironmentSection.Get<DeployedEnvironmentOptions>() ?? new DeployedEnvironmentOptions();

// Region is the deployment's telemetry location; fail fast rather than emit a region-less location id.
var region = deployedEnvironment.Region;
if (string.IsNullOrWhiteSpace(region))
{
    throw new InvalidOperationException(
        "Configuration 'DeployedEnvironment:Region' is required for the service-level-indicator location id.");
}

var locationId = ServiceLevelIndicator.CreateLocationId("public", region);
builder.Services.AddServiceLevelIndicator(options => options.LocationId = locationId)
    // Stamp each SLI with the caller's tenant so emissions aren't all CustomerResourceId=Unknown.
    // The enrichment runs after authentication (on the way out), so the tenant_id claim is available;
    // under an ARM resource provider, switch this to the ARM resource id.
    .Enrich(ctx =>
    {
        var tenantId = ctx.HttpContext.User.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(tenantId))
            ctx.SetCustomerResourceId($"tenant://{tenantId}");
    })
    .AddApiVersion();

// === Trust-boundary layer =================================================

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Gate dev-only options on IsDevelopment so a copy/paste into a production
        // composition root keeps RequireHttpsMetadata=true (the ASP.NET Core default)
        // and IncludeErrorDetails=false (don't leak validation failure reasons).
        var isDev = builder.Environment.IsDevelopment();

        o.Authority = "TEMPLATE_GATEWAY_ISSUER_URL";
        o.Audience = "projects";
        o.RequireHttpsMetadata = !isDev;
        o.IncludeErrorDetails = isDev;
        o.MapInboundClaims = false;
        o.SaveToken = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "TEMPLATE_GATEWAY_ISSUER_URL",
            ValidateAudience = true,
            ValidAudience = "projects",
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            TryAllIssuerSigningKeys = false,
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddTrellisInternalJwtActorProvider(o =>
{
    o.ExpectedIssuer = "TEMPLATE_GATEWAY_ISSUER_URL";
    o.ExpectedAudience = "projects";

    // Project the tenant_id ABAC claim through to Actor.Attributes AND require it.
    // Missing tenant_id fails closed at the actor-provider boundary (401), not
    // at the handler — so a misconfigured caller never reaches the auth pipeline.
    o.AttributeClaimMap["tenant_id"] = "tenant_id";
    o.RequiredAttributes = ["tenant_id"];
});

// === Application + anti-corruption layers ================================
//
// The DI that used to be inlined here now lives with each layer. AddProjectsApplication wires the Mediator
// pipeline; AddProjectsAcl wires the in-memory Project store, the EF Core context (SQL Server via Aspire),
// the inbound eventing plane (inbox dedup + the read-model projection handler + the Service Bus pump), and
// resource-based authorization.
builder.Services.AddProjectsApplication();
builder.AddProjectsAcl();

var app = builder.Build();

// Create the inbox + read-model schema in Development (use EF migrations in production). Runs before the
// Service Bus pump starts (hosted services start on app.Run), so the consumer always finds its tables.
if (app.Environment.IsDevelopment())
    await app.Services.EnsureProjectsCreatedAsync();

// === HTTP pipeline =======================================================

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion();
    app.MapScalarApiReference(options =>
    {
        var descriptions = app.DescribeApiVersions();
        for (var i = 0; i < descriptions.Count; i++)
        {
            var description = descriptions[i];
            options.AddDocument(description.GroupName, description.GroupName, isDefault: i == descriptions.Count - 1);
        }
    });
}

// Render any 4xx/5xx (including pipeline short-circuits) as RFC 9457 ProblemDetails.
app.UseExceptionHandler();
app.UseStatusCodePages();

// Measure every matched request, BEFORE auth and validation. Routing has already run, so the SLI
// middleware sees the endpoint, and because it emits on the way out it still records the final
// status. Placed after auth/validation it would miss 401/403/422 short-circuits, silently
// undercounting the failure surface.
app.UseServiceLevelIndicator();

app.UseAuthentication();
app.UseAuthorization();
app.UseScalarValueValidation();

app.MapProjectEndpoints();
app.MapTeamEndpoints();
app.MapDefaultEndpoints();

app.Run();

// Public entry-point marker for WebApplicationFactory<T> integration tests. The cross-service eventing
// test boots both hosts in one process, so each host needs a distinct public type to target.
namespace ProjectTrackerTemplate.Projects.Api
{
    public sealed class ProjectsApiEntryPoint;
}
