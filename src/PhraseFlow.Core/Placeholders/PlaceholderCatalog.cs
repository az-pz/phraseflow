namespace PhraseFlow.Core.Placeholders;

/// <summary>Describes a placeholder for documentation, the editor's insert menu and the parser.</summary>
public sealed record PlaceholderInfo(
    string Name,
    string Category,
    string Syntax,
    string Description,
    string Example,
    int MinArgs = 0,
    IReadOnlyList<string>? Aliases = null,
    string? InsertText = null)
{
    /// <summary>Text inserted by the editor's "Insert placeholder" menu.</summary>
    public string Template => InsertText ?? Syntax.Split("  ", StringSplitOptions.TrimEntries)[0];
}

/// <summary>Canonical placeholder names.</summary>
public static class Ph
{
    public const string Date = "date";
    public const string Time = "time";
    public const string DateTime = "datetime";
    public const string Utc = "utc";
    public const string Week = "week";
    public const string Quarter = "quarter";
    public const string Unix = "unix";
    public const string Ordinal = "ordinal";
    public const string Clipboard = "clipboard";
    public const string Cursor = "cursor";
    public const string Input = "input";
    public const string Paragraph = "paragraph";
    public const string Choice = "choice";
    public const string Checkbox = "checkbox";
    public const string PickDate = "pickdate";
    public const string Snippet = "snippet";
    public const string Upper = "upper";
    public const string Lower = "lower";
    public const string Title = "title";
    public const string Trim = "trim";
    public const string Slug = "slug";
    public const string UrlEncode = "urlencode";
    public const string Replace = "replace";
    public const string Repeat = "repeat";
    public const string User = "user";
    public const string FullName = "fullname";
    public const string Computer = "computer";
    public const string Env = "env";
    public const string Ip = "ip";
    public const string Guid = "guid";
    public const string Random = "random";
    public const string Counter = "counter";
    public const string Calc = "calc";
    public const string Key = "key";
    public const string Enter = "enter";
    public const string Tab = "tab";
    public const string Delay = "delay";
    public const string Shell = "shell";
    public const string PowerShell = "powershell";
}

public static class PlaceholderCatalog
{
    public const string DateTimeCategory = "Date & time";
    public const string ClipboardCategory = "Clipboard & cursor";
    public const string FieldsCategory = "Fill-in fields";
    public const string TextCategory = "Text";
    public const string SystemCategory = "System";
    public const string GeneratorCategory = "Generators";
    public const string KeysCategory = "Keys & timing";
    public const string ScriptCategory = "Scripts";

