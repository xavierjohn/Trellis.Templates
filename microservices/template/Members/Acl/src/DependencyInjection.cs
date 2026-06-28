using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Members.Acl;

// Registers the anti-corruption / infrastructure layer: the EF Core context (SQL Server via Aspire) with
// the Trellis interceptors + outbox capture, the repository, resource-based authorization, the unit of
// work, the outbox relay, and the Service Bus integration-event publisher that replaces the in-process
// default so events leave the process. Takes the host builder because the Aspire component registrations
// (AddSqlServerDbContext, AddAzureServiceBusClient) hang off IHostApplicationBuilder.
public static class DependencyInjection
{
    public static IHostApplicationBuilder AddMembersAcl(this IHostApplicationBuilder builder)
    {
        // EF Core over SQL Server. Aspire injects the "membersdb" connection string (see AppHost) and adds
        // connection resilience, health checks, and telemetry; AddTrellisInterceptors stamps the ETag +
        // timestamps and rewrites value-object / Maybe<T> queries; the outbox interceptor captures domain
        // events in the SAME transaction as the aggregate.
        builder.AddSqlServerDbContext<MembersDbContext>("membersdb",
            configureDbContextOptions: options => options
                .AddTrellisInterceptors()
                .AddTrellisOutboxInterceptor());

        // Azure Service Bus client (Aspire injects the "messaging" connection string — the local emulator
        // in dev, a real namespace in production).
        builder.AddAzureServiceBusClient(MemberEventsChannel.ConnectionName);

        var services = builder.Services;

        services.AddScoped<IMemberRepository, EfMemberRepository>();

        // Resource-based authorization scans the Application assembly (the commands/queries that bind a
        // Member resource) and this assembly (the MemberResourceLoader). HideExistence<Member>() is the
        // single line that makes Members "HR-sensitive": a cross-tenant failure is projected to 404, not 403.
        services.AddResourceAuthorization(
            typeof(InviteMemberCommand).Assembly,
            typeof(MemberResourceLoader).Assembly);
        services.AddResourceAuthorization(options => options.HideExistence<Member>());

        // Replace the default in-process integration-event publisher with the Service Bus adapter so the
        // outbox relay delivers MemberInvited to other services. The aggregates, translator, and outbox do
        // not change — only this registration.
        services.Replace(ServiceDescriptor.Singleton<IIntegrationEventPublisher, ServiceBusIntegrationEventPublisher>());

        // The EF unit of work (TransactionalCommandBehavior commits on command-handler success) + the
        // outbox relay that drains captured rows after the commit and publishes the integration events.
        services.AddTrellisUnitOfWork<MembersDbContext>();
        services.AddTrellisOutbox<MembersDbContext>();

        return builder;
    }

    // Dev-only schema creation + demo seed (two tenants, two members each). Production uses EF migrations.
    public static async Task SeedMembersDevelopmentDataAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MembersDbContext>();
        await MembersSeed.EnsureSeededAsync(db, cancellationToken);
    }
}
