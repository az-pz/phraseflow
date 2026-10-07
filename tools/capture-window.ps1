# Captures a window of a process to a PNG using PrintWindow (works when the window is covered).
# Usage: capture-window.ps1 -ProcessId 1234 -Output shot.png [-TitleContains "PhraseFlow"]
param(
    [Parameter(Mandatory)] [int] $ProcessId,
    [Parameter(Mandatory)] [string] $Output,
    [string] $TitleContains = ""
)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinCapture {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static List<IntPtr> Find(uint pid, string title) {
        var result = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) {
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                if (title.Length == 0 || sb.ToString().Contains(title)) result.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
"@
[WinCapture]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
$handles = [WinCapture]::Find([uint32]$ProcessId, $TitleContains)
if ($handles.Count -eq 0) { Write-Error "No visible window found"; exit 1 }
$h = $handles[0]
$rect = New-Object WinCapture+RECT
[WinCapture]::GetWindowRect($h, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left; $hgt = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[WinCapture]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "Captured ${w}x${hgt} -> $Output"

