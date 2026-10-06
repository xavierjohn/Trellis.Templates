#pragma warning disable IDE0047
using ProjectTrackerTemplate.Members.Acl;
using ProjectTrackerTemplate.Members.Api;
using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using Scalar.AspNetCore;
using Trellis.Asp;
using Trellis.Asp.Idempotency;
using Trellis.Microservices.AspNetCore;
using Trellis.ServiceDefaults;
using Trellis.ServiceLevelIndicators;

// Members microservice — HR-sensitive cluster (CRUD on Member aggregate).
//
// Audience: "members" (matches the YARP cluster name → AudiencePerCluster).
// Path:     /api/members (list), /api/members/{id} (get), /api/members (post)
//
// HOW THIS DIFFERS FROM PROJECTS:
//
// Members uses HideExistence<Member>() (see ConfigureResourceAuthorization below).
// That single line is what collapses a cross-tenant Error.Forbidden into an
// Error.NotFound at the response-mapping stage — so a caller probing for the
// existence of an employee in another tenant gets the same 404 they'd get for
// a non-existent MemberId. Without that single line, the response would be 403
// and the caller would learn the id corresponds to a real member.
//
// Use HideExistence whenever the resource identifier itself is sensitive. Use
// the standard (403) behaviour when "this resource exists but is forbidden" is
// itself an OK signal — typical for operational resources like Projects.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// === API surface =========================================================
//
// Date-based API versioning (clients pass ?api-version=2026-03-26), OpenAPI + Scalar, RFC 9457
// ProblemDetails, scalar value-object validation, idempotency, and Service Level Indicators. The
// endpoints themselves — versioned route groups — live in Endpoints/MemberEndpoints.cs.

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
    .UseIdempotency()
    .UseMediator()
    .UseDomainEvents(typeof(InviteMemberCommand).Assembly)
    .UseIntegrationEvents()
    .UseResourceAuthorization(typeof(InviteMemberCommand).Assembly, typeof(MembersDbContext).Assembly)
    .UseResourceAuthorization(policy => policy.HideExistence<Member>())
    .UseOutbox<MembersDbContext>()
    .UseEntityFrameworkUnitOfWork<MembersDbContext>());
builder.Services.AddConfiguredIdempotencyStore(builder.Environment, builder.Configuration);

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
    audience: "members",
    configureActor: o =>
    {
        // Project the tenant_id ABAC claim into Actor.Attributes AND require it, so a missing
        // tenant_id fails closed at the actor-provider boundary (401) before any handler runs.
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
// Layers own Mediator handler generation, the DbContext/provider, repositories and vendor transport.
// The AddTrellis composition above owns pipeline ordering, dispatch and the outbox relay.
builder.Services.AddMembersApplication();
builder.AddMembersAcl();

var app = builder.Build();

// Create the schema + seed the demo members in Development (use EF migrations in production).
if (app.Environment.IsDevelopment())
    await app.Services.SeedMembersDevelopmentDataAsync();

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
app.UseTrellisIdempotency();
app.UseScalarValueValidation();

app.MapMemberEndpoints();
app.MapDefaultEndpoints();

app.Run();

// Public entry-point marker for WebApplicationFactory<T> integration tests. The cross-service eventing
// test boots both hosts in one process, so each host needs a distinct public type to target.
namespace ProjectTrackerTemplate.Members.Api
{
    public sealed class MembersApiEntryPoint;
}