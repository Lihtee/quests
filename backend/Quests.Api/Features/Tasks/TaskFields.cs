namespace Quests.Api.Features.Tasks;

/// <summary>
/// A partial set of task field values. Only the fields named in <see cref="Changed"/> are applied;
/// the other values are ignored.
/// </summary>
public sealed record TaskFields(
    IReadOnlySet<string> Changed,
    string? Title = null,
    string? Notes = null,
    DateTimeOffset? DueAt = null,
    DateTimeOffset? CompletedAt = null)
{
    public const string TitleField = "title";
    public const string NotesField = "notes";
    public const string DueAtField = "dueAt";
    public const string CompletedAtField = "completedAt";

    /// <summary>Field names a change may list.</summary>
    public static readonly IReadOnlySet<string> Known =
        new HashSet<string>(StringComparer.Ordinal) { TitleField, NotesField, DueAtField, CompletedAtField };

    public bool Has(string field) => Changed.Contains(field);
}
