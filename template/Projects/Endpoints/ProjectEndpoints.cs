namespace ProjectTrackerTemplate.Projects.Endpoints;

using Asp.Versioning;
using Asp.Versioning.Builder;
using Mediator;
using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.Domain;
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

        projects.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new ListProjectsQuery(), ct);
                return result.ToHttpResponse(items => items.Select(ProjectResponse.From).ToArray());
            });

        projects.MapGet("/{id}", async (ProjectId id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetProjectQuery(id), ct);
                return result.ToHttpResponse(ProjectResponse.From);
            });

        projects.MapPut("/{id}", async (ProjectId id, UpdateProjectRequest body, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new UpdateProjectCommand(id, body.Title, body.Description), ct);
                return result.ToHttpResponse();
            });

        return app;
    }
}

// Wire-format DTOs for the Projects API. UpdateProjectRequest is intentionally not validated here —
// production would make UpdateProjectCommand implement IValidate (Trellis.Mediator.FluentValidation)
// so ValidationBehavior rejects bad input before the handler runs.
internal sealed record UpdateProjectRequest(string Title, string Description);

internal sealed record ProjectResponse(string Id, string OwnerId, string TenantId, string Title, string Description)
{
    public static ProjectResponse From(Project p) =>
        new(p.Id.Value, p.OwnerId, p.TenantId.Value, p.Title, p.Description);
}
