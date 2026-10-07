using System.Collections.Concurrent;
using System.Globalization;
using PhraseFlow.App.Native;
using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Placeholders;
using static PhraseFlow.App.Native.NativeMethods;

namespace PhraseFlow.App.Services;

/// <summary>UI operations the expansion pipeline needs (implemented by the application on the UI thread).</summary>
internal interface IExpansionUi
{
    /// <summary>Shows the fill-in form; returns field values or null when cancelled.</summary>
    IReadOnlyDictionary<string, string>? ShowForm(SnippetEntry entry, IReadOnlyList<FormField> fields, nint targetWindow);

    void OnExpanded(SnippetEntry entry, string typed, ExpansionOutput output);

    void OnError(string message);
}

/// <summary>
/// Evaluates snippets and types or pastes them into the target application on a dedicated worker thread.
/// While it injects, the keyboard hook holds the user's keystrokes; they are replayed after <see cref="KeyboardHook.EndExpansion"/>.
/// </summary>
internal sealed class ExpansionService : IDisposable
{
    private const int MaxUndoLength = 2000;

    private readonly BlockingCollection<Action> _jobs = new();
    private readonly Thread _worker;
    private readonly KeyboardHook _hook;
    private readonly Func<AppSettings> _settings;
    private readonly IExpansionHost _host;
    private readonly IExpansionUi _ui;

    public ExpansionService(KeyboardHook hook, Func<AppSettings> settings, IExpansionHost host, IExpansionUi ui)
    {
        _hook = hook;
        _settings = settings;
        _host = host;
        _ui = ui;
        _worker = new Thread(Work) { IsBackground = true, Name = "PhraseFlow expansion" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
        _hook.MatchFound += match => _jobs.Add(() => ExpandTyped(match));
        _hook.UndoRequested += (undo, hold) => _jobs.Add(() => Undo(undo, hold));
    }

    /// <summary>Inserts a snippet chosen in the picker into <paramref name="targetWindow"/> (or copies it).</summary>
    public void InsertFromPicker(SnippetEntry entry, nint targetWindow, bool copyToClipboard) =>
        _jobs.Add(() => ExpandPicked(entry, targetWindow, copyToClipboard));

    public void Dispose()
    {
        _jobs.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
    }

    private void Work()
    {
        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try
            {
                job();
            }
            catch (Exception ex)
            {
                Log.Error("Expansion job failed", ex);
            }
        }
    }

    private void ExpandTyped(HookMatch hookMatch)
    {
        var match = hookMatch.Match;
        var entry = match.Entry;
        var settings = _settings();
        var hold = hookMatch.HoldGeneration;
        var started = false;
        try
        {
            var fields = TemplateEvaluator.GetFields(entry.Content, entry.PlainText, _host);
            if (fields.Count > 0)
            {
                started = true;
                ExpandWithForm(hookMatch, fields, settings);
                return;
            }

            var output = BuildOutput(entry, match, null);
            if (!StillValid(hookMatch))
            {
                return;
            }

            started = true;
            var completed = Insert(output, entry, settings, match.CharactersToDelete, hookMatch.Window, Guard(hookMatch.ContextGeneration, hold));
            var context = match.IsDelimiterTrigger && !entry.OmitDelimiter ? match.TriggerChar.ToString() : "";
            UndoInfo? undo = null;
            if (completed && settings.BackspaceUndo && output.IsSimpleText && output.Text.Length <= MaxUndoLength &&
                output.Text.IndexOfAny(['\n', '\r', '\t']) < 0)
            {
                undo = new UndoInfo(hookMatch.Window, ExpansionOutput.CaretLength(output.Text), match.OriginalText, hookMatch.ContextGeneration);
            }

            _hook.EndExpansion(hold, completed ? context : "", undo);
            if (completed)
            {
                _ui.OnExpanded(entry, match.TypedKeyword, output);
            }

        }
        catch (Exception ex)
        {
            Log.Error($"Expanding '{entry.Keyword}' failed", ex);
            if (started)
            {
                _hook.EndExpansion(hold, "");
            }
            else
            {
                // Nothing was erased yet: deliver the blocked trigger key and the keys typed meanwhile.
                _hook.CancelExpansion(hold);
            }

            _ui.OnError($"Could not expand {entry.Keyword}: {ex.Message}");
        }
    }

