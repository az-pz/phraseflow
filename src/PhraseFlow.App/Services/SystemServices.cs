using System.IO.Pipes;
using System.Media;
using Microsoft.Win32;

namespace PhraseFlow.App.Services;

/// <summary>Adds or removes PhraseFlow from the per-user "Run" registry key.</summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PhraseFlow";

    public static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                key.SetValue(ValueName, Command);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error("Could not update the startup registration", ex);
        }
    }
}

/// <summary>Plays a short, soft "pop" generated in memory when a snippet expands.</summary>
internal static class SoundService
{
    private static readonly Lazy<SoundPlayer> Player = new(() =>
    {
        var player = new SoundPlayer(new MemoryStream(CreatePop()));
        player.Load();
        return player;
    });

    public static void Play()
    {
        try
        {
            Player.Value.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            Log.Warn("Could not play the expansion sound: " + ex.Message);
        }
    }

    private static byte[] CreatePop()
    {
        const int sampleRate = 22050;
        const double seconds = 0.07;
        var samples = (int)(sampleRate * seconds);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / sampleRate;
            var frequency = 900 + 900 * t / seconds;
            var envelope = Math.Sin(Math.PI * i / samples);
            writer.Write((short)(Math.Sin(2 * Math.PI * frequency * t) * envelope * 6000));
        }

        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>Ensures one running instance; later launches ask the first one to show its window.</summary>
internal sealed class SingleInstance : IDisposable
{
    private const string Name = "PhraseFlow.SingleInstance";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancel = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    public static SingleInstance? TryAcquire(bool requestExit = false)
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{Name}.{Environment.UserName}", out var createdNew);
        if (createdNew)
        {
            if (!requestExit)
            {
                return new SingleInstance(mutex);
            }

            mutex.ReleaseMutex();
        }

        mutex.Dispose();
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            client.WriteByte(requestExit ? ExitMessage : ShowMessage);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
        }

        return null;
    }

    public const byte ShowMessage = 1;
    public const byte ExitMessage = 2;

    private static string PipeName => $"{Name}.{Environment.UserName}";

    /// <summary>Invokes <paramref name="onMessage"/> whenever another instance is launched (show or exit request).</summary>
    public void Listen(Action<byte> onMessage)
    {
        _ = Task.Run(async () =>
        {
            while (!_cancel.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cancel.Token);
                    var message = server.ReadByte();
                    onMessage(message == ExitMessage ? ExitMessage : ShowMessage);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException ex)
                {
                    Log.Warn("Single-instance pipe error: " + ex.Message);
                    await Task.Delay(500);
                }
            }
        });
    }

    public void Dispose()
    {
        _cancel.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex.Dispose();
    }
}
