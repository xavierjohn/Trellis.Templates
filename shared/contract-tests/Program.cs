using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using YamlDotNet.RepresentationModel;

// usage: <manifest.yaml> <templateId> <instantiatedRoot>
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: trellis-contract-tests <manifest.yaml> <templateId> <instantiatedRoot>");
    return 2;
}
var (manifestPath, templateId, root) = (args[0], args[1], args[2]);
if (templateId is not ("asp" or "microservices"))
{
    Console.Error.WriteLine($"Unknown template '{templateId}'; expected 'asp' or 'microservices'.");
    return 2;
}
var profilePath = Path.Combine(root, ".trellis-template.json");
using var profileDocument = JsonDocument.Parse(File.ReadAllText(profilePath));
var profile = profileDocument.RootElement.EnumerateObject()
    .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
if (profile["template"] != templateId)
{
    Console.Error.WriteLine($"Profile '{profilePath}' belongs to '{profile["template"]}', not '{templateId}'.");
    return 2;
}
if (!bool.TryParse(profile["apiVersioning"], out _)
    || profile["database"] is not ("sqlite" or "postgres" or "sqlserver")
    || profile["auth"] is not ("jwt" or "entra")
    || profile["telemetryExporters"] is not ("otlp" or "azure-monitor" or "both")
    || profile["deployment"] is not ("none" or "container" or "azure")
    || (profile["database"] == "sqlite" && (templateId != "asp" || profile["deployment"] == "azure")))
{
    Console.Error.WriteLine($"Profile '{profilePath}' contains invalid or unexpanded template options.");
    return 2;
}

var yaml = new YamlStream();
using (var sr = new StreamReader(manifestPath)) yaml.Load(sr);
var docRoot = (YamlMappingNode)yaml.Documents[0].RootNode;
var caps = (YamlMappingNode)docRoot["capabilities"];

Console.WriteLine($"Capability contract — template '{templateId}'   root = {root}\n");
int failures = 0, skips = 0;

foreach (var entry in caps.Children)
{
    var capName = ((YamlScalarNode)entry.Key).Value!;
    var cap = (YamlMappingNode)entry.Value;
    var requiredFor = ((YamlSequenceNode)cap["requiredFor"]).Select(n => ((YamlScalarNode)n).Value).ToHashSet();
    if (!requiredFor.Contains(templateId)) continue;
    if (!Applies(cap, profile)) continue;

    bool planned = Has(cap, "status") && ((YamlScalarNode)cap["status"]).Value == "planned";
    var fails = new List<string>();
    int capSkips = 0;

    foreach (var c in (YamlSequenceNode)cap["checks"])
    {
        if (!Applies((YamlMappingNode)c, profile)) continue;
        var (state, desc) = RunCheck((YamlMappingNode)c, root);
        if (state == "fail") fails.Add(desc);
        else if (state == "skip") capSkips++;
    }

    string label;
    if (fails.Count == 0) label = capSkips > 0 ? "PASS(+skip)" : "PASS";
    else if (planned) label = "PLANNED";
    else { label = "FAIL"; failures++; }
    skips += capSkips;

    Console.WriteLine($"  [{label,-11}] {capName}");
    foreach (var f in fails) Console.WriteLine($"                  missing: {f}");
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? $"OK — all required capabilities present ({skips} runtime check(s) skipped in POC)"
    : $"DRIFT — {failures} required capability(ies) MISSING");
return failures == 0 ? 0 : 1;

(string, string) RunCheck(YamlMappingNode chk, string root)
{
    switch (((YamlScalarNode)chk["kind"]).Value)
    {
        case "source-contains":
            if (Has(chk, "anyOf"))
            {
                foreach (var sub in (YamlSequenceNode)chk["anyOf"])
                {
                    var m = (YamlMappingNode)sub;
                    if (SourceContains(root, V(m, "glob"), V(m, "pattern"))) return ("pass", "");
                }
                return ("fail", "any-of source pattern");
            }
            return SourceContains(root, V(chk, "glob"), V(chk, "pattern"))
                ? ("pass", "") : ("fail", $"{V(chk, "glob")} ~ /{V(chk, "pattern")}/");

        case "package-referenced":
            var pkg = V(chk, "package");
            return SourceContains(root, "**/*.csproj", $"PackageReference\\s+Include=\"{Regex.Escape(pkg)}\"")
                ? ("pass", "") : ("fail", $"package {pkg}");

        case "package-not-referenced":
            var absentPackage = V(chk, "package");
            return !SourceContains(root, "**/*.csproj", $"PackageReference\\s+Include=\"{Regex.Escape(absentPackage)}\"")
                ? ("pass", "") : ("fail", $"unexpected package {absentPackage}");

        case "source-not-contains":
            return !SourceContains(root, V(chk, "glob"), V(chk, "pattern"))
                ? ("pass", "") : ("fail", $"unexpected {V(chk, "glob")} ~ /{V(chk, "pattern")}/");

        case "files-absent":
            return !FindFiles(root, V(chk, "glob")).Any()
                ? ("pass", "") : ("fail", $"unexpected files matching {V(chk, "glob")}");

        case "http-status":
        case "builds":
        case "docs-in-sync":    // These declarative runtime checks are not executed by this source runner.
            return ("skip", "");

        default:
            throw new InvalidDataException($"Unknown parity check kind '{V(chk, "kind")}'.");
    }
}

static bool SourceContains(string root, string glob, string pattern)
{
    var re = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
    return FindFiles(root, glob).Any(path => re.IsMatch(File.ReadAllText(path)));
}

static IEnumerable<string> FindFiles(string root, string glob)
{
    var matcher = new Matcher();
    matcher.AddInclude(glob);
    matcher.AddExclude("**/bin/**");
    matcher.AddExclude("**/obj/**");
    return matcher.GetResultsInFullPath(root);
}

static bool Applies(YamlMappingNode node, IReadOnlyDictionary<string, string> profile)
{
    if (!Has(node, "when")) return true;
    foreach (var condition in ((YamlMappingNode)node["when"]).Children)
    {
        var name = ((YamlScalarNode)condition.Key).Value!;
        if (!profile.TryGetValue(name, out var selected))
            throw new InvalidDataException($"Unknown template profile option '{name}' in the parity manifest.");
        var choices = condition.Value is YamlSequenceNode sequence
            ? sequence.Select(choice => ((YamlScalarNode)choice).Value!)
            : [((YamlScalarNode)condition.Value).Value!];
        if (!choices.Contains(selected, StringComparer.OrdinalIgnoreCase)) return false;
    }
    return true;
}

static bool Has(YamlMappingNode m, string key) =>
    m.Children.Keys.OfType<YamlScalarNode>().Any(s => s.Value == key);

static string V(YamlMappingNode m, string key) => ((YamlScalarNode)m[key]).Value!;
