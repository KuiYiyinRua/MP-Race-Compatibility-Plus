param(
    [Parameter(Mandatory=$true)][string]$RunRoot,
    [Parameter(Mandatory=$true)][int]$GamePid,
    [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference = 'Stop'
$run = [IO.Path]::GetFullPath($RunRoot).TrimEnd('\')
$process = Get-CimInstance Win32_Process -Filter "ProcessId=$GamePid"
if (!$process -or !$process.ExecutablePath.StartsWith((Join-Path $run 'Game'), [StringComparison]::OrdinalIgnoreCase) -or
    !$process.CommandLine.Contains('-miliraactionsprobe') -or !$process.CommandLine.Contains('-handbookmanual')) {
    throw 'PID does not belong to this run and its gated manual handbook probe'
}
$target = [IO.Path]::GetFullPath($Output)
if (!$target.StartsWith($run + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Capture output must stay inside the run root' }
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HandbookCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@
$window = (Get-Process -Id $GamePid).MainWindowHandle
if ($window -eq [IntPtr]::Zero) { throw 'Game window handle unavailable' }
[void][HandbookCapture]::ShowWindow($window, 9)
[void][HandbookCapture]::SetForegroundWindow($window)
Start-Sleep -Milliseconds 250
$rect = New-Object HandbookCapture+Rect
if (![HandbookCapture]::GetWindowRect($window, [ref]$rect)) { throw 'GetWindowRect failed' }
$width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "Invalid window bounds: ${width}x$height" }
$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $dc = $graphics.GetHdc()
    try { if (![HandbookCapture]::PrintWindow($window, $dc, 2)) { throw 'PrintWindow failed' } }
    finally { $graphics.ReleaseHdc($dc) }
    $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $graphics.Dispose(); $bitmap.Dispose()
}
[pscustomobject]@{ Pid=$GamePid; Path=$target; Width=$width; Height=$height }
