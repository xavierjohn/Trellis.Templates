using Trellis;

namespace ProjectTrackerTemplate.Members.Domain;

// The role a member holds inside a tenant. Modelled as a RequiredEnum value object — a closed set —
// rather than a raw string, so an unknown role is rejected at the boundary (422) instead of being
// silently persisted. The wire form is the lowercase symbolic name ("owner" / "contributor"), pinned
// with [EnumValue] so the published contract stays stable even if the C# field names are renamed.
public partial class Role : RequiredEnum<Role>
{
    [EnumValue("owner")]
    public static readonly Role Owner = new();

    [EnumValue("contributor")]
    public static readonly Role Contributor = new();
}
