using Trellis;

namespace ProjectTrackerTemplate.Members.Domain;

// Typed identifier for the Member aggregate. RequiredString<TSelf> from
// Trellis.Primitives gives value-object semantics, JSON conversion, and
// EF/scalar-binding integration for free. [Trim, NotDefault] makes TryCreate
// reject null/empty/whitespace (Required* primitives are lenient by default).
[Trim, NotDefault]
public sealed partial class MemberId : RequiredString<MemberId>;
