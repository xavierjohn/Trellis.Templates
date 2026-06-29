// Emits the convention-derived Azure resource names for one deployment context as JSON.
//
// This is the C# -> IaC seam: the SAME Trellis.ResourceNaming.Azure library the application uses at
// runtime computes the names here, so the deployment and the running service agree on every name by
// construction (including the Hash5 global-uniqueness suffix, which Bicep cannot reproduce).
// deploy.ps1 runs this once per stack and feeds the JSON into Bicep as parameters.
//
//   Global (cloud-singletons, no region):
//     dotnet run --project deploy/names -- --system tdo --environment prod --cloud AzureCloud --scope Shared
//   Regional (one per region):
//     dotnet run --project deploy/names -- --system tdo --environment prod --cloud AzureCloud --scope Shared \
//                                          --region westus3 --region-short usw3 --out names.usw3.json

using System.Text.Json;
using System.Text.Json.Nodes;
using Trellis.ResourceNaming;
using Trellis.ResourceNaming.Azure;

var options = ParseArgs(args);

var scope = options.TryGetValue("scope", out var scopeRaw) && !string.IsNullOrWhiteSpace(scopeRaw)
    ? Enum.Parse<CloudScope>(scopeRaw, ignoreCase: true)
    : CloudScope.Shared;

options.TryGetValue("region", out var region);
options.TryGetValue("region-short", out var regionShort);

// --region and --region-short identify a regional stack and must be supplied together; one without the
// other would silently fall back to the global stack while still setting the region on the context.
if (string.IsNullOrWhiteSpace(region) != string.IsNullOrWhiteSpace(regionShort))
{
    throw new ArgumentException(
        "Provide --region and --region-short together (both for a regional stack, neither for the global stack).");
}

var context = new DeployedEnvironmentOptions
{
    System = Required(options, "system"),
    Environment = Required(options, "environment"),
    Cloud = Required(options, "cloud"),
    Scope = scope,
    Region = region,
    RegionShortName = regionShort,
};

var json = new JsonObject
{
    ["scope"] = string.IsNullOrWhiteSpace(regionShort) ? "global" : "regional",

    // Cloud-singletons — identical in every region, so they are emitted for both stacks. The global
    // stack creates them; each regional stack references them (e.g. the SQL FQDN the app connects to).
    // The region-less resource group uses the escape hatch because the named ResourceGroupName()
    // accessor requires a region.
    ["sqlServerName"] = context.SqlServerName(),
    ["sqlServerFqdn"] = context.SqlServerFqdn(),
    ["sqlDatabaseName"] = context.Name(AzureResourceTypes.SqlDatabase),
    ["globalResourceGroup"] = context.Name(AzureResourceTypes.ResourceGroup),
};

// Regional resources — only when a region is supplied. Each carries the region token (and, for
// globally DNS-scoped types like Key Vault, a region-specific uniqueness suffix).
if (!string.IsNullOrWhiteSpace(regionShort))
{
    json["region"] = region;
    json["regionShortName"] = regionShort;
    json["resourceGroup"] = context.ResourceGroupName();
    json["appServiceName"] = context.AppServiceName();
    json["managedIdentityName"] = context.ManagedIdentityName();
    json["keyVaultName"] = context.KeyVaultName();
    json["keyVaultUri"] = context.KeyVaultUri().ToString();
    json["logAnalyticsName"] = context.LogAnalyticsName();
}

var serialized = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

if (options.TryGetValue("out", out var outPath) && !string.IsNullOrWhiteSpace(outPath))
{
    // Plain UTF-8 (no BOM) so `az deployment ... -p @file` and other JSON consumers parse cleanly.
    File.WriteAllText(outPath, serialized, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.Error.WriteLine($"Wrote {outPath}");
}

Console.WriteLine(serialized);

return 0;

static string Required(Dictionary<string, string> options, string key) =>
    options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required argument --{key}.");

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--", StringComparison.Ordinal))
            continue;

        var key = argv[i][2..];
        var value = i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? argv[++i]
            : "true";
        result[key] = value;
    }

    return result;
}
