using PhraseFlow.Core.Models;

namespace PhraseFlow.Core.Matching;

/// <summary>Decides whether snippets apply to a given process.</summary>
public sealed class AppFilter
{
    private readonly HashSet<string> _apps;

    public AppFilter(AppFilterMode mode, IEnumerable<string> apps)
    {
        Mode = mode;
        _apps = new HashSet<string>(apps.Select(Normalize).Where(a => a.Length > 0), StringComparer.OrdinalIgnoreCase);
    }

    public static AppFilter All { get; } = new(AppFilterMode.AllApps, []);

    public AppFilterMode Mode { get; }

    public IReadOnlyCollection<string> Apps => _apps;

    public static AppFilter FromText(AppFilterMode mode, string? apps) => new(mode, ParseList(apps));

    public static IEnumerable<string> ParseList(string? text) =>
        (text ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Lower-cases and removes the <c>.exe</c> extension and any directory.</summary>
    public static string Normalize(string? processName)
    {
        var name = (processName ?? "").Trim().Trim('"');
        var slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.ToLowerInvariant();
    }

    public bool Contains(string? processName) => _apps.Contains(Normalize(processName));

    public bool Allows(string? processName) => Mode switch
    {
        AppFilterMode.OnlyListed => processName is not null && Contains(processName),
        AppFilterMode.AllExceptListed => processName is null || !Contains(processName),
        _ => true,
    };
}
