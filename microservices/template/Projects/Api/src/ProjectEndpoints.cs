namespace ProjectTrackerTemplate.Projects.Api;

using Asp.Versioning;
using Asp.Versioning.Builder;
using Mediator;
using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Asp;
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

        projects.MapGet("/", (IMediator mediator, CancellationToken ct) =>
            mediator.Send(new ListProjectsQuery(), ct)
                .ToHttpResponseAsync(items => items.Select(ProjectResponse.From).ToArray()));

        projects.MapGet("/{id}", (ProjectId id, IMediator mediator, CancellationToken ct) =>
            mediator.Send(new GetProjectQuery(id), ct)
                .ToHttpResponseAsync(
                    ProjectResponse.From,
                    opts => opts
                        .WithETag(p => EntityTagValue.Strong(p.ETag))
                        .WithLastModified(p => p.LastModified)));

        // PUT /api/projects/{id}: edit a project. The body carries value objects, so a malformed title or
        // description is a 422 (.WithScalarValueValidation()). The write is conditional — If-Match is
        // required (RFC 9110): a missing precondition is 428, a stale one is 412. On success it returns 200
        // with the updated representation and the new ETag for the caller's next conditional write.
        projects.MapPut("/{id}", (ProjectId id, UpdateProjectRequest body, HttpRequest request, IMediator mediator, CancellationToken ct) =>
            {
                var ifMatch = ETagHelper.ParseIfMatch(request);
                return mediator.Send(new UpdateProjectCommand(id, body.Title, body.Description, ifMatch), ct)
                    .ToHttpResponseAsync(
                        ProjectResponse.From,
                        opts => opts
                            .WithETag(p => EntityTagValue.Strong(p.ETag))
                            .WithLastModified(p => p.LastModified));
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
