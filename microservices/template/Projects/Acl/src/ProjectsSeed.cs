using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Projects.Acl;

// Dev-only seed so the Project Tracker outcome matrix is self-contained: two tenants ("acme", "globex")
// with two projects each, owned by different members. Production would use EF migrations and a real
// seed/bootstrap strategy rather than EnsureCreated.
public static class ProjectsSeed
{
    public static async Task EnsureSeededAsync(ProjectsDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Projects.AnyAsync(cancellationToken))
            return;

        db.Projects.AddRange(
            NewProject("acme-p1", "alice", "acme", "Q1 launch", "Coordinate Q1 marketing launch."),
            NewProject("acme-p2", "bob", "acme", "Onboarding", "Refactor onboarding funnel."),
            NewProject("globex-p1", "carol", "globex", "Beta rollout", "Phase-3 beta to enterprise."),
            NewProject("globex-p2", "dave", "globex", "Annual audit", "Compliance audit prep."));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Project NewProject(string id, string ownerId, string tenant, string title, string description) =>
        new(
            ProjectId.TryCreate(id).GetValueOrThrow($"seed ProjectId('{id}') must be valid"),
            ownerId,
            TenantId.TryCreate(tenant).GetValueOrThrow($"seed TenantId('{tenant}') must be valid"),
            ProjectTitle.TryCreate(title).GetValueOrThrow($"seed title('{title}') must be valid"),
            ProjectDescription.TryCreate(description).GetValueOrThrow($"seed description('{description}') must be valid"));
}
