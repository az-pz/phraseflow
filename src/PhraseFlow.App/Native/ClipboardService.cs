using System.Runtime.InteropServices;
using static PhraseFlow.App.Native.NativeMethods;

namespace PhraseFlow.App.Native;

/// <summary>Win32 clipboard access that works from any thread and preserves all data formats.</summary>
internal static unsafe class ClipboardService
{
    private static readonly uint ExcludeFromMonitoring = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint CanIncludeInHistory = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    private static readonly uint CanUploadToCloud = RegisterClipboardFormatW("CanUploadToCloudClipboard");

    /// <summary>Window that owns clipboard data we set; must belong to a thread that pumps messages.</summary>
    public static nint OwnerWindow { get; set; }

    public sealed record Snapshot(IReadOnlyList<(uint Format, byte[] Data)> Items);

    public static string? GetText()
    {
        if (!Open())
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
            {
                return null;
            }

            var pointer = GlobalLock(handle);
            if (pointer is null)
            {
                return null;
            }

            try
            {
                var maxChars = (int)(GlobalSize(handle) / 2);
                var span = new ReadOnlySpan<char>(pointer, maxChars);
                var end = span.IndexOf('\0');
                return new string(end >= 0 ? span[..end] : span);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Puts text on the clipboard, optionally hiding it from clipboard history and managers.</summary>
    public static bool SetText(string text, bool transient)
    {
        // Windows text controls expect CRLF line breaks in CF_UNICODETEXT.
        text = text.ReplaceLineEndings("\r\n");
        if (!Open())
        {
            return false;
        }

        try
        {
            EmptyClipboard();
            var bytes = new byte[(text.Length + 1) * 2];
            MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
            var ok = SetData(CF_UNICODETEXT, bytes);
            if (transient)
            {
                MarkTransient();
            }

            return ok;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Copies every memory-based clipboard format so the clipboard can be restored after a paste.</summary>
    public static Snapshot? Capture(long maxBytes = 64L * 1024 * 1024)
    {
        if (!Open())
        {
            return null;
        }

        try
        {
            var items = new List<(uint, byte[])>();
            long total = 0;
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                if (!IsCopyableFormat(format))
                {
                    continue;
                }

                var handle = GetClipboardData(format);
                if (handle == 0)
                {
                    continue;
                }

                var size = (long)GlobalSize(handle);
                total += size;
                if (total > maxBytes)
                {
                    return null;
                }

                var pointer = GlobalLock(handle);
                if (pointer is null)
                {
                    continue;
                }

                try
                {
                    items.Add((format, new ReadOnlySpan<byte>(pointer, (int)size).ToArray()));
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }

            return new Snapshot(items);
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool Restore(Snapshot snapshot)
    {
        if (!Open())
        {
            Log.Warn("Could not open the clipboard to restore its previous content.");
            return false;
        }

        try
        {
            EmptyClipboard();
            var ok = true;
            foreach (var (format, data) in snapshot.Items)
            {
                ok &= SetData(format, data);
            }

            if (snapshot.Items.Count > 0)
            {
                MarkTransient();
            }

            if (!ok)
            {
                Log.Warn("Some clipboard formats could not be restored.");
            }

            return ok;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Changes every time any application writes to the clipboard.</summary>
    public static uint SequenceNumber => GetClipboardSequenceNumber();

    private static bool Open()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(OwnerWindow))
            {
                return true;
            }

            Thread.Sleep(15 + attempt * 10);
        }

        return false;
    }

    private static void MarkTransient()
    {
        SetData(ExcludeFromMonitoring, [0, 0, 0, 0]);
        SetData(CanIncludeInHistory, [0, 0, 0, 0]);
        SetData(CanUploadToCloud, [0, 0, 0, 0]);
    }

    private static bool SetData(uint format, byte[] data)
    {
        if (format == 0)
        {
            return false;
        }

        var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)Math.Max(data.Length, 1));
        if (handle == 0)
        {
            return false;
        }

        var pointer = GlobalLock(handle);
        if (pointer is null)
        {
            GlobalFree(handle);
            return false;
        }

        data.AsSpan().CopyTo(new Span<byte>(pointer, data.Length));
        GlobalUnlock(handle);
        if (SetClipboardData(format, handle) == 0)
        {
            GlobalFree(handle);
            return false;
        }

        return true;
    }

    private static bool IsCopyableFormat(uint format) => format switch
    {
        // GDI handles and owner-drawn formats are not HGLOBAL memory; Windows synthesizes bitmaps from CF_DIB.
        2 or 3 or 9 or 14 or 0x80 or 0x82 or 0x83 or 0x8E => false,
        >= 0x0200 and <= 0x02FF => false,
        >= 0x0300 and <= 0x03FF => false,
        _ => true,
    };
}
