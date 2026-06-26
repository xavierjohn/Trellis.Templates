using Trellis;

namespace ProjectTrackerTemplate.SharedKernel;

// SHARED KERNEL (Evans' context-mapping pattern). A small, deliberately minimal model that the
// Projects and Members bounded contexts CO-OWN and agree on byte-for-byte. tenant_id is the
// cross-cutting platform identity: the gateway stamps it into every internal JWT and every service
// authorizes against it, so all contexts MUST share one definition or cross-service tenant matching
// silently breaks. A change here is a cross-context decision — guard it with shared tests.
//
// Keep the kernel SMALL. Only genuinely cross-cutting contracts belong here; service-local identities
// (ProjectId, MemberId) and aggregates stay in their own service (Separate Ways), free to evolve
// independently. See UBIQUITOUS-LANGUAGE.md for the terms shared across contexts.
//
// Modeling tenant_id as a RequiredString<TSelf> value object (rather than a raw string) gives
// value-object equality, JSON conversion, and a source-generated IParsable<TSelf> — so the actor's
// tenant_id ABAC claim is read type-safely via actor.GetRequiredAttribute<TenantId> /
// actor.TryGetAttribute<TenantId> instead of a raw Attributes["tenant_id"] string lookup. It is
// compared EXACTLY (no [Trim]) so authorization matches the raw ABAC principal; [NotDefault] rejects
// an empty/default tenant id (Required* primitives are lenient by default).
[NotDefault]
public sealed partial class TenantId : RequiredString<TenantId>;
