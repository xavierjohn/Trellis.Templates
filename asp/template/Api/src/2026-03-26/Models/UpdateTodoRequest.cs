namespace TodoSample.Api.v2026_03_26.Models;

using System.Text.Json.Serialization;
using TodoSample.Domain;

/// <summary>
/// Request model for updating a todo item.
/// </summary>
public record UpdateTodoRequest
{
    /// <summary>Updated title (1–200 characters).</summary>
    public Title Title { get; init; } = null!;

    /// <summary>Future due date with Z or an explicit timezone offset, normalized to UTC.</summary>
    [JsonConverter(typeof(DueDateJsonConverter))]
    public DueDate DueDate { get; init; } = null!;

    /// <summary>Updated optional categorization tag.</summary>
    public Maybe<Tag> Tag { get; init; }
}
