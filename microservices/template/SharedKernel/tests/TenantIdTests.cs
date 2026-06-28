using ProjectTrackerTemplate.SharedKernel;

namespace SharedKernel.Tests;

public class TenantIdTests
{
    [Fact]
    public void TryCreate_valid_succeeds()
    {
        var result = TenantId.TryCreate("acme");

        result.Should().BeSuccess().Which.Value.Should().Be("acme");
    }

    [Fact]
    public void TryCreate_blank_fails()
        => TenantId.TryCreate("").Should().BeFailure();

    [Fact]
    public void Equality_is_by_value()
    {
        var a = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");
        var b = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");

        a.Should().Be(b);
    }
}
