namespace ProjectTrackerTemplate.Projects.Api;

using Asp.Versioning;
using Asp.Versioning.Builder;
using Mediator;
using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.ApiVersioning;
using Trellis.ServiceLevelIndicators;

// Versioned route group for the Projects API, extracted from Program.cs so it scales and so the
// API version is a first-class concept. Clients select the version with ?api-version=2026-03-26.
public static class ProjectEndpoints
{
    private static readonly ApiVersion V20260326 = new(new DateOnly(2026, 3, 26));

    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        ApiVersionSet versionSet = app.NewApiVersionSet("Projects")
            .HasApiVersion(V20260326)
            .ReportApiVersions()
            .Build();

        // Conventions shared by EVERY endpoint in the group are declared once here — authorization,
        // the supported version, and SLI emission (the operation name is derived per-route by the
        // middleware, e.g. "GET /api/projects/{id}"). Endpoints below add nothing of their own.
        var projects = app.MapGroup("/api/projects")
            .WithApiVersionSet(versionSet)
            .WithTags("Projects")
            .MapToApiVersion(V20260326)
            .RequireAuthorization()
            .AddServiceLevelIndicator();

        // GET /api/projects: keyset-paginated list of the caller's tenant projects. Returns a
        // PagedResponse envelope plus an RFC 8288 Link header (rel="next") when more pages exist;
        // HttpContext.PageUrl builds the next-page URL and injects the active api-version. The route is
        // NAMED so PageUrl can resolve it (self-referential pagination). A malformed cursor is a 422.
        projects.MapGet("/", (string? cursor, int? limit, HttpContext http, IMediator mediator, CancellationToken ct) =>
                mediator.Send(new ListProjectsQuery(cursor, limit ?? 0), ct)
                    .ToHttpResponseAsync(
                        nextUrlBuilder: http.PageUrl(
                            "Projects_List",
                            (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
                        body: ProjectResponse.From))
            .WithName("Projects_List");

        // GET /api/projects/{id}: emits the aggregate's strong ETag + Last-Modified. EvaluatePreconditions
        // honors RFC 9110 If-None-Match / If-Modified-Since, returning 304 Not Modified on a cache hit.
        projects.MapGet("/{id}", (ProjectId id, IMediator mediator, CancellationToken ct) =>
            mediator.Send(new GetProjectQuery(id), ct)
                .ToHttpResponseAsync(
                    ProjectResponse.From,
                    opts => opts
                        .WithETag(p => EntityTagValue.Strong(p.ETag))
                        .WithLastModified(p => p.LastModified)
                        .EvaluatePreconditions()));

        // PUT /api/projects/{id}: edit a project. The body carries value objects, so a malformed title or
        // description is a 422 (.WithScalarValueValidation()). The write is conditional — If-Match is
        // required (RFC 9110): a missing precondition is 428, a stale one is 412. On success it maps to a
        // WriteOutcome.Updated so HonorPrefer can serve RFC 7240 Prefer: return=minimal (204 No Content) or
        // return=representation (200 + body), always emitting the new ETag for the caller's next write.
        projects.MapPut("/{id}", (ProjectId id, UpdateProjectRequest body, HttpRequest request, IMediator mediator, CancellationToken ct) =>
            {
                var ifMatch = ETagHelper.ParseIfMatch(request);
                return UpdateProjectCommand.TryCreate(id, body.Title, body.Description, ifMatch)
                    .BindAsync(command => mediator.Send(command, ct))
                    .MapAsync(p => (WriteOutcome<Project>)new WriteOutcome<Project>.Updated(
                        p,
                        Metadata: RepresentationMetadata.Create()
                            .SetStrongETag(p.ETag)
                            .SetLastModified(p.LastModified)
                            .Build()))
                    .ToHttpResponseAsync(
                        ProjectResponse.From,
                        opts => opts.HonorPrefer());
            })
            .WithScalarValueValidation();

        return app;
    }
}

// Wire-format DTOs for the Projects API. The request carries value objects, so a malformed (empty or
// over-long) title/description is rejected with a 422 by .WithScalarValueValidation() before the handler
// runs; the response is primitive so clients never couple to the domain types.
internal sealed record UpdateProjectRequest(ProjectTitle Title, ProjectDescription Description);

internal sealed record ProjectResponse(string Id, string OwnerId, string TenantId, string Title, string Description)
{
    public static ProjectResponse From(Project p) =>
        new(p.Id, p.OwnerId, p.TenantId, p.Title, p.Description);
}
