using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Members.Acl;
using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.EntityFrameworkCore;

namespace Members.Acl.Tests;

// Exercises the EF Core repository against a real (in-memory SQLite) relational store, so the value-object
// conventions, the tenant-scoped query, and the staging behaviour all run as they would on SQL Server.
public sealed class EfMemberRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MembersDbContext _db;

    public EfMemberRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new MembersDbContext(new DbContextOptionsBuilder<MembersDbContext>()
            .UseSqlite(_connection)
            .AddTrellisInterceptors()
            .Options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task ListByTenantAsync_returns_only_the_callers_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new EfMemberRepository(_db);
        repository.Add(NewMember("acme-alice", "acme"));
        repository.Add(NewMember("acme-bob", "acme"));
        repository.Add(NewMember("globex-carol", "globex"));
        await _db.SaveChangesAsync(ct);

        var acme = await repository.ListByTenantAsync(
            TenantId.TryCreate("acme").GetValueOrThrow("valid tenant"), ct);

        acme.Select(m => m.Id.Value).Should().BeEquivalentTo("acme-alice", "acme-bob");
    }

    private static Member NewMember(string id, string tenant) =>
        new(
            MemberId.TryCreate(id).GetValueOrThrow("valid id"),
            TenantId.TryCreate(tenant).GetValueOrThrow("valid tenant"),
            $"{id}@example.test",
            "contributor");

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
