namespace AntiCorruptionLayer.Tests;

using Trellis.ResourceNaming.Azure;
using Xunit;

// The Trellis.ResourceNaming.Azure package produces resource names and endpoints from a single
// DeployedEnvironmentOptions — replacing the template's former hand-rolled EnvironmentOptions.
public class ResourceNamingLibraryTests
{
    private static readonly DeployedEnvironmentOptions Env = new()
    {
        System = "tdo",
        Environment = "prod",
        Region = "westus3",
        RegionShortName = "usw3",
        Cloud = KnownClouds.AzureCloud,
    };

    [Fact]
    public void Storage_name_follows_the_convention() =>
        Env.StorageName().Should().Be("tdostprod");

    [Fact]
    public void Blob_url_resolves_for_the_cloud() =>
        Env.BlobUrl().AbsoluteUri.Should().Be("https://tdostprod.blob.core.windows.net/");

    [Fact]
    public void KeyVault_uri_is_regional() =>
        Env.KeyVaultUri().AbsoluteUri.Should().Be("https://tdo-kv-prod-usw3.vault.azure.net/");

    [Fact]
    public void ServiceBus_namespace_is_the_connect_alias() =>
        Env.ServiceBusNamespace().Should().Be("tdo-sbns-prod.servicebus.windows.net");

    [Fact]
    public void Cosmos_url_resolves() =>
        Env.CosmosUrl().AbsoluteUri.Should().Be("https://tdo-cosmos-prod.documents.azure.com/");

    [Fact]
    public void Cloud_drives_the_endpoint_suffix()
    {
        var usGov = new DeployedEnvironmentOptions
        {
            System = "tdo",
            Environment = "prod",
            Cloud = KnownClouds.AzureUSGovernment,
        };

        usGov.BlobUrl().AbsoluteUri.Should().Be("https://tdostprod.blob.core.usgovcloudapi.net/");
    }
}
