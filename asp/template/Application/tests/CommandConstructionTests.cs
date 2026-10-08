namespace Application.Tests;

using Microsoft.Extensions.Time.Testing;
using TodoSample.Application.Todos;
using TodoSample.Domain;
using Trellis.Testing;

public class CommandConstructionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Create_reports_all_missing_fields_or_preserves_valid_values(bool missingTitle, bool missingDueDate)
    {
        var title = missingTitle ? null : Title.Create("Test");
        var dueDate = missingDueDate ? null : DueDate.Create(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var tag = Tag.Create("work");
        var expected = new List<FieldViolation>();
        if (missingTitle)
            expected.Add(new FieldViolation(InputPointer.ForProperty("title"), ValidationCodes.ValueNotNull, Detail: "Title is required."));
        if (missingDueDate)
            expected.Add(new FieldViolation(InputPointer.ForProperty("dueDate"), ValidationCodes.ValueNotNull, Detail: "Due date is required."));

        var result = CreateTodoCommand.TryCreate(title, dueDate, Maybe.From(tag));

        if (expected.Count > 0)
        {
            result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().Equal(expected);
            return;
        }

        var command = result.Should().BeSuccess().Which;
        command.Title.Should().BeSameAs(title);
        command.DueDate.Should().BeSameAs(dueDate);
        command.Tag.Should().HaveValueEqualTo(tag);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Update_reports_all_missing_fields_or_preserves_valid_values(bool missingId, bool missingTitle, bool missingDueDate)
    {
        var id = missingId ? null : TodoId.NewUniqueV7();
        var title = missingTitle ? null : Title.Create("Test");
        var dueDate = missingDueDate ? null : DueDate.Create(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var tag = Tag.Create("work");
        EntityTagValue[] etags = [EntityTagValue.Strong("v1")];
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero));
        var expected = new List<FieldViolation>();
        if (missingId)
            expected.Add(new FieldViolation(InputPointer.ForProperty("id"), ValidationCodes.ValueNotNull, Detail: "Todo id is required."));
        if (missingTitle)
            expected.Add(new FieldViolation(InputPointer.ForProperty("title"), ValidationCodes.ValueNotNull, Detail: "Title is required."));
        if (missingDueDate)
            expected.Add(new FieldViolation(InputPointer.ForProperty("dueDate"), ValidationCodes.ValueNotNull, Detail: "Due date is required."));

        var result = UpdateTodoCommand.TryCreate(id, title, dueDate, Maybe.From(tag), etags, timeProvider);

        if (expected.Count > 0)
        {
            result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().Equal(expected);
            return;
        }

        var command = result.Should().BeSuccess().Which;
        command.TodoId.Should().BeSameAs(id);
        command.Title.Should().BeSameAs(title);
        command.DueDate.Should().BeSameAs(dueDate);
        command.Tag.Should().HaveValueEqualTo(tag);
        command.IfMatchETags.Should().BeSameAs(etags);
    }

    [Fact]
    public void Update_skips_the_due_date_rule_when_a_required_field_is_missing()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero));
        var pastDueDate = DueDate.Create(new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc));

        var result = UpdateTodoCommand.TryCreate(TodoId.NewUniqueV7(), null, pastDueDate, Maybe<Tag>.None, timeProvider: timeProvider);

        result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().Equal(
            new FieldViolation(InputPointer.ForProperty("title"), ValidationCodes.ValueNotNull, Detail: "Title is required."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complete_validates_and_preserves_the_id(bool missingId)
    {
        var id = missingId ? null : TodoId.NewUniqueV7();

        var result = CompleteTodoCommand.TryCreate(id);

        if (missingId)
        {
            result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().Equal(
                new FieldViolation(InputPointer.ForProperty("id"), ValidationCodes.ValueNotNull, Detail: "Todo id is required."));
            return;
        }

        var command = result.Should().BeSuccess().Which;
        command.TodoId.Should().BeSameAs(id);
        command.IfMatchETags.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complete_preserves_the_supplied_precondition(bool empty)
    {
        var id = TodoId.NewUniqueV7();
        EntityTagValue[] etags = empty ? [] : [EntityTagValue.Strong("v1")];

        var command = CompleteTodoCommand.TryCreate(id, etags).Should().BeSuccess().Which;

        command.TodoId.Should().BeSameAs(id);
        command.IfMatchETags.Should().BeSameAs(etags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_validates_the_id_and_preserves_the_precondition(bool missingId)
    {
        var id = missingId ? null : TodoId.NewUniqueV7();
        EntityTagValue[] etags = [EntityTagValue.Strong("v1")];

        var result = DeleteTodoCommand.TryCreate(id, etags);

        if (missingId)
        {
            result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().Equal(
                new FieldViolation(InputPointer.ForProperty("id"), ValidationCodes.ValueNotNull, Detail: "Todo id is required."));
            return;
        }

        var command = result.Should().BeSuccess().Which;
        command.TodoId.Should().BeSameAs(id);
        command.IfMatchETags.Should().BeSameAs(etags);
    }
}