    /// <summary>Erases the keyword, asks for the fill-in values, then inserts the result (like espanso forms).</summary>
    private void ExpandWithForm(HookMatch hookMatch, IReadOnlyList<FormField> fields, AppSettings settings)
    {
        var match = hookMatch.Match;
        var entry = match.Entry;
        if (!StillValid(hookMatch))
        {
            return;
        }

        using (InputInjector.ReleaseHeldModifiers(_hook.IsUserDown))
        {
            InputInjector.PressKey(VirtualKeys.Back, match.CharactersToDelete);
        }

        // Replay keys typed so far into the target before the form takes the focus.
        _hook.EndExpansion(hookMatch.HoldGeneration, "", wait: true);
        var values = _ui.ShowForm(entry, fields, hookMatch.Window);
        WindowHelper.Activate(hookMatch.Window);
        Thread.Sleep(80);
        var (context, hold) = _hook.BeginExpansion();
        try
        {
            if (values is null)
            {
                InputInjector.TypeText(match.OriginalText, 0);
                return;
            }

            var output = BuildOutput(entry, match, values);
            if (Insert(output, entry, settings, backspaces: 0, hookMatch.Window, Guard(context, hold)))
            {
                _ui.OnExpanded(entry, match.TypedKeyword, output);
            }
        }
        finally
        {
            _hook.EndExpansion(hold, "");
        }
    }

    private void ExpandPicked(SnippetEntry entry, nint targetWindow, bool copyToClipboard)
    {
        var settings = _settings();
        var fields = TemplateEvaluator.GetFields(entry.Content, entry.PlainText, _host);
        IReadOnlyDictionary<string, string>? values = null;
        if (fields.Count > 0)
        {
            values = _ui.ShowForm(entry, fields, targetWindow);
            if (values is null)
            {
                WindowHelper.Activate(targetWindow);
                return;
            }
        }

        var output = BuildOutput(entry, null, values);
        if (copyToClipboard)
        {
            if (ClipboardService.SetText(output.Text, transient: false))
            {
                _ui.OnExpanded(entry, "", output);
            }
            else
            {
                _ui.OnError("The snippet could not be copied because the clipboard is in use by another application.");
            }

            return;
        }

        if (!WindowHelper.Activate(targetWindow))
        {
            Log.Warn("Could not reactivate the target window for the picker.");
        }

        Thread.Sleep(60);
        var (context, hold) = _hook.BeginExpansion();
        try
        {
            if (Insert(output, entry, settings, backspaces: 0, targetWindow, Guard(context, hold)))
            {
                _ui.OnExpanded(entry, "", output);
            }
        }
        finally
        {
            _hook.EndExpansion(hold, "");
        }
    }

    private void Undo(UndoInfo undo, int hold)
    {
        if (!_hook.IsHoldCurrent(hold))
        {
            return;
        }

        try
        {
            if (!_hook.IsContextCurrent(undo.ContextGeneration) || WindowHelper.Foreground != undo.Window)
            {
                return;
            }

            using (InputInjector.ReleaseHeldModifiers(_hook.IsUserDown))
            {
                var inputs = new List<INPUT>();
                InputInjector.AddKeyPress(inputs, VirtualKeys.Back, undo.CaretLength);
                InputInjector.AddText(inputs, undo.OriginalText);
                InputInjector.Send(inputs);
            }
        }
        finally
        {
            _hook.EndExpansion(hold, "");
        }
    }

    private Func<bool> Guard(int context, int hold) => () => _hook.IsContextCurrent(context) && _hook.IsHoldCurrent(hold);

    /// <summary>
    /// The caret must still be where the keyword was typed: no click, window switch or early release of held keys.
    /// </summary>
    private bool StillValid(HookMatch hookMatch)
    {
        if (_hook.IsContextCurrent(hookMatch.ContextGeneration) && _hook.IsHoldCurrent(hookMatch.HoldGeneration) &&
            WindowHelper.Foreground == hookMatch.Window)
        {
            return true;
        }

        Log.Warn($"Skipped expanding '{hookMatch.Match.Entry.Keyword}' because the focus or caret moved.");
        _hook.EndExpansion(hookMatch.HoldGeneration, null);
        return false;
    }

    private ExpansionOutput BuildOutput(SnippetEntry entry, TypingMatch? match, IReadOnlyDictionary<string, string>? values)
    {
        var output = TemplateEvaluator.Expand(entry.Content, entry.PlainText, _host, values, EvaluationMode.Live, CultureInfo.CurrentCulture);
        foreach (var error in output.Errors)
        {
            Log.Warn($"{entry.Keyword}: {error}");
        }

        if (match is null)
        {
            return output;
        }

        if (entry.CaseMode == CaseMode.Adapt)
        {
            output = CaseAdapter.Apply(output, CaseAdapter.Detect(match.TypedKeyword, entry.Keyword), CultureInfo.CurrentCulture);
        }

        if (match.IsDelimiterTrigger && !entry.OmitDelimiter)
        {
            // Enter and Tab are re-sent as real key presses (e.g. so Enter still sends a chat message).
            output = match.TriggerChar switch
            {
                '\n' => new ExpansionOutput([.. output.Segments, new KeySegment(new KeyChord(KeyModifiers.None, VirtualKeys.Return))], output.Errors),
                '\t' => new ExpansionOutput([.. output.Segments, new KeySegment(new KeyChord(KeyModifiers.None, VirtualKeys.Tab))], output.Errors),
                _ => output.Append(match.TriggerChar.ToString()),
            };
        }

        return output;
    }

