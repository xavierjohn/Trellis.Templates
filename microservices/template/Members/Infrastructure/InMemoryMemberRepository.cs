using System.Collections.Concurrent;
using ProjectTrackerTemplate.Members.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Members.Infrastructure;

// In-memory member store seeded so the Project Tracker outcome matrix is
// self-contained. The seed actors line up with the Projects seed
// (alice + bob in tenant=acme; carol + dave in tenant=globex), so
// cross-tenant scenarios can be exercised end-to-end via Sample.http.
public sealed class InMemoryMemberRepository : IMemberRepository
{
    private readonly ConcurrentDictionary<string, Member> _members = new(StringComparer.Ordinal);

    public InMemoryMemberRepository()
    {
        // Seed: two tenants ("acme", "globex") with two members each. MemberIds are
        // TENANT-SCOPED (format: "{tenant}-{localPart}") so cross-tenant collisions
        // are impossible by construction — this is the canonical multi-tenant SaaS
        // pattern. Without tenant-scoping, an actor with members:invite could derive
        // a colliding MemberId by inviting someone with a known local-part and silently
        // overwrite a member in a DIFFERENT tenant.
        Seed(new Member(MakeId("acme-alice"),   tenantId: MakeTenant("acme"),   email: "alice@acme.example",   role: "owner"));
        Seed(new Member(MakeId("acme-bob"),     tenantId: MakeTenant("acme"),   email: "bob@acme.example",     role: "contributor"));
        Seed(new Member(MakeId("globex-carol"), tenantId: MakeTenant("globex"), email: "carol@globex.example", role: "owner"));
        Seed(new Member(MakeId("globex-dave"),  tenantId: MakeTenant("globex"), email: "dave@globex.example",  role: "contributor"));
    }

    private static MemberId MakeId(string id) =>
        MemberId.TryCreate(id).GetValueOrThrow($"seed MemberId('{id}') must be valid");

    private static TenantId MakeTenant(string tenant) =>
        TenantId.TryCreate(tenant).GetValueOrThrow($"seed TenantId('{tenant}') must be valid");

    private void Seed(Member m) => _members[m.Id.Value] = m;

    public ValueTask<Maybe<Member>> FindByIdAsync(MemberId id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_members.TryGetValue(id.Value, out var m) ? Maybe.From(m) : Maybe<Member>.None);

    public ValueTask<IReadOnlyList<Member>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Member>>(
            _members.Values
                .Where(m => m.TenantId == tenantId)
                .OrderBy(m => m.Id.Value, StringComparer.Ordinal)
                .ToArray());

    public ValueTask AddAsync(Member member, CancellationToken cancellationToken)
    {
        _members[member.Id.Value] = member;
        return ValueTask.CompletedTask;
    }
}
