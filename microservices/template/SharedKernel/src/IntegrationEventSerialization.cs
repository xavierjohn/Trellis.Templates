using System.Text.Json;

namespace ProjectTrackerTemplate.SharedKernel;

// One serialization contract shared by the producer's transport publisher and the consumer's
// transport pump, so an event written by Members deserializes byte-for-byte in Projects. The
// integration contracts are primitive-only (see MemberInvitedIntegrationEvent), so the stock Web
// defaults are sufficient — no custom converters travel with the payload. Centralizing the options
// here keeps both sides from drifting (a casing or null-handling mismatch would silently break
// cross-service binding).
public static class IntegrationEventSerialization
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
