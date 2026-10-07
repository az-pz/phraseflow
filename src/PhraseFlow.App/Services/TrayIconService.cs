using System.Drawing;
using System.Drawing.Imaging;
using WinForms = System.Windows.Forms;

namespace PhraseFlow.App.Services;

/// <summary>Notification-area icon with a context menu.</summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly Icon _activeIcon;
    private readonly Icon _pausedIcon;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _pickerItem;

    public TrayIconService()
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PhraseFlow.ico"))!.Stream;
        _activeIcon = new Icon(stream, WinForms.SystemInformation.SmallIconSize);
        _pausedIcon = CreateGrayscale(_activeIcon);

        var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        var open = new WinForms.ToolStripMenuItem("Open PhraseFlow", null, (_, _) => OpenRequested?.Invoke()) { Font = new Font(WinForms.Control.DefaultFont, FontStyle.Bold) };
        _pickerItem = new WinForms.ToolStripMenuItem("Search snippets…", null, (_, _) => PickerRequested?.Invoke());
        var add = new WinForms.ToolStripMenuItem("New snippet…", null, (_, _) => NewSnippetRequested?.Invoke());
        _pauseItem = new WinForms.ToolStripMenuItem("Pause expansion", null, (_, _) => PauseToggled?.Invoke());
        var exit = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke());
        menu.Items.AddRange([open, _pickerItem, add, new WinForms.ToolStripSeparator(), _pauseItem, new WinForms.ToolStripSeparator(), exit]);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _activeIcon,
            Text = "PhraseFlow",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
    }

    public event Action? OpenRequested;

    public event Action? PickerRequested;

    public event Action? NewSnippetRequested;

    public event Action? PauseToggled;

    public event Action? ExitRequested;

    public void Update(bool enabled, int snippetCount, string pickerHotkey)
    {
        _notifyIcon.Icon = enabled ? _activeIcon : _pausedIcon;
        var text = enabled ? $"PhraseFlow: {snippetCount:N0} snippets active" : "PhraseFlow: paused";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        _pauseItem.Checked = !enabled;
        _pickerItem.Text = string.IsNullOrWhiteSpace(pickerHotkey) ? "Search snippets…" : $"Search snippets…   {pickerHotkey}";
    }

    public void ShowNotification(string title, string text, bool warning = false)
    {
        _notifyIcon.ShowBalloonTip(4000, title, text, warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _activeIcon.Dispose();
        _pausedIcon.Dispose();
    }

    private static Icon CreateGrayscale(Icon icon)
    {
        using var source = icon.ToBitmap();
        using var gray = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(gray))
        {
            var matrix = new ColorMatrix(
            [
                [0.30f, 0.30f, 0.30f, 0, 0],
                [0.59f, 0.59f, 0.59f, 0, 0],
                [0.11f, 0.11f, 0.11f, 0, 0],
                [0, 0, 0, 0.55f, 0],
                [0, 0, 0, 0, 1],
            ]);
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        var handle = gray.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var result = (Icon)temporary.Clone();
        DestroyIcon(handle);
        return result;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint handle);
}
