using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using PhraseFlow.App.Native;
using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.App.Services;

/// <summary>Provides clipboard, user, system, snippet and script values to the template evaluator.</summary>
internal sealed class AppExpansionHost(
    Func<KeywordIndex> index,
    Func<AppSettings> settings,
    SnippetLibrary library,
    Action countersChanged) : IExpansionHost
{
    private static readonly Lazy<string> DisplayName = new(() => WindowHelper.GetDisplayName() ?? Environment.UserName);

    public DateTime Now => DateTime.Now;

    public string UserName => Environment.UserName;

    public string FullName => DisplayName.Value;

    public string MachineName => Environment.MachineName;

    public string? GetClipboardText() => ClipboardService.GetText();

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? GetLocalIpAddress()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => n.GetIPProperties())
                .OrderByDescending(p => p.GatewayAddresses.Count > 0);
            return candidates
                .SelectMany(p => p.UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?
                .ToString();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    public SnippetReference? FindSnippet(string keyword) => index().FindByKeyword(keyword)?.ToReference();

    public long GetCounter(string name, bool increment)
    {
        long next;
        lock (library.Counters)
        {
            library.Counters.TryGetValue(name, out var current);
            next = current + 1;
            if (increment)
            {
                library.Counters[name] = next;
            }
        }

        if (increment)
        {
            countersChanged();
        }

        return next;
    }

    public string RunScript(string command, ScriptKind kind)
    {
        var current = settings();
        if (!current.AllowScripts)
        {
            throw new InvalidOperationException("Script placeholders are disabled. Enable them in Settings → Advanced.");
        }

        var start = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (kind == ScriptKind.PowerShell)
        {
            start.FileName = "powershell.exe";
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command" })
            {
                start.ArgumentList.Add(arg);
            }

            start.ArgumentList.Add("[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command);
        }
        else
        {
            start.FileName = "cmd.exe";
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("chcp 65001 >nul & " + command);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the script.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(current.ScriptTimeoutSeconds)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The script did not finish within {current.ScriptTimeoutSeconds} seconds.");
        }

        var text = output.GetAwaiter().GetResult();
        if (process.ExitCode != 0 && text.Length == 0)
        {
            throw new InvalidOperationException(error.GetAwaiter().GetResult().Trim());
        }

        return text;
    }
}