    /// <summary>
    /// Erases the keyword and inserts the output. When the clipboard is used, the previous content is restored before
    /// returning (unless another application replaced it meanwhile). Stops early, returning false, if
    /// <paramref name="isValid"/> fails because the caret may have moved.
    /// </summary>
    private bool Insert(ExpansionOutput output, SnippetEntry entry, AppSettings settings, int backspaces, nint window, Func<bool> isValid)
    {
        var method = entry.InsertMethod == InsertMethod.Default ? settings.InsertMethod : entry.InsertMethod;
        if (method is InsertMethod.Auto or InsertMethod.Default)
        {
            method = output.Text.Length > settings.ClipboardThreshold || output.Text.Contains('\n')
                ? InsertMethod.Clipboard
                : InsertMethod.Typing;
        }

        var layout = WindowHelper.GetKeyboardLayoutFor(window);
        var pending = new List<INPUT>();
        if (backspaces > 0)
        {
            InputInjector.AddKeyPress(pending, VirtualKeys.Back, backspaces);
        }

        ClipboardService.Snapshot? snapshot = null;
        uint pastedSequence = 0;
        var clipboardUsed = false;
        var cursorSeen = false;
        var actionAfterCursor = false;
        var caretAfterCursor = 0;

        void Flush()
        {
            if (pending.Count > 0)
            {
                InputInjector.Send(pending);
                pending.Clear();
            }
        }

        try
        {
            using (InputInjector.ReleaseHeldModifiers(_hook.IsUserDown))
            {
                foreach (var segment in output.Segments)
                {
                    _hook.KeepAlive();
                    if (!isValid())
                    {
                        Log.Warn($"Stopped inserting '{entry.Keyword}' because the focus or caret moved.");
                        return false;
                    }

                    switch (segment)
                    {
                        case TextSegment text:
                            if (cursorSeen)
                            {
                                caretAfterCursor += ExpansionOutput.CaretLength(text.Text);
                            }

                            if (method == InsertMethod.Clipboard && !clipboardUsed)
                            {
                                snapshot = ClipboardService.Capture();
                                if (snapshot is null)
                                {
                                    Log.Warn("The clipboard is unavailable or too large to preserve; typing the snippet instead.");
                                    method = InsertMethod.Typing;
                                }
                            }

                            if (method == InsertMethod.Clipboard)
                            {
                                Flush();
                                if (clipboardUsed)
                                {
                                    // Give the application time to read the previous paste before replacing it.
                                    Thread.Sleep(Math.Max(100, settings.ClipboardRestoreDelayMs / 3));
                                }

                                if (ClipboardService.SetText(text.Text, transient: true))
                                {
                                    clipboardUsed = true;
                                    pastedSequence = ClipboardService.SequenceNumber;
                                    InputInjector.Paste(settings.PasteShortcut);
                                }
                                else
                                {
                                    if (!clipboardUsed && snapshot is not null)
                                    {
                                        ClipboardService.Restore(snapshot);
                                        snapshot = null;
                                    }

                                    method = InsertMethod.Typing;
                                    InputInjector.TypeText(text.Text, settings.TypingDelayMs);
                                }
                            }
                            else if (settings.TypingDelayMs > 0)
                            {
                                Flush();
                                InputInjector.TypeText(text.Text, settings.TypingDelayMs);
                            }
                            else
                            {
                                InputInjector.AddText(pending, text.Text);
                            }

                            break;
                        case KeySegment key:
                            Flush();
                            actionAfterCursor |= cursorSeen;
                            InputInjector.SendChord(key.Chord, key.Repeat, layout);
                            break;
                        case DelaySegment delay:
                            Flush();
                            for (var waited = 0; waited < delay.Milliseconds; waited += 250)
                            {
                                Thread.Sleep(Math.Min(250, delay.Milliseconds - waited));
                                _hook.KeepAlive();
                            }

                            break;
                        case CursorSegment:
                            cursorSeen = true;
                            break;
                    }
                }

                Flush();
                if (cursorSeen && !actionAfterCursor && caretAfterCursor > 0)
                {
                    InputInjector.PressKey(VirtualKeys.Left, Math.Min(caretAfterCursor, 5000));
                }
            }

            return true;
        }
        finally
        {
            if (clipboardUsed && snapshot is not null)
            {
                // Keys stay held until this completes, so a held Ctrl+V or Ctrl+C acts on the user's own clipboard.
                _hook.KeepAlive();
                Thread.Sleep(settings.ClipboardRestoreDelayMs);
                if (ClipboardService.SequenceNumber == pastedSequence)
                {
                    ClipboardService.Restore(snapshot);
                }
                else
                {
                    Log.Info("The clipboard changed during the expansion; keeping the new content.");
                }
            }
        }
    }
}
