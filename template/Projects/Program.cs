using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.Domain;
using ProjectTrackerTemplate.Projects.Infrastructure;
using Trellis.Asp;
using Trellis.Mediator;
using Trellis.Microservices.AspNetCore;

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
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

// === Endpoints — dispatch via mediator, translate Result→HTTP via ToHttpResponse ===

app.MapGet("/api/projects", async (IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new ListProjectsQuery(), ct);
    return result.ToHttpResponse(projects => projects.Select(ProjectResponse.From).ToArray());
}).RequireAuthorization();

app.MapGet("/api/projects/{id}", async (string id, IMediator mediator, CancellationToken ct) =>
{
    if (!ProjectId.TryCreate(id).TryGetValue(out var projectId))
        return Results.BadRequest(new { error = "invalid_project_id" });

    var result = await mediator.Send(new GetProjectQuery(projectId), ct);
    return result.ToHttpResponse(ProjectResponse.From);
}).RequireAuthorization();

app.MapPut("/api/projects/{id}", async (string id, UpdateProjectRequest body, IMediator mediator, CancellationToken ct) =>
{
    if (!ProjectId.TryCreate(id).TryGetValue(out var projectId))
        return Results.BadRequest(new { error = "invalid_project_id" });

    var result = await mediator.Send(new UpdateProjectCommand(projectId, body.Title, body.Description), ct);
    return result.ToHttpResponse();
}).RequireAuthorization();

app.Run();

// === Wire-format DTOs (kept inline for readability — single Program.cs scan) ===

// Intentionally NOT validated — template starter focuses on the auth pipeline.
// Production would validate at the endpoint/DTO boundary before sending the
// command, or make UpdateProjectCommand implement IValidate (via
// Trellis.Mediator.FluentValidation) so ValidationBehavior rejects bad input
// before the handler runs. NOTE: Trellis pipeline order is
// static auth → resource auth → validation → handler, so a validation failure
// surfaces only AFTER the resource-auth gates pass.
internal sealed record UpdateProjectRequest(string Title, string Description);

internal sealed record ProjectResponse(string Id, string OwnerId, string TenantId, string Title, string Description)
{
    public static ProjectResponse From(Project p) =>
        new(p.Id.Value, p.OwnerId, p.TenantId.Value, p.Title, p.Description);
}
