using Trellis;

namespace ProjectTrackerTemplate.Members.Domain;

// Typed identifier for the Member aggregate. RequiredString<TSelf> from
// Trellis.Primitives gives value-object semantics, validation, JSON conversion,
// and EF/scalar-binding integration for free.
public sealed partial class MemberId : RequiredString<MemberId>;
