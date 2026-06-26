namespace ProjectTrackerTemplate.SharedKernel;

// The transport coordinates for the MemberInvited published language: the Service Bus connection and the
// queue Members publishes member events to / Projects consumes from. These are part of the integration
// contract — both services must agree on them — so they live beside the event. The AppHost declares a
// matching Service Bus queue by the same name (see AppHost/Program.cs).
//
// A QUEUE (point-to-point) fits this template's single consumer: Members is the only producer of member
// events and Projects the only consumer, so "member-events" reads as a logical event channel. To fan a
// published event out to SEVERAL independent services, switch to a topic with a per-consumer subscription
// (on the local Service Bus emulator each subscription needs at least one rule — e.g. a correlation filter
// on the message Subject — because the emulator does not create the implicit match-all rule Azure does).
public static class MemberEventsChannel
{
    // The Aspire connection name. Both services resolve their ServiceBusClient with it
    // (builder.AddAzureServiceBusClient), and the AppHost models the namespace under the same name.
    public const string ConnectionName = "messaging";

    // The queue Members publishes member events to and Projects consumes.
    public const string QueueName = "member-events";
}
