using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.Members.Infrastructure;
using Trellis.Asp;
using Trellis.Mediator;
using Trellis.Microservices.AspNetCore;

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

// === Trust-boundary layer =================================================

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        var isDev = builder.Environment.IsDevelopment();

        o.Authority = "TEMPLATE_GATEWAY_ISSUER_URL";
        o.Audience = "members";
        o.RequireHttpsMetadata = !isDev;
        o.IncludeErrorDetails = isDev;
        o.MapInboundClaims = false;
        o.SaveToken = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "TEMPLATE_GATEWAY_ISSUER_URL",
            ValidateAudience = true,
            ValidAudience = "members",
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
    o.ExpectedAudience = "members";
    o.AttributeClaimMap["tenant_id"] = "tenant_id";
    o.RequiredAttributes = ["tenant_id"];
});

// === Domain + infrastructure ============================================

builder.Services.AddSingleton<IMemberRepository, InMemoryMemberRepository>();

// === Mediator + resource-based authorization layer ======================

builder.Services.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddResourceAuthorization(typeof(Member).Assembly);

// The single line that makes Members "HR-sensitive": cross-tenant access
// failures are projected to 404, NOT 403. The behaviour is keyed on the
// RESOURCE type, not the message type — so every command/query whose
// IIdentifyResource binds to Member inherits the policy.
builder.Services.AddResourceAuthorization(o => o.HideExistence<Member>());

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

// === Endpoints — dispatch via mediator, translate Result→HTTP via ToHttpResponse ===

app.MapGet("/api/members/{id}", async (string id, IMediator mediator, CancellationToken ct) =>
{
    if (!MemberId.TryCreate(id).TryGetValue(out var memberId))
        return Results.BadRequest(new { error = "invalid_member_id" });

    var result = await mediator.Send(new GetMemberQuery(memberId), ct);
    return result.ToHttpResponse(MemberResponse.From);
}).RequireAuthorization();

app.MapPost("/api/members", async (InviteMemberRequest body, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new InviteMemberCommand(body.Email, body.Role), ct);
    return result.ToHttpResponse(id => new { id = id.Value });
}).RequireAuthorization();

app.Run();

// === Wire-format DTOs (kept inline for readability — single Program.cs scan) ===

internal sealed record InviteMemberRequest(string Email, string Role);

internal sealed record MemberResponse(string Id, string TenantId, string Email, string Role)
{
    public static MemberResponse From(Member m) => new(m.Id.Value, m.TenantId.Value, m.Email, m.Role);
}
