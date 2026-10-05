namespace ProjectTrackerTemplate.SharedKernel;

// Deployment coordinates, separate from contract identity. Trellis derives the topic and Subject from
// IntegrationEventName; each independent consumer owns its subscription and stable inbox ConsumerId.
public static class MessagingTopology
{
    // The Aspire connection name. Both services resolve their ServiceBusClient with it
    // (builder.AddAzureServiceBusClient), and the AppHost models the namespace under the same name.
    public const string ConnectionName = "messaging";

    public const string ProjectsSubscriptionName = "projects";
}