#pragma warning disable IDE0047
using ProjectTrackerTemplate.Projects.Acl;
using ProjectTrackerTemplate.Projects.Api;
using ProjectTrackerTemplate.Projects.Application;
using Scalar.AspNetCore;
using Trellis.Asp;
using Trellis.Microservices.AspNetCore;
using Trellis.ServiceDefaults;
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

#if (!NoApiVersioning)
builder.Services.AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer()
    .AddOpenApi(options => options.Document.AddScalarTransformers());
#else
builder.Services.AddOpenApi(options => options.AddScalarTransformers());
#endif

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
{
    // Override Trellis's default 500 wording with this service's support message.
    if (ctx.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
        ctx.ProblemDetails.Detail = "An error occurred. Please share the trace id with support.";
});

// Trellis ASP integration + scalar value-object validation. The UseScalarValueValidation
// middleware (below) rewrites a failed value-object bind — e.g. a malformed {id} route value —
// into a 422 ProblemDetails before the handler runs. Add .WithScalarValueValidation() to an
// endpoint only when its request BODY carries value objects (none here yet).
builder.Services.AddTrellis(options => options
    .UseAsp()
    .UseScalarValueValidation()
    .UseProblemDetails()
    .UseMediator()
    .UseResourceAuthorization(typeof(GetProjectQuery).Assembly, typeof(ProjectsDbContext).Assembly)
    .UseInbox<ProjectsDbContext>(inbox => inbox.ConsumerId = MessagingTopology.ProjectsSubscriptionName)
    .UseEntityFrameworkUnitOfWork<ProjectsDbContext>());

builder.ConfigureServiceLevelIndicators();

// === Trust-boundary layer =================================================

// One call fuses the strict internal-JWT bearer profile with the actor provider so the issuer,
// audience, and scheme cannot drift apart. It re-applies the non-negotiable validation invariants
// (RS256-only, MapInboundClaims=false, iss/aud/lifetime/signature checks) in a PostConfigure that a
// later Configure cannot weaken, failing closed at startup if one does. The configureJwtBearer
// callback carries only deployment-specific bits — gated on IsDevelopment so a copy/paste into a
// production composition root keeps RequireHttpsMetadata=true and does not leak validation-failure
// reasons.
builder.Services.AddTrellisInternalJwtBearer(
    issuer: builder.GetGatewayIssuer(),
    audience: "projects",
    configureActor: o =>
    {
        // Project the tenant_id ABAC claim through to Actor.Attributes AND require it.
        // Missing tenant_id fails closed at the actor-provider boundary (401), not
        // at the handler — so a misconfigured caller never reaches the auth pipeline.
        o.AttributeClaimMap["tenant_id"] = "tenant_id";
        o.RequiredAttributes = ["tenant_id"];
    },
    configureJwtBearer: o =>
    {
        var isDev = builder.Environment.IsDevelopment();
        o.RequireHttpsMetadata = !isDev;
        o.IncludeErrorDetails = isDev;
    });

builder.Services.AddAuthorization();

// === Application + anti-corruption layers ================================
//
// Layers own Mediator handler generation, persistence, the projection handler and vendor transport.
// The AddTrellis composition above owns resource authorization, the unit of work and inbox registration.
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
#if (!NoApiVersioning)
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
#else
    app.MapOpenApi();
    app.MapScalarApiReference();
#endif
}

// Render any 4xx/5xx (including pipeline short-circuits) as RFC 9457 ProblemDetails.
app.UseTrellisProblemDetails();

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