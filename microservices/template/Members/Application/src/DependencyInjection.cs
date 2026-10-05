using Microsoft.Extensions.DependencyInjection;

namespace ProjectTrackerTemplate.Members.Application;

// Registers the Application layer. Kept here (not in the host's Program.cs) so the assembly that owns
// the command/query handlers also owns their source-generated Mediator registration. The API root
// selects Trellis's pipeline and scans this assembly for domain-event handlers.
public static class DependencyInjection
{
    public static IServiceCollection AddMembersApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        return services;
    }
}