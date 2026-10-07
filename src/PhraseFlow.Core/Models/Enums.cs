namespace PhraseFlow.Core.Models;

/// <summary>Determines when a snippet's keyword is replaced by its content.</summary>
public enum TriggerMode
{
    /// <summary>Expand after the keyword is followed by a trigger character (space, tab, enter, punctuation).</summary>
    Delimiter,

    /// <summary>Expand as soon as the last character of the keyword is typed.</summary>
    Immediate,

    /// <summary>Never expand automatically; the snippet can only be inserted from the search picker.</summary>
    PickerOnly,
}

/// <summary>How the typed keyword is compared and how its case affects the expansion.</summary>
public enum CaseMode
{
    /// <summary>Match ignoring case and mirror the typed case (btw → by the way, Btw → By the way, BTW → BY THE WAY).</summary>
    Adapt,

    /// <summary>The keyword must be typed exactly; content is inserted as-is.</summary>
    Sensitive,

    /// <summary>Match ignoring case; content is inserted as-is.</summary>
    Insensitive,
}

/// <summary>How expanded text is delivered to the target application.</summary>
public enum InsertMethod
{
    /// <summary>Use the global setting (snippets only).</summary>
    Default,

    /// <summary>Type short single-line text, paste long or multi-line text.</summary>
    Auto,

    /// <summary>Simulate typing of every character.</summary>
    Typing,

    /// <summary>Paste through the clipboard (the previous clipboard content is restored).</summary>
    Clipboard,
}

/// <summary>Restricts a group of snippets to (or away from) specific applications.</summary>
public enum AppFilterMode
{
    AllApps,
    OnlyListed,
    AllExceptListed,
}

public enum PasteShortcut
{
    CtrlV,
    ShiftInsert,
}

public enum ThemePreference
{
    System,
    Light,
    Dark,
}
