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
        Seed(new Member(MakeId("alice"), tenantId: "acme",   email: "alice@acme.example",   role: "owner"));
        Seed(new Member(MakeId("bob"),   tenantId: "acme",   email: "bob@acme.example",     role: "contributor"));
        Seed(new Member(MakeId("carol"), tenantId: "globex", email: "carol@globex.example", role: "owner"));
        Seed(new Member(MakeId("dave"),  tenantId: "globex", email: "dave@globex.example",  role: "contributor"));
    }

    private static MemberId MakeId(string id) =>
        MemberId.TryCreate(id).GetValueOrThrow($"seed MemberId('{id}') must be valid");

    private void Seed(Member m) => _members[m.Id.Value] = m;

    public ValueTask<Maybe<Member>> FindByIdAsync(MemberId id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_members.TryGetValue(id.Value, out var m) ? Maybe.From(m) : Maybe<Member>.None);

    public ValueTask<IReadOnlyList<Member>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Member>>(
            _members.Values
                .Where(m => string.Equals(m.TenantId, tenantId, StringComparison.Ordinal))
                .OrderBy(m => m.Id.Value, StringComparer.Ordinal)
                .ToArray());

    public ValueTask AddAsync(Member member, CancellationToken cancellationToken)
    {
        _members[member.Id.Value] = member;
        return ValueTask.CompletedTask;
    }
}
