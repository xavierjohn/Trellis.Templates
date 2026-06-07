using System.Collections.Concurrent;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Projects.Infrastructure;

// In-memory project store seeded with two projects per tenant for the
// outcome-matrix walkthrough. Real services would resolve a DbContext /
// Cosmos client / HTTP client from DI; the seed pattern makes the template
// self-contained for a click-to-send tour.
//
// FindByIdAsync emits the projects.resource_loads counter + a structured log
// line on EVERY call. That instrumentation is the proof of the "load once"
// invariant — see ARCHITECTURE.md §RBAC.
//
// Registered as SINGLETON so the seeded dictionary persists across requests.
// Singleton means no scoped service injection — that's why the counter is
// tagged only with project.id (not actor.id).
public sealed partial class InMemoryProjectRepository : IProjectRepository
{
    private readonly ConcurrentDictionary<string, Project> _projects;
    private readonly ILogger<InMemoryProjectRepository> _logger;

    public InMemoryProjectRepository(ILogger<InMemoryProjectRepository> logger)
    {
        _logger = logger;

        _projects = new ConcurrentDictionary<string, Project>(StringComparer.Ordinal);

        // Seed: two tenants ("acme", "globex"); each tenant has two projects
        // owned by different members. The Sample.http scenarios exercise:
        //   - alice (tenant=acme) reads her own project (200)
        //   - alice (tenant=acme) reads bob's project in acme (200)
        //   - alice (tenant=acme) updates her own project (204)
        //   - alice (tenant=acme) updates bob's project in acme (403, not owner)
        //   - alice (tenant=acme) reads a globex project (403, cross-tenant)
        //   - alice without orders:write permission (403, missing permission)
        Seed(new Project(MakeId("acme-p1"),   ownerId: "alice", tenantId: "acme",   title: "Q1 launch",        description: "Coordinate Q1 marketing launch."));
        Seed(new Project(MakeId("acme-p2"),   ownerId: "bob",   tenantId: "acme",   title: "Onboarding",       description: "Refactor onboarding funnel."));
        Seed(new Project(MakeId("globex-p1"), ownerId: "carol", tenantId: "globex", title: "Beta rollout",     description: "Phase-3 beta to enterprise."));
        Seed(new Project(MakeId("globex-p2"), ownerId: "dave",  tenantId: "globex", title: "Annual audit",     description: "Compliance audit prep."));
    }

    private static ProjectId MakeId(string id) =>
        ProjectId.TryCreate(id).GetValueOrThrow($"seed ProjectId('{id}') must be valid");

    private void Seed(Project project) => _projects[project.Id.Value] = project;

    public ValueTask<Maybe<Project>> FindByIdAsync(ProjectId id, CancellationToken cancellationToken)
    {
        ProjectsMetrics.ResourceLoads.Add(
            1,
            new KeyValuePair<string, object?>("project.id", id.Value));

        LogProjectResourceLoaded(_logger, id.Value);

        return ValueTask.FromResult(_projects.TryGetValue(id.Value, out var project)
            ? Maybe.From(project)
            : Maybe<Project>.None);
    }

    public ValueTask<IReadOnlyList<Project>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Project>>(
            _projects.Values
                .Where(p => string.Equals(p.TenantId, tenantId, StringComparison.Ordinal))
                .OrderBy(p => p.Id.Value, StringComparer.Ordinal)
                .ToArray());

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        EventName = "ProjectResourceLoaded",
        Message = "Loaded Project {ProjectId} from the ACL repository. This is the signal that proves load-once.")]
    private static partial void LogProjectResourceLoaded(ILogger logger, string projectId);
}
