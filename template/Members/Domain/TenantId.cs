using Trellis;

namespace ProjectTrackerTemplate.Members.Domain;

// Typed identifier for the tenant an aggregate belongs to. Modeling tenant_id as a
// RequiredString<TSelf> value object (rather than a raw string) gives value-object
// equality, JSON conversion, and a source-generated IParsable<TSelf> — so the actor's
// tenant_id ABAC claim is read type-safely via actor.GetRequiredAttribute<TenantId> /
// actor.TryGetAttribute<TenantId> instead of a raw Attributes["tenant_id"] string lookup.
// It is compared EXACTLY (no [Trim]) so authorization matches the raw ABAC principal; [NotDefault]
// rejects an empty/default tenant id (Required* primitives are lenient by default).
[NotDefault]
public sealed partial class TenantId : RequiredString<TenantId>;
