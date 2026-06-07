using Trellis;

namespace ProjectTrackerTemplate.Projects.Domain;

// Typed identifier for the Project aggregate. RequiredString<TSelf> from
// Trellis.Primitives gives value-object semantics, validation, JSON conversion,
// and EF/scalar-binding integration for free.
public sealed partial class ProjectId : RequiredString<ProjectId>;
