namespace TodoSample.Api.v2026_03_26.Models;

using System.Text.Json.Serialization;
using TodoSample.Domain;

/// <summary>
/// Request model for creating a todo item.
/// </summary>
public record CreateTodoRequest
{
    /// <summary>Title of the todo (1–200 characters).</summary>
    public Title Title { get; init; } = null!;

    /// <summary>Due date with Z or an explicit timezone offset, normalized to UTC.</summary>
    [JsonConverter(typeof(DueDateJsonConverter))]
    public DueDate DueDate { get; init; } = null!;

    /// <summary>Optional categorization tag (lowercase alphanumeric + hyphens).</summary>
    public Maybe<Tag> Tag { get; init; }
}
