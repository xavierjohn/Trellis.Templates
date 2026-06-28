using Trellis;

namespace ProjectTrackerTemplate.Projects.Domain;

// Title of a project. 1–200 characters, modelled as a RequiredString value object so an empty or
// over-long title is rejected at the boundary (422) instead of persisted.
[Trim, NotDefault, StringLength(200)]
public sealed partial class ProjectTitle : RequiredString<ProjectTitle>;
