using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectTrackerTemplate.Projects.Acl;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

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
            "acme", "acme-newperson", "contributor", DateTimeOffset.UtcNow);

        await handler.HandleAsync(evt, ct);
        await _db.SaveChangesResultUnitAsync(ct).BeSuccessAsync();

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
            "acme", "acme-newperson", "contributor", DateTimeOffset.UtcNow);

        await handler.HandleAsync(evt, ct);
        await _db.SaveChangesResultUnitAsync(ct).BeSuccessAsync();
        await handler.HandleAsync(evt, ct); // redelivery of the same member
        await _db.SaveChangesResultUnitAsync(ct).BeSuccessAsync();

        (await _db.KnownMembers.ToListAsync(ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task Inbox_deduplicates_transport_retries_and_handler_deduplicates_new_rows_for_the_same_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ProjectsDbContext>(options =>
            options.UseSqlite(_connection).AddTrellisInterceptors());
        services.AddTrellisInbox<ProjectsDbContext>(options => options.ConsumerId = MessagingTopology.ProjectsSubscriptionName);
        services.AddIntegrationEventHandler<MemberInvitedIntegrationEvent, MemberInvitedHandler>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var evt = new MemberInvitedIntegrationEvent("acme", "acme-newperson", "contributor", DateTimeOffset.UtcNow);
        var first = new IntegrationEnvelope(Guid.CreateVersion7(), evt);

        (await dispatcher.DispatchAsync(first, ct)).Should().Be(InboxDispatchOutcome.Processed);
        (await dispatcher.DispatchAsync(first, ct)).Should().Be(InboxDispatchOutcome.SkippedDuplicate);
        (await dispatcher.DispatchAsync(new IntegrationEnvelope(Guid.CreateVersion7(), evt), ct))
            .Should().Be(InboxDispatchOutcome.Processed);

        (await _db.KnownMembers.AsNoTracking().ToListAsync(ct)).Should().ContainSingle();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}