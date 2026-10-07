namespace PhraseFlow.Core.Placeholders;

public enum ScriptKind
{
    Cmd,
    PowerShell,
}

public sealed record SnippetReference(string Keyword, string Content, bool PlainText);

/// <summary>Supplies environment-dependent values to the template evaluator.</summary>
public interface IExpansionHost
{
    DateTime Now { get; }

    string UserName { get; }

    string FullName { get; }

    string MachineName { get; }

    string? GetClipboardText();

    string? GetEnvironmentVariable(string name);

    string? GetLocalIpAddress();

    SnippetReference? FindSnippet(string keyword);

    /// <summary>Returns the next value of a named counter, incrementing it only when <paramref name="increment"/> is true.</summary>
    long GetCounter(string name, bool increment);

    /// <summary>Runs a command and returns its output; throws when scripts are disabled or the command fails.</summary>
    string RunScript(string command, ScriptKind kind);
}

/// <summary>A simple host with overridable delegates; used for previews and tests.</summary>
public class DelegateExpansionHost : IExpansionHost
{
    public Func<DateTime> Clock { get; init; } = () => DateTime.Now;

    public Func<string?> Clipboard { get; init; } = () => null;

    public Func<string, SnippetReference?> SnippetLookup { get; init; } = _ => null;

    public Func<string, ScriptKind, string> ScriptRunner { get; init; } =
        (_, _) => throw new InvalidOperationException("Scripts are disabled. Enable them in Settings → Advanced.");

    public Dictionary<string, long> Counters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime Now => Clock();

    public string UserName { get; init; } = Environment.UserName;

    public string FullName { get; init; } = Environment.UserName;

    public string MachineName { get; init; } = Environment.MachineName;

    public Func<string?> LocalIp { get; init; } = () => "192.168.1.10";

    public string? GetClipboardText() => Clipboard();

    public virtual string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? GetLocalIpAddress() => LocalIp();

    public SnippetReference? FindSnippet(string keyword) => SnippetLookup(keyword);

    public long GetCounter(string name, bool increment)
    {
        lock (Counters)
        {
            Counters.TryGetValue(name, out var current);
            var next = current + 1;
            if (increment)
            {
                Counters[name] = next;
            }

            return next;
        }
    }

    public string RunScript(string command, ScriptKind kind) => ScriptRunner(command, kind);
}
