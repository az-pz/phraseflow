using System.Globalization;
using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.Core.Tests;

internal static class TestHost
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Wednesday, 7 October 2026, 13:45:30.</summary>
    public static readonly DateTime Now = new(2026, 10, 7, 13, 45, 30, DateTimeKind.Local);

    public static DelegateExpansionHost Create(
        string? clipboard = "clip text",
        Func<string, SnippetReference?>? snippets = null,
        Func<string, ScriptKind, string>? scripts = null)
    {
        return new DelegateExpansionHost
        {
            Clock = () => Now,
            Clipboard = () => clipboard,
            SnippetLookup = snippets ?? (_ => null),
            ScriptRunner = scripts ?? ((_, _) => throw new InvalidOperationException("Scripts are disabled.")),
            UserName = "jdoe",
            FullName = "Jane Doe",
            MachineName = "WORKSTATION",
            LocalIp = () => "10.0.0.5",
        };
    }

    public static ExpansionOutput Expand(
        string content,
        IExpansionHost? host = null,
        IReadOnlyDictionary<string, string>? fields = null,
        EvaluationMode mode = EvaluationMode.Live) =>
        TemplateEvaluator.Expand(content, plainText: false, host ?? Create(), fields, mode, Culture);

    public static string Text(string content, IExpansionHost? host = null, IReadOnlyDictionary<string, string>? fields = null) =>
        Expand(content, host, fields).Text;
}
