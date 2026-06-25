namespace TodoSample.AntiCorruptionLayer;

public static class EnvironmentOptionsExt
{
    // Region specific resources.
    // Example: App Service, Key Vault, Managed Identity. etc.
    public static string GetRegionalResourceName(this EnvironmentOptions settings, string resourceType) =>
        $"{settings.Environment}-{settings.ServiceName}-{settings.RegionShortName}-{resourceType}".ToLowerInvariant();

    // Shared resources.
    // Example: Storage Account, Cosmos DB, SQL etc.
    public static string GetSharedResourceName(this EnvironmentOptions settings, string resourceType) =>
        $"{settings.Environment}-{settings.ServiceName}-{resourceType}".ToLowerInvariant();

    // The cloud segment of an SLI ms-loc location id (e.g. "public") for the configured CloudType.
    public static string GetLocationCloud(this EnvironmentOptions settings) => settings.Cloud switch
    {
        CloudType.AzureCloud => "public",
        CloudType.AzureUSGovernment => "usgov",
        CloudType.AzureChinaCloud => "china",
        CloudType.AzureGermanCloud => "germany",
        _ => throw new NotSupportedException($"Cloud type '{settings.Cloud}' has no SLI location moniker."),
    };
}
