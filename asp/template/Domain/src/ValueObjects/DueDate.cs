namespace TodoSample.Domain;

/// <summary>
/// UTC due date for a todo item.
/// </summary>
[NotDefault]
public partial class DueDate : RequiredDateTime<DueDate>
{
    static partial void ValidateAdditional(DateTime value, string fieldName, ref string? errorMessage)
    {
        if (value.Kind != DateTimeKind.Utc)
            errorMessage = "Due date must be UTC. Supply Z or an explicit timezone offset in API requests.";
    }
}
