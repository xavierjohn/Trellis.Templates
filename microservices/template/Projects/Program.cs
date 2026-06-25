using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ProjectTrackerTemplate.Projects.Domain;
using ProjectTrackerTemplate.Projects.Endpoints;
using ProjectTrackerTemplate.Projects.Infrastructure;
using Scalar.AspNetCore;
using Trellis.Asp;
using Trellis.Mediator;
using Trellis.Microservices.AspNetCore;
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

// Bind the deployed-environment options once; the SLI location id's region comes from configuration
// (the cloud segment stays a fixed placeholder).
var deployedEnvironmentSection = builder.Configuration.GetSection(EnvironmentOptions.SectionName);
builder.Services.Configure<EnvironmentOptions>(deployedEnvironmentSection);
var deployedEnvironment = deployedEnvironmentSection.Get<EnvironmentOptions>() ?? new EnvironmentOptions();

builder.Services.AddServiceLevelIndicator(options =>
    options.LocationId = ServiceLevelIndicator.CreateLocationId("public", deployedEnvironment.Region))
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

// === Domain + infrastructure ============================================

builder.Services.AddSingleton<IProjectRepository, InMemoryProjectRepository>();

// === Mediator + resource-based authorization layer ======================

// Handlers MUST be Scoped because IAuthorizedResource<TMessage, TResource>
// (the v4 typed accessor that ResourceAuthorizationBehavior populates) is
// registered as scoped. Mediator's default is Singleton, so this opt-in is
// required for the resource-auth pipeline to wire correctly.
builder.Services.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddResourceAuthorization(typeof(Project).Assembly);
                                          // Scans for IAuthorizeResource<Project>
                                          // + IIdentifyResource<Project, ProjectId>
                                          // → bridges to ProjectResourceLoader
                                          // + registers IAuthorizedResource<,> accessor

var app = builder.Build();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseScalarValueValidation();
app.UseServiceLevelIndicator();

app.MapProjectEndpoints();
app.MapDefaultEndpoints();

app.Run();
