param(
    [Parameter(Mandatory=$true)][string]$RunRoot,
    [Parameter(Mandatory=$true)][int]$GamePid,
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y
)
$ErrorActionPreference = 'Stop'
$run = [IO.Path]::GetFullPath($RunRoot).TrimEnd('\')
$process = Get-CimInstance Win32_Process -Filter "ProcessId=$GamePid"
if (!$process -or !$process.ExecutablePath.StartsWith((Join-Path $run 'Game'), [StringComparison]::OrdinalIgnoreCase) -or
    !$process.CommandLine.Contains('-miliraactionsprobe') -or !$process.CommandLine.Contains('-handbookmanual')) {
    throw 'PID does not belong to this run and its gated manual handbook probe'
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HandbookMouse {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref Point point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
}
'@
$window = (Get-Process -Id $GamePid).MainWindowHandle
if ($window -eq [IntPtr]::Zero) { throw 'Game window handle unavailable' }
$client = New-Object HandbookMouse+Rect
$sized = $false
for ($attempt=0; $attempt -lt 5; $attempt++) {
    [void][HandbookMouse]::ShowWindowAsync($window, 9)
    [void][HandbookMouse]::ShowWindow($window, 9)
    Start-Sleep -Milliseconds 400
    if ([HandbookMouse]::GetClientRect($window, [ref]$client) -and $client.Right -gt 0 -and $client.Bottom -gt 0) { $sized = $true; break }
}
if (!$sized -or $X -lt 0 -or $Y -lt 0 -or
    $X -ge $client.Right -or $Y -ge $client.Bottom) { throw "Click outside client area: $X,$Y bounds=$($client.Right)x$($client.Bottom) hwnd=$window" }
$priorWindow = [HandbookMouse]::GetForegroundWindow()
$priorPoint = New-Object HandbookMouse+Point
[void][HandbookMouse]::GetCursorPos([ref]$priorPoint)
try {
    [void][HandbookMouse]::ShowWindow($window, 9)
    [void][HandbookMouse]::SetForegroundWindow($window)
    Start-Sleep -Milliseconds 300
    if ([HandbookMouse]::GetForegroundWindow() -ne $window) {
        $foreground = [HandbookMouse]::GetForegroundWindow()
        [uint32]$foregroundPid = 0
        $foregroundThread = [HandbookMouse]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid)
        $currentThread = [HandbookMouse]::GetCurrentThreadId()
        $attached = $foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and
            [HandbookMouse]::AttachThreadInput($currentThread, $foregroundThread, $true)
        try {
            [void][HandbookMouse]::BringWindowToTop($window)
            [void][HandbookMouse]::SetForegroundWindow($window)
        } finally {
            if ($attached) { [void][HandbookMouse]::AttachThreadInput($currentThread, $foregroundThread, $false) }
        }
        Start-Sleep -Milliseconds 300
        if ([HandbookMouse]::GetForegroundWindow() -ne $window) {
            [HandbookMouse]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
            try { [void][HandbookMouse]::SetForegroundWindow($window) }
            finally { [HandbookMouse]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero) }
            Start-Sleep -Milliseconds 300
        }
        if ([HandbookMouse]::GetForegroundWindow() -ne $window) {
            [HandbookMouse]::SwitchToThisWindow($window, $true)
            Start-Sleep -Milliseconds 300
        }
        if ([HandbookMouse]::GetForegroundWindow() -ne $window) {
            $pointParam = [IntPtr](($Y -shl 16) -bor ($X -band 0xffff))
            if (![HandbookMouse]::PostMessage($window, 0x0200, [UIntPtr]::Zero, $pointParam)) { throw 'WM_MOUSEMOVE dispatch failed' }
            if (![HandbookMouse]::PostMessage($window, 0x0201, [UIntPtr]::new(1), $pointParam)) { throw 'WM_LBUTTONDOWN dispatch failed' }
            Start-Sleep -Milliseconds 80
            if (![HandbookMouse]::PostMessage($window, 0x0202, [UIntPtr]::Zero, $pointParam)) { throw 'WM_LBUTTONUP dispatch failed' }
            return [pscustomobject]@{ Pid=$GamePid; ClientX=$X; ClientY=$Y; Mode='targeted-window-messages'; ForegroundPid=$foregroundPid; ForegroundHwnd=$foreground }
        }
    }
    $point = New-Object HandbookMouse+Point
    $point.X = $X; $point.Y = $Y
    if (![HandbookMouse]::ClientToScreen($window, [ref]$point)) { throw 'ClientToScreen failed' }
    [void][HandbookMouse]::SetCursorPos($point.X, $point.Y)
    Start-Sleep -Milliseconds 100
    [HandbookMouse]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [HandbookMouse]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    [pscustomobject]@{ Pid=$GamePid; ClientX=$X; ClientY=$Y; ScreenX=$point.X; ScreenY=$point.Y }
} finally {
    [void][HandbookMouse]::SetCursorPos($priorPoint.X, $priorPoint.Y)
    if ($priorWindow -ne [IntPtr]::Zero -and $priorWindow -ne $window) {
        [void][HandbookMouse]::SetForegroundWindow($priorWindow)
    }
}
