namespace ProjectTrackerTemplate.Projects.Domain;

// Permission scopes for the Projects service. Centralizing them lets handlers reference a compile-checked
// symbol (Permissions.ProjectsRead) instead of a magic string literal — no typos, one source of truth,
// find-all-references. They stay plain strings on purpose: a permission is an OAuth/JWT scope the gateway
// mints into the actor's permissions claim, and Trellis authorizes against that string
// (IAuthorize.RequiredPermissions is IReadOnlyList<string>). A value-object wrapper would only add a
// .Value conversion at every call site without changing what travels on the wire.
public static class Permissions
{
    public const string ProjectsRead = "projects:read";
    public const string ProjectsWrite = "projects:write";
}
