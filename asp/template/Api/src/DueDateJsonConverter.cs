namespace TodoSample.Api;

using System.Text.Json;
using System.Text.Json.Serialization;
using TodoSample.Domain;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Validation;

internal sealed class DueDateJsonConverter : JsonConverter<DueDate>
{
    private readonly ValidatingJsonConverter<DueDate, DateTime> _standard = new();

    public override bool HandleNull => true;

    public override DueDate? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !reader.TryGetDateTime(out var date) ||
            date.Kind == DateTimeKind.Unspecified ||
            !reader.TryGetDateTimeOffset(out var offsetDate))
            return _standard.Read(ref reader, typeToConvert, options);

        var fieldName = ValidationErrorsContext.CurrentPropertyName ?? "dueDate";
        return DueDate.TryCreate(offsetDate.UtcDateTime, fieldName).Match<DueDate, DueDate?>(
            onSuccess: value => value,
            onFailure: error =>
            {
                if (error is Error.InvalidInput invalidInput)
                    ValidationErrorsContext.AddBodyError(invalidInput);
                else
                    ValidationErrorsContext.AddBodyError(fieldName, error.Detail ?? "Due date is invalid.");
                return null;
            });
    }

    public override void Write(Utf8JsonWriter writer, DueDate value, JsonSerializerOptions options) =>
        _standard.Write(writer, value, options);
}