    public static IReadOnlyList<PlaceholderInfo> All { get; } =
    [
        new(Ph.Date, DateTimeCategory, "{date}  {date:format}  {date:format|shift}",
            "Current date. Format uses .NET codes: yyyy, MM, dd, ddd, dddd, MMM, MMMM (iso = yyyy-MM-dd). " +
            "Shift with +1d, -2w, +1M, +1y, +3bd (business days) or anchors som/eom, sow/eow, soy/eoy.",
            "{date:dddd, MMMM d, yyyy}", InsertText: "{date:yyyy-MM-dd}"),
        new(Ph.Time, DateTimeCategory, "{time}  {time:format}  {time:format|shift}",
            "Current time. Format codes: HH (24h), hh (12h), mm, ss, tt (AM/PM). Shift with +2h, -30m, +15s.",
            "{time:HH:mm}", InsertText: "{time:HH:mm}"),
        new(Ph.DateTime, DateTimeCategory, "{datetime}  {datetime:format|shift}",
            "Current date and time (default: your short date and time format).",
            "{datetime:yyyy-MM-dd HH:mm}", Aliases: ["now"], InsertText: "{datetime:yyyy-MM-dd HH:mm}"),
        new(Ph.Utc, DateTimeCategory, "{utc}  {utc:format|shift}",
            "Current UTC date and time (default: ISO 8601, e.g. 2026-01-31T14:05:00Z).",
            "{utc}"),
        new(Ph.Week, DateTimeCategory, "{week}  {week:shift}",
            "ISO 8601 week number.", "Week {week}"),
        new(Ph.Quarter, DateTimeCategory, "{quarter}  {quarter:shift}",
            "Calendar quarter (1–4).", "Q{quarter} {date:yyyy}"),
        new(Ph.Unix, DateTimeCategory, "{unix}",
            "Unix timestamp (seconds since 1970-01-01 UTC).", "{unix}"),
        new(Ph.Ordinal, DateTimeCategory, "{ordinal:number}",
            "Number with an English ordinal suffix (1st, 2nd, 3rd, 4th…).",
            "{date:MMMM} {ordinal:{date:%d}}", MinArgs: 1, InsertText: "{ordinal:{date:%d}}"),

        new(Ph.Clipboard, ClipboardCategory, "{clipboard}",
            "Text currently on the clipboard.", "“{clipboard}”"),
        new(Ph.Cursor, ClipboardCategory, "{cursor}",
            "Puts the text cursor here after the expansion.", "<b>{cursor}</b>"),

        new(Ph.Input, FieldsCategory, "{input:Label}  {input:Label|Default}",
            "Asks for a line of text before expanding. Fields with the same label share one value.",
            "Hi {input:Name|there},", MinArgs: 1, InsertText: "{input:Name}"),
        new(Ph.Paragraph, FieldsCategory, "{paragraph:Label}  {paragraph:Label|Default}",
            "Asks for multiple lines of text.", "{paragraph:Message}", MinArgs: 1, InsertText: "{paragraph:Message}"),
        new(Ph.Choice, FieldsCategory, "{choice:Label|Option 1|Option 2|…}",
            "Lets you pick one option from a drop-down list.",
            "Priority: {choice:Priority|High|Medium|Low}", MinArgs: 2, InsertText: "{choice:Pick one|Option A|Option B}"),
        new(Ph.Checkbox, FieldsCategory, "{checkbox:Label|Text if ticked|Text if not ticked}",
            "Includes optional text when the box is ticked.",
            "Thanks!{checkbox:Add P.S.| P.S. Talk soon.}", MinArgs: 2, InsertText: "{checkbox:Include extra| extra text}"),
        new(Ph.PickDate, FieldsCategory, "{pickdate:Label}  {pickdate:Label|format}",
            "Shows a date picker and inserts the chosen date.",
            "Due {pickdate:Due date|MMMM d}", MinArgs: 1, Aliases: ["datepicker"], InsertText: "{pickdate:Date|yyyy-MM-dd}"),

        new(Ph.Snippet, TextCategory, "{snippet:keyword}",
            "Inserts the content of another snippet (nested snippets).", "{snippet:;br}", MinArgs: 1, InsertText: "{snippet:keyword}"),
        new(Ph.Upper, TextCategory, "{upper:text}", "UPPER-CASES the text.", "{upper:make it loud}", MinArgs: 1, InsertText: "{upper:{clipboard}}"),
        new(Ph.Lower, TextCategory, "{lower:text}", "lower-cases the text.", "{lower:QUIET PLEASE}", MinArgs: 1, InsertText: "{lower:{clipboard}}"),
        new(Ph.Title, TextCategory, "{title:text}", "Converts To Title Case.", "{title:the quick brown fox}", MinArgs: 1, InsertText: "{title:{clipboard}}"),
        new(Ph.Trim, TextCategory, "{trim:text}", "Removes leading and trailing whitespace.", "[{trim:   padded   }]", MinArgs: 1, InsertText: "{trim:{clipboard}}"),
        new(Ph.Slug, TextCategory, "{slug:text}", "Converts text to a lower-case-hyphenated slug.", "{slug:Fix Login Bug #42!}", MinArgs: 1, InsertText: "{slug:{clipboard}}"),
        new(Ph.UrlEncode, TextCategory, "{urlencode:text}", "URL-encodes the text.", "https://www.bing.com/search?q={urlencode:text expander}", MinArgs: 1, InsertText: "{urlencode:{clipboard}}"),
        new(Ph.Replace, TextCategory, "{replace:text|find|replacement}",
            "Replaces every occurrence of find (\\n and \\t allowed) with replacement.",
            "{replace:2026-01-31|-|/}", MinArgs: 2, InsertText: "{replace:{clipboard}|\\n| }"),
        new(Ph.Repeat, TextCategory, "{repeat:text|count}", "Repeats the text (max 1000 times).", "{repeat:=|24}", MinArgs: 2, InsertText: "{repeat:-|20}"),

        new(Ph.User, SystemCategory, "{user}", "Windows user name.", "{user}", Aliases: ["username"]),
        new(Ph.FullName, SystemCategory, "{fullname}", "Display name of the signed-in user (falls back to the user name).", "{fullname}"),
        new(Ph.Computer, SystemCategory, "{computer}", "Computer name.", "{computer}", Aliases: ["machine", "hostname"]),
        new(Ph.Env, SystemCategory, "{env:VARIABLE}", "Value of an environment variable.", "{env:USERPROFILE}", MinArgs: 1, InsertText: "{env:USERPROFILE}"),
        new(Ph.Ip, SystemCategory, "{ip}", "Local IPv4 address.", "{ip}"),

        new(Ph.Guid, GeneratorCategory, "{guid}  {guid:N}  {guid:upper}",
            "New GUID/UUID (formats: D default, N digits only, B braces, P parentheses, upper).", "{guid}", Aliases: ["uuid"]),
        new(Ph.Random, GeneratorCategory, "{random:min|max}  {random:a|b|c}",
            "Random whole number in a range, or a random pick from a list.", "{random:1|6}", MinArgs: 1, InsertText: "{random:1|100}"),
        new(Ph.Counter, GeneratorCategory, "{counter:name}",
            "Persistent counter that increases by one on every expansion.", "Ticket #{counter:ticket}", MinArgs: 1, InsertText: "{counter:name}"),
        new(Ph.Calc, GeneratorCategory, "{calc:expression}  {calc:expression|format}",
            "Arithmetic: + - * / % ^, parentheses, round(x,n), floor, ceil, abs, sqrt, min, max. Optional .NET number format (e.g. 0.00, N2, C).",
            "{calc:3*19.99*1.08|0.00}", MinArgs: 1, InsertText: "{calc:1+1}"),

        new(Ph.Key, KeysCategory, "{key:Name}  {key:Name|count}  {key:Ctrl+S}",
            "Presses a key or shortcut: Enter, Tab, Esc, Space, Backspace, Delete, Up, Down, Left, Right, Home, End, PgUp, PgDn, F1–F24, with Ctrl/Alt/Shift/Win.",
            "Name{key:Tab}Email", MinArgs: 1, InsertText: "{key:Tab}"),
        new(Ph.Enter, KeysCategory, "{enter}", "Presses Enter.", "Line one{enter}Line two"),
        new(Ph.Tab, KeysCategory, "{tab}", "Presses Tab (e.g. to move to the next form field).", "user{tab}password"),
        new(Ph.Delay, KeysCategory, "{delay:milliseconds}", "Pauses before continuing (max 10 seconds).", "{delay:250}", MinArgs: 1, InsertText: "{delay:500}"),

        new(Ph.Shell, ScriptCategory, "{shell:command}",
            "Runs a Command Prompt command and inserts its output. Must be enabled in Settings.", "{shell:ver}", MinArgs: 1, Aliases: ["cmd"], InsertText: "{shell:echo %DATE%}"),
        new(Ph.PowerShell, ScriptCategory, "{powershell:command}",
            "Runs a PowerShell command and inserts its output. Must be enabled in Settings.", "{powershell:Get-Date -Format o}", MinArgs: 1, Aliases: ["ps"], InsertText: "{powershell:Get-Date}"),
    ];

    private static readonly Dictionary<string, PlaceholderInfo> Lookup = BuildLookup();

    public static IReadOnlyList<string> Categories { get; } = All.Select(p => p.Category).Distinct().ToList();

    private static Dictionary<string, PlaceholderInfo> BuildLookup()
    {
        var lookup = new Dictionary<string, PlaceholderInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in All)
        {
            lookup[info.Name] = info;
            foreach (var alias in info.Aliases ?? [])
            {
                lookup[alias] = info;
            }
        }

        return lookup;
    }

    public static bool TryResolve(string name, out PlaceholderInfo info) => Lookup.TryGetValue(name, out info!);

    public static bool IsFieldPlaceholder(string canonicalName) =>
        canonicalName is Ph.Input or Ph.Paragraph or Ph.Choice or Ph.Checkbox or Ph.PickDate;
}
