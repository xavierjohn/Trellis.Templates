using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTrackerTemplate.Projects.Application;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.Messaging.AzureServiceBus;

namespace ProjectTrackerTemplate.Projects.Acl;

// Registers the anti-corruption / infrastructure layer: the EF Core context (SQL Server via Aspire) that
// holds the Project aggregate, inbox dedup table and team read model; repositories, projection handler,
// and shipped Service Bus transport. Framework modules are selected together in the API root. Aspire
// registrations hang off IHostApplicationBuilder.
public static class DependencyInjection
{
    public static IHostApplicationBuilder AddProjectsAcl(this IHostApplicationBuilder builder)
    {
        // Azure Service Bus client (Aspire injects the "messaging" connection — local emulator in dev).
        builder.AddAzureServiceBusClient(MessagingTopology.ConnectionName);

        // EF Core over SQL Server for the inbox dedup table + the read models. Aspire injects "projectsdb";
        // AddTrellisInterceptors wires the value-object column conventions.
        builder.AddSqlServerDbContext<ProjectsDbContext>("projectsdb",
            configureDbContextOptions: options => options.AddTrellisInterceptors());

        var services = builder.Services;

        // The Project aggregate, the team read model, and the inbox all use the EF Core context above.
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IKnownMemberDirectory, KnownMemberDirectory>();

        services.AddIntegrationEventHandler<MemberInvitedIntegrationEvent, MemberInvitedHandler>();
        var contracts = IntegrationEventNameMap.FromAssemblies(typeof(MemberInvitedIntegrationEvent).Assembly);
        var topic = contracts.NameFor(typeof(MemberInvitedIntegrationEvent))
            .GetValueOrThrow("MemberInvited must declare an IntegrationEventName.");
        services.AddAzureServiceBusIntegrationEventConsumer(contracts,
            options => options.Subscribe(topic, MessagingTopology.ProjectsSubscriptionName));

        return builder;
    }

    // Dev-only: create the inbox + read-model + Project schema. Production uses EF migrations. Runs
    // before the Service Bus pump starts (hosted services start on app.Run) so the consumer always
    // finds its tables.
    public static async Task EnsureProjectsCreatedAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await ProjectsSeed.EnsureSeededAsync(db, cancellationToken);
    }
}