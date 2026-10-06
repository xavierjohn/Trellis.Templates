namespace Domain.Tests;

using TodoSample.Domain;

public class DueDateTests
{
    [Fact]
    public void TryCreate_valid_date_succeeds()
    {
        var date = DateTime.UtcNow.AddDays(7);
        var result = DueDate.TryCreate(date);

        result.Should().BeSuccess()
            .Which.Value.Should().Be(date);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void TryCreate_min_value_fails(DateTimeKind kind)
    {
        var result = DueDate.TryCreate(DateTime.SpecifyKind(DateTime.MinValue, kind));

        result.Should().BeFailure()
            .Which.Should().BeOfType<Error.InvalidInput>();
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void TryCreate_non_utc_date_fails(DateTimeKind kind)
    {
        var date = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7), kind);

        var result = DueDate.TryCreate(date);

        result.Should().BeFailureOfType<Error.InvalidInput>();
    }
}
