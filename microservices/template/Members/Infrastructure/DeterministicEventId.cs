using System.Security.Cryptography;
using System.Text;

namespace ProjectTrackerTemplate.Members.Infrastructure;

// Derives a STABLE, deterministic event id from a member's business key. Trellis' outbox is
// at-least-once and re-runs a translator on retry, so one invitation can be published more than once,
// each copy enrolled under a different outbox row id. Hashing the business key — rather than minting a
// fresh Guid per translation — makes every copy of one invitation share an identity, so the consumer's
// inbox, keyed on (ConsumerId, MessageId), collapses the redeliveries to a single effect. This is the
// concrete embodiment of "dedupe on business identity, not the message id."
internal static class DeterministicEventId
{
    public static Guid ForMember(string memberId)
    {
        // RFC 9562 version-8 (custom) UUID: 16 bytes of a SHA-256 over a namespaced key, with the
        // version + variant bits stamped so the result is a well-formed, collision-resistant UUID.
        var key = $"projecttracker:member-invited:{memberId}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);

        Span<byte> bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant RFC 4122
        // Interpret the bytes in big-endian (RFC) order so the version/variant land in the bytes stamped
        // above; the default Guid(byte[]) ctor uses a mixed-endian layout that would move the version nibble.
        return new Guid(bytes, bigEndian: true);
    }
}
