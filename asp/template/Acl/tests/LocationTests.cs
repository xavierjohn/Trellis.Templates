namespace AntiCorruptionLayer.Tests;

using TodoSample.AntiCorruptionLayer;
using Xunit;

public class LocationTests
{
    [Theory]
    [InlineData(CloudType.AzureCloud, "public")]
    [InlineData(CloudType.AzureUSGovernment, "usgov")]
    [InlineData(CloudType.AzureChinaCloud, "china")]
    [InlineData(CloudType.AzureGermanCloud, "germany")]
    public void Will_get_location_cloud_for_cloud_type(string cloudType, string expected)
    {
        EnvironmentOptions environmentOptions = new() { Cloud = cloudType };

        var actual = environmentOptions.GetLocationCloud();

        actual.Should().Be(expected);
    }
}
