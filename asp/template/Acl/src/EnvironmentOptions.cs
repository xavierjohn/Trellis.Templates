namespace TodoSample.AntiCorruptionLayer;

public class EnvironmentOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "DeployedEnvironment";

    public string ServiceName { get; set; } = "TDO";

    public string Region { get; set; } = "local";

    public string RegionShortName { get; set; } = "local";

    public string Environment { get; set; } = EnvironmentType.Test;

    public string Cloud { get; set; } = CloudType.AzureCloud;
}
