namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The deployed-environment options for a microservice, bound from the <see cref="SectionName"/> configuration
/// section. A single source for environment-derived values such as the SLI location id (cloud moniker + region).
/// </summary>
public sealed class EnvironmentOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "DeployedEnvironment";

    /// <summary>Service short code (e.g. <c>prj</c>).</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Environment / lifecycle as a short word (e.g. <c>prod</c>).</summary>
    public string Environment { get; set; } = "local";

    /// <summary>Full Azure region name (e.g. <c>westus3</c>), used for the SLI location id and display.</summary>
    public string Region { get; set; } = "local";

    /// <summary>Short region code (e.g. <c>usw3</c>) used as the region token in regional resource names.</summary>
    public string RegionShortName { get; set; } = "local";

    /// <summary>Azure cloud — a <see cref="CloudType"/> value.</summary>
    public string Cloud { get; set; } = CloudType.AzureCloud;
}

/// <summary>String identifiers for the supported Azure cloud environments.</summary>
public static class CloudType
{
    /// <summary>Azure public (commercial) cloud.</summary>
    public const string AzureCloud = nameof(AzureCloud);

    /// <summary>Azure US Government cloud.</summary>
    public const string AzureUSGovernment = nameof(AzureUSGovernment);

    /// <summary>Azure China (21Vianet) cloud.</summary>
    public const string AzureChinaCloud = nameof(AzureChinaCloud);

    /// <summary>Azure Germany cloud (retired by Azure).</summary>
    public const string AzureGermanCloud = nameof(AzureGermanCloud);
}

/// <summary>Accessors that derive environment-specific values from <see cref="EnvironmentOptions"/>.</summary>
public static class EnvironmentOptionsExtensions
{
    /// <summary>The cloud segment of an SLI ms-loc location id (e.g. <c>public</c>) for the configured cloud.</summary>
    public static string GetLocationCloud(this EnvironmentOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Cloud switch
        {
            CloudType.AzureCloud => "public",
            CloudType.AzureUSGovernment => "usgov",
            CloudType.AzureChinaCloud => "china",
            CloudType.AzureGermanCloud => "germany",
            _ => throw new NotSupportedException($"Cloud type '{settings.Cloud}' has no SLI location moniker."),
        };
    }
}
