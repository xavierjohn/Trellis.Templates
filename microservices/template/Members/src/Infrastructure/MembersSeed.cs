using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Members.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Members.Infrastructure;

// Dev-only seed so the Project Tracker outcome matrix is self-contained: two tenants ("acme",
// "globex") with two members each. MemberIds are TENANT-SCOPED ("{tenant}-{localPart}") so
// cross-tenant collisions are impossible by construction. Production would use EF migrations and a
// real seed/bootstrap strategy rather than EnsureCreated.
public static class MembersSeed
{
    public static async Task EnsureSeededAsync(MembersDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        if (await db.Members.AnyAsync(cancellationToken).ConfigureAwait(false))
            return;

        db.Members.AddRange(
            NewMember("acme-alice", "acme", "alice@acme.example", "owner"),
            NewMember("acme-bob", "acme", "bob@acme.example", "contributor"),
            NewMember("globex-carol", "globex", "carol@globex.example", "owner"),
            NewMember("globex-dave", "globex", "dave@globex.example", "contributor"));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Member NewMember(string id, string tenant, string email, string role) =>
        new(
            MemberId.TryCreate(id).GetValueOrThrow($"seed MemberId('{id}') must be valid"),
            TenantId.TryCreate(tenant).GetValueOrThrow($"seed TenantId('{tenant}') must be valid"),
            email,
            role);
}
