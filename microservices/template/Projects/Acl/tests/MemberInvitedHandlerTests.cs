using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Projects.Acl;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.EntityFrameworkCore;

namespace Projects.Acl.Tests;

// Exercises the cross-service eventing consumer against a real (in-memory SQLite) relational store, so
// the value-object conventions, the composite key, and the idempotent upsert all run as they would on
// SQL Server. The inbox dispatcher normally calls SaveChanges; here the test does it (the handler only
// stages, as the inbox contract requires).
public sealed class MemberInvitedHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProjectsDbContext _db;

    public MemberInvitedHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new ProjectsDbContext(new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseSqlite(_connection)
            .AddTrellisInterceptors()
            .Options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Upserts_the_invited_member_into_the_read_model()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MemberInvitedHandler(_db);
        var evt = new MemberInvitedIntegrationEvent(
            Guid.NewGuid(), "acme", "acme-newperson", "contributor", DateTimeOffset.UtcNow);

        await handler.HandleAsync(evt, ct);
        await _db.SaveChangesAsync(ct);

        var rows = await _db.KnownMembers.ToListAsync(ct);
        rows.Should().ContainSingle();
        rows[0].MemberId.Should().Be("acme-newperson");
        rows[0].TenantId.Value.Should().Be("acme");
        rows[0].Role.Should().Be("contributor");
    }

    [Fact]
    public async Task Is_idempotent_for_a_redelivered_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MemberInvitedHandler(_db);
        var evt = new MemberInvitedIntegrationEvent(
            Guid.NewGuid(), "acme", "acme-newperson", "contributor", DateTimeOffset.UtcNow);

        await handler.HandleAsync(evt, ct);
        await _db.SaveChangesAsync(ct);
        await handler.HandleAsync(evt, ct); // redelivery of the same member
        await _db.SaveChangesAsync(ct);

        (await _db.KnownMembers.ToListAsync(ct)).Should().ContainSingle();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
