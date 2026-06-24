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

    bool planned = Has(cap, "status") && ((YamlScalarNode)cap["status"]).Value == "planned";
    var fails = new List<string>();
    int capSkips = 0;

    foreach (var c in (YamlSequenceNode)cap["checks"])
    {
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

        default:               // http-status / builds / docs-in-sync — runtime, skipped in POC
            return ("skip", "");
    }
}

static bool SourceContains(string root, string glob, string pattern)
{
    var matcher = new Matcher();
    matcher.AddInclude(glob);
    Regex re;
    try { re = new Regex(pattern); } catch { return false; }
    foreach (var path in matcher.GetResultsInFullPath(root))
    {
        try { if (re.IsMatch(File.ReadAllText(path))) return true; } catch { /* ignore */ }
    }
    return false;
}

static bool Has(YamlMappingNode m, string key) =>
    m.Children.Keys.OfType<YamlScalarNode>().Any(s => s.Value == key);

static string V(YamlMappingNode m, string key) => ((YamlScalarNode)m[key]).Value!;
