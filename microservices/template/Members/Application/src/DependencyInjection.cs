using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Members.Application;

// Registers the Application layer. Kept here (not in the host's Program.cs) so the assembly that owns
// the command/query handlers also owns their Mediator + dispatch wiring — and so AddDomainEventDispatch
// scans THIS assembly, where the IDomainEventHandlers (the audit logger and the integration-event
// translator) live. After the assembly split, a scan of the Domain assembly would not find them.
public static class DependencyInjection
{
    public static IServiceCollection AddMembersApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddTrellisBehaviors();

        // Member.Invite raises MemberInvited; the dispatch finds this assembly's handlers — the audit
        // logger and the translator that Add()s the external MemberInvitedIntegrationEvent to the collector.
        services.AddDomainEventDispatch(typeof(MemberInvitedTranslator).Assembly);

        // The integration-event collector + dispatch. The Acl layer replaces the default in-process
        // publisher with the Service Bus adapter so the relay delivers events to other services.
        services.AddIntegrationEventDispatch();

        return services;
    }
}
