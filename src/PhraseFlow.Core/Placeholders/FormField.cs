namespace PhraseFlow.Core.Placeholders;

public enum FieldKind
{
    Text,
    Paragraph,
    Choice,
    Checkbox,
    Date,
}

/// <summary>A fill-in field that must be answered before a snippet is expanded.</summary>
public sealed record FormField(
    string Key,
    FieldKind Kind,
    string Label,
    string DefaultValue,
    IReadOnlyList<string> Options)
{
    public static string MakeKey(FieldKind kind, string label) => $"{kind}:{label.Trim().ToLowerInvariant()}";

    /// <summary>Default values keyed by <see cref="Key"/>, used for previews.</summary>
    public static Dictionary<string, string> Defaults(IEnumerable<FormField> fields) =>
        fields.GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.First().DefaultValue);

    /// <summary>Like <see cref="Defaults"/>, but empty text fields show their label, e.g. "[Name]".</summary>
    public static Dictionary<string, string> PreviewValues(IEnumerable<FormField> fields) =>
        fields.GroupBy(f => f.Key).ToDictionary(
            g => g.Key,
            g => g.First() is { Kind: FieldKind.Text or FieldKind.Paragraph, DefaultValue.Length: 0 } field
                ? $"[{field.Label}]"
                : g.First().DefaultValue);
}
