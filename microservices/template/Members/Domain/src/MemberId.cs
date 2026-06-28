using Trellis;

namespace ProjectTrackerTemplate.Members.Domain;

// Typed identifier for the Member aggregate. RequiredString<TSelf> from
// Trellis.Core gives value-object semantics, JSON conversion, and
// EF/scalar-binding integration for free. [Trim, NotDefault] makes TryCreate
// reject null/empty/whitespace; [StringLength(128)] bounds it to the storage
// column width, so an over-long derived id fails validation (422) rather than
// blowing up at SaveChanges (500). Required* primitives are lenient by default.
[Trim, NotDefault, StringLength(128)]
public sealed partial class MemberId : RequiredString<MemberId>;
