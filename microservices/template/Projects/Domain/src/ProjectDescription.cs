using Trellis;

namespace ProjectTrackerTemplate.Projects.Domain;

// Free-text description of a project. Up to 1000 characters, modelled as a RequiredString value object
// so an empty or over-long value is rejected at the boundary (422) instead of persisted.
[Trim, NotDefault, StringLength(1000)]
public sealed partial class ProjectDescription : RequiredString<ProjectDescription>;
