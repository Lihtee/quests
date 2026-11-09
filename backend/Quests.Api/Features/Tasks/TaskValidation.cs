namespace Quests.Api.Features.Tasks;

/// <summary>Boundary validation for task field values, so the model's invariant checks never throw for user input.</summary>
public static class TaskValidation
{
    /// <summary>Returns the names of listed fields whose values are invalid; empty means valid.</summary>
    public static IReadOnlyList<string> InvalidFields(TaskFields fields)
    {
        var invalid = new List<string>();

        if (fields.Has(TaskFields.TitleField) && !IsValidTitle(fields.Title))
            invalid.Add(TaskFields.TitleField);

        if (fields.Has(TaskFields.NotesField) && fields.Notes is { Length: > TaskItem.NotesMaxLength })
            invalid.Add(TaskFields.NotesField);

        return invalid;
    }

    private static bool IsValidTitle(string? title)
    {
        var trimmed = title?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= TaskItem.TitleMaxLength;
    }
}
