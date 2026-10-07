using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Storage;

namespace PhraseFlow.E2E;

/// <summary>
/// Live end-to-end test: starts PhraseFlow, types into its own text box with SendInput and checks the expansions.
/// Input is only sent while this harness (or PhraseFlow's own form/picker) is the foreground window, so nothing can
/// reach other applications. Usage: PhraseFlow.E2E.exe path\to\PhraseFlow.exe
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: PhraseFlow.E2E <path to PhraseFlow.exe>");
            return 2;
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new HarnessForm(Path.GetFullPath(args[0]));
        Application.Run(form);
        return form.Failures;
    }
}

internal sealed class HarnessForm : Form
{
    private readonly string _exe;
    private readonly TextBox _box;
    private readonly StringBuilder _report = new();
    private Process? _app;

    public HarnessForm(string exe)
    {
        _exe = exe;
        Text = "PhraseFlow E2E harness";
        Width = 900;
        Height = 400;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        _box = new TextBox { Multiline = true, Dock = DockStyle.Fill, AcceptsTab = true, Font = new Font("Consolas", 14) };
        Controls.Add(_box);
    }

    public int Failures { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            await RunAsync();
        }
        catch (Exception ex)
        {
            Record(false, "fatal", ex.ToString());
        }
        finally
        {
            await StopAppAsync();
            var path = Path.Combine(AppContext.BaseDirectory, "e2e-results.txt");
            File.WriteAllText(path, _report.ToString());
            Console.WriteLine(_report.ToString());
            Console.WriteLine(Failures == 0 ? "ALL PASSED" : $"{Failures} FAILED");
            Close();
        }
    }

    private async Task RunAsync()
    {
        var directory = Path.GetDirectoryName(_exe)!;
        File.WriteAllText(Path.Combine(directory, "portable.txt"), "");
        var data = Path.Combine(directory, "Data");
        if (Directory.Exists(data))
        {
            Directory.Delete(data, recursive: true);
        }

        // The built-in library plus a few test-only snippets; scripts are enabled for the script scenario.
        var paths = DataPaths.Resolve(directory);
        var library = DefaultLibrary.CreateLibrary();
        library.Groups.Add(CreateTestGroup());
        new LibraryStore(paths.LibraryFile, paths.BackupDirectory).Save(library);
        new SettingsStore(paths.SettingsFile).Save(new AppSettings { AllowScripts = true });

        _app = Process.Start(new ProcessStartInfo(_exe, "--minimized") { UseShellExecute = false });
        var log = Path.Combine(data, "phraseflow.log");
        for (var i = 0; i < 100 && !(File.Exists(log) && File.ReadAllText(log).Contains("Started with")); i++)
        {
            await Task.Delay(150);
        }

        await Task.Delay(500);
        if (!Native.ForceForeground(Handle))
        {
            Record(false, "setup", "Could not bring the harness window to the foreground; no input was sent.");
            return;
        }

        _box.Focus();

        await Check("autocorrect", () => Type("I recieve teh mail "), "I receive the mail ");
        await Check("abbreviation", () => Type(";btw "), "by the way ");
        await Check("case propagation", () => Type(";Btw and ;BTW "), "By the way and BY THE WAY ");
        await Check("contraction + punctuation trigger", () => Type("dont!"), "don't!");
        await Check("emoji (immediate)", () => Type("ok :tada:"), "ok 🎉");
        await Check("LaTeX symbol", () => Type("\\alpha \\Delta "), "α Δ ");
        await Check("symbol keyword with punctuation", () => Type("a ;-> b"), "a → b");
        await Check("date placeholder", () => Type(";isodate "), DateTime.Now.ToString("yyyy-MM-dd") + " ");
        await Check("regional date + period trigger", () => Type(";date."), DateTime.Now.ToString("d") + ".");
        await Check("cursor placement", async () =>
        {
            await Type(";bold ");
            await Task.Delay(500);
            await Type("x");
        }, "**x** ");
        await Check("enter as trigger", () => Type("teh\n"), "the\r\n");
        await Check("fast typing during expansion", () => Type(";btw quick", batch: true), "by the way quick");
        await Check("typing during a delay longer than the hold limit", async () =>
        {
            await Type(";e2elong ");
            await Task.Delay(300);
            await Type("abc");
            await Task.Delay(8200);
        }, "startend abc");
        await Check("shortcut held during expansion replays in order", async () =>
        {
            await Type(";e2eslow ");
            await Task.Delay(250);
            Native.SendChord(Handle, Native.VK_SHIFT, Native.VK_HOME);
            await Type("y");
            await Task.Delay(1600);
        }, "y");
        await Check("typing during script evaluation", async () =>
        {
            await Type(";e2eps ");
            await Type("abc");
            await Task.Delay(4000);
        }, "ps-ok abc");
        await Check("backspace undo", async () =>
        {
            await Type("teh ");
            await Task.Delay(600);
            await Type("\b");
        }, "teh ");
        await Check("nested snippets", () => Type(";sig "), text => text.Contains("Your Job Title | Your Company") && text.Contains("your.name@example.com"));

        Clipboard.SetText("ORIGINAL-CLIP");
        await Check("multi-line paste + clipboard restore", async () =>
        {
            await Type(";mdtable ");
            await Task.Delay(1200);
        }, text => text.Contains("| Column 1 | Column 2 | Column 3 |\r\n| --- |") && SafeClipboard() == "ORIGINAL-CLIP");

        await Check("clipboard placeholder", () => Type(";mdlink "), "[](ORIGINAL-CLIP) ");

        await Check("fill-in form", async () =>
        {
            await Type(";hello ");
            if (!await WaitForForegroundProcessAsync(_app!.Id, 3000))
            {
                CloseFormWindow();
                throw new InvalidOperationException("The fill-in form did not become the active window.");
            }

            await Task.Delay(300);
            await Native.TypeAsync("Ada\n", requiredForegroundProcess: _app.Id);
            await WaitForForegroundAsync(Handle, 3000);
            await Task.Delay(800);
        }, text => text.StartsWith("Hello Ada,", StringComparison.Ordinal) && text.Contains("Best regards,"));

        await Check("search picker", async () =>
        {
            Native.SendChord(Handle, Native.VK_CONTROL, Native.VK_MENU, Native.VK_SPACE);
            if (!await WaitForForegroundProcessAsync(_app!.Id, 3000))
            {
                throw new InvalidOperationException("The picker did not open.");
            }

            await Task.Delay(300);
            await Native.TypeAsync("tableflip\n", requiredForegroundProcess: _app.Id);
            await WaitForForegroundAsync(Handle, 3000);
            await Task.Delay(800);
        }, "(╯°□°)╯︵ ┻━┻");

        await Check("pause hotkey", async () =>
        {
            Native.SendChord(Handle, Native.VK_CONTROL, Native.VK_MENU, Native.VK_SHIFT, 'P');
            await Task.Delay(500);
            await Type("teh ");
            await Task.Delay(400);
            Native.SendChord(Handle, Native.VK_CONTROL, Native.VK_MENU, Native.VK_SHIFT, 'P');
            await Task.Delay(500);
            await Type("teh ");
        }, "teh the ");
    }

    private static SnippetGroup CreateTestGroup()
    {
        var group = new SnippetGroup { Name = "E2E tests", Description = "Snippets used by the end-to-end harness." };
        group.Snippets.Add(new Snippet { Keyword = ";e2elong", Content = "start{delay:7600}end" });
        group.Snippets.Add(new Snippet { Keyword = ";e2eslow", Content = "one{delay:1200}two" });
        group.Snippets.Add(new Snippet { Keyword = ";e2eps", Content = "{powershell:Start-Sleep -Milliseconds 700; 'ps-ok'}" });
        return group;
    }

    private async Task Check(string name, Func<Task> act, string expected) =>
        await Check(name, act, text => text == expected, expected);

    private async Task Check(string name, Func<Task> act, Func<string, bool> verify, string? expected = null)
    {
        _box.Clear();
        await Task.Delay(250);
        if (!Native.ForceForeground(Handle))
        {
            Record(false, name, "harness lost the foreground; test skipped without sending input");
            return;
        }

        _box.Focus();
        string text;
        try
        {
            // The box was cleared programmatically; End tells PhraseFlow the caret context changed (like a click would).
            Native.SendChord(Handle, Native.VK_END);
            await Task.Delay(100);
            await act();
            await Task.Delay(900);
            text = _box.Text;
        }
        catch (Exception ex)
        {
            Record(false, name, ex.Message + $" (text: [{Escape(_box.Text)}])");
            return;
        }

        var ok = verify(text);
        Record(ok, name, $"got [{Escape(text)}]" + (ok || expected is null ? "" : $" expected [{Escape(expected)}]"));
    }

    private Task Type(string text, bool batch = false) => Native.TypeAsync(text, requiredForegroundWindow: Handle, batch: batch);

    private void Record(bool ok, string name, string detail)
    {
        if (!ok)
        {
            Failures++;
        }

        _report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
    }

    private static string Escape(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n");

    private static string? SafeClipboard()
    {
        try
        {
            return Clipboard.GetText();
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static async Task<bool> WaitForForegroundProcessAsync(int pid, int timeoutMs)
    {
        for (var waited = 0; waited < timeoutMs; waited += 50)
        {
            if (Native.ForegroundProcessId() == pid)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static async Task WaitForForegroundAsync(nint hwnd, int timeoutMs)
    {
        for (var waited = 0; waited < timeoutMs && Native.GetForegroundWindow() != hwnd; waited += 50)
        {
            await Task.Delay(50);
        }
    }

    private void CloseFormWindow()
    {
        if (_app is null)
        {
            return;
        }

        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, _app.Id),
            new PropertyCondition(AutomationElement.NameProperty, "Fill in snippet"));
        var window = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
        if (window?.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern) == true)
        {
            ((WindowPattern)pattern).Close();
        }
    }

    private async Task StopAppAsync()
    {
        if (_app is null)
        {
            return;
        }

        using (var stop = Process.Start(new ProcessStartInfo(_exe, "--exit") { UseShellExecute = false }))
        {
            stop?.WaitForExit(5000);
        }

        for (var i = 0; i < 50 && !_app.HasExited; i++)
        {
            await Task.Delay(100);
        }

        if (!_app.HasExited)
        {
            _app.Kill();
            _report.AppendLine("NOTE  PhraseFlow did not exit on --exit and was killed.");
        }
        else
        {
            _report.AppendLine("NOTE  PhraseFlow exited cleanly via --exit.");
        }
    }
}
