using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Projects.Application;

// Registers the Application layer: the Mediator pipeline (commands/queries + Trellis behaviors). Kept here
// (not in the host's Program.cs) so the assembly that owns the handlers also owns their Mediator wiring.
// Unlike Members, Projects raises no domain events (it is the consumer), so there is no dispatch to wire.
public static class DependencyInjection
{
    public static IServiceCollection AddProjectsApplication(this IServiceCollection services)
    {
        // Handlers MUST be Scoped because IAuthorizedResource<TMessage, TResource> (the v4 typed accessor
        // ResourceAuthorizationBehavior populates) is scoped; Mediator's default Singleton would not bind.
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddTrellisBehaviors();
        return services;
    }
}
