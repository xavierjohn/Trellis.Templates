#pragma warning disable IDE0047
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTrackerTemplate.Members.Application;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.Messaging.AzureServiceBus;

namespace ProjectTrackerTemplate.Members.Acl;

// Registers the anti-corruption / infrastructure layer: the EF Core context (SQL Server via Aspire) with
// the Trellis interceptors + outbox capture, repository, and shipped Service Bus transport. Framework
// pipeline modules are selected together in the API root. Takes the host builder because Aspire
// (AddSqlServerDbContext, AddAzureServiceBusClient) hang off IHostApplicationBuilder.
public static class DependencyInjection
{
    public static IHostApplicationBuilder AddMembersAcl(this IHostApplicationBuilder builder)
    {
        // EF Core over SQL Server. Aspire injects the "membersdb" connection string (see AppHost) and adds
        // connection resilience, health checks, and telemetry; AddTrellisInterceptors stamps the ETag +
        // timestamps and rewrites value-object / Maybe<T> queries; the outbox interceptor captures domain
        // events in the SAME transaction as the aggregate.
#if (UsePostgres)
        builder.AddNpgsqlDbContext<MembersDbContext>("membersdb",
#else
        builder.AddSqlServerDbContext<MembersDbContext>("membersdb",
#endif
            configureDbContextOptions: options => options
                .AddTrellisInterceptors()
                .AddTrellisOutboxInterceptor());

        // Azure Service Bus client (Aspire injects the "messaging" connection string — the local emulator
        // in dev, a real namespace in production).
        builder.AddAzureServiceBusClient(MessagingTopology.ConnectionName);

        var services = builder.Services;

        services.AddScoped<IMemberRepository, EfMemberRepository>();

        var contracts = IntegrationEventNameMap.FromAssemblies(typeof(MemberInvitedIntegrationEvent).Assembly);
        services.AddAzureServiceBusIntegrationEventPublisher(contracts, options => options.MessageSource = "members");

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