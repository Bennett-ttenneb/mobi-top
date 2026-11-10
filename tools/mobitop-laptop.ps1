# Shows Mobi-Top on this laptop via scrcpy. The desktop gets one window; every app opened
# from it gets its own real Windows window (title bar, minimise, maximise, close, resize).
# Closing an app's window closes the app.
$dir = Join-Path $PSScriptRoot 'scrcpy-win64-v5.0.1'
$adb = Join-Path $dir 'adb.exe'
$env:SCRCPY_ICON_DIR = $PSScriptRoot # mobitop icon instead of scrcpy's, if present
# App windows live inside the Mobi-Top window, where SDL never sees them gain focus; without this
# it swallows every click as a "focus" click and the app never gets it
$env:SDL_MOUSE_FOCUS_CLICKTHROUGH = '1'
# Hardware acceleration choice from the launcher (encoder and renderer options), added to every window
$accel = @("$env:MOBITOP_SCRCPY_ARGS" -split ' ' | Where-Object { $_ })
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr h, string s);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr h, IntPtr parent);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
}
"@
Add-Type -AssemblyName System.Windows.Forms

# Phone screen sized to fill the work area pixel for pixel (no blurry stretching)
[Win]::SetProcessDPIAware() | Out-Null
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$dpi = 300 # Android zoom; higher = bigger text and buttons
$frame = [int](64 * [Win]::GetDpiForSystem() / 96) # title bar, borders and a little slack
# An auto-hiding Windows taskbar isn't in the work area but still pops up over the bottom
# of the screen, right where Mobi-Top's own taskbar is; leave room for it
$reserve = 0
if ($wa.Height -eq [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height) { $reserve = [int](48 * [Win]::GetDpiForSystem() / 96) }
$deskW = [int][Math]::Floor(($wa.Width - $frame / 2) / 16) * 16 # video encoders want multiples of 16
$deskH = [int][Math]::Floor(($wa.Height - $frame - $reserve) / 16) * 16
# Phone apps are drawn big for small screens; at the desktop's 300 dpi a window squashes them.
# 160 dpi (Android's baseline) makes their text about the size of Windows apps' and gives them room.
$appDpi = 160
# App windows sit inside the Mobi-Top window, so they must fit above its taskbar
# (48dp at the desktop zoom), allowing for their title bar and the cascade offset
$appW = [Math]::Min(1280, [int]($deskW * 0.62 / 16) * 16)
$appH = [int](($deskH - 48 * $dpi / 160 - 140) / 16) * 16

function Start-Scrcpy([string[]]$opts, [string]$log) {
    Remove-Item "$log*" -ErrorAction SilentlyContinue
    Start-Process (Join-Path $dir 'scrcpy.exe') -PassThru -NoNewWindow `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err" -ArgumentList ($opts + $accel)
}

# scrcpy prints "New display: 1920x1072/240 (id=4)" once the phone has made the screen
function Wait-Display($p, [string]$log) {
    for ($i = 0; $i -lt 40 -and -not $p.HasExited; $i++) {
        Start-Sleep -Milliseconds 500
        $text = (Get-Content $log, "$log.err" -Raw -ErrorAction SilentlyContinue) -join ''
        $m = [regex]::Match($text, 'New display: (\d+)x(\d+)/(\d+) \(id=(\d+)\)')
        if ($m.Success) { return $m }
    }
    return $null
}

function Get-Window($p) {
    for ($i = 0; $i -lt 40; $i++) {
        $p.Refresh()
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { return $p.MainWindowHandle }
        Start-Sleep -Milliseconds 100
    }
    return [IntPtr]::Zero
}

# A hidden, ready-made window; Mobi-Top opens the next app on its screen
function New-Spare {
    $log = Join-Path $env:TEMP "mobitop-win-$(Get-Random).log"
    $p = Start-Scrcpy @("--new-display=${appW}x${appH}/$appDpi", '--flex-display', '--no-vd-system-decorations', '--no-audio', '--window-borderless',
        '--display-ime-policy=local', '"--window-title=Mobi-Top app"', '--window-x=-4000', '--window-y=0') $log
    $m = Wait-Display $p $log
    if (-not $m) { if (-not $p.HasExited) { $p.Kill() }; return $null }
    $h = Get-Window $p
    [Win]::ShowWindow($h, 0) | Out-Null # SW_HIDE: off the taskbar until an app lands in it
    return @{ p = $p; id = $m.Groups[4].Value; h = $h }
}

# Fallback title from the package name (com.google.android.youtube -> Youtube) when Mobi-Top did not log one
function Get-Title([string]$pkg) {
    $parts = $pkg -split '\.'
    $s = $parts[-1]
    if ($parts.Count -gt 1 -and $s -in 'android', 'app', 'mobile', 'phone', 'client', 'activity') { $s = $parts[-2] }
    return (Get-Culture).TextInfo.ToTitleCase($s)
}

# Puts an app window inside the Mobi-Top window in a slim Mobi-Top frame (Frames.cs): move,
# minimise, maximise and close all work within Mobi-Top. x/y are relative to the Mobi-Top screen.
function Add-Child([IntPtr]$h, [string]$title, [int]$x, [int]$y) {
    $r = New-Object Win+RECT
    [Win]::GetClientRect($h, [ref]$r) | Out-Null
    [AppFrame]::Wrap($mainH, $h, $title, $x, $y, $r.R, $r.B) | Out-Null
}

# A phone-shaped window: same resolution and zoom as the phone's own screen, scaled to the app window height
function New-PhoneWindow([string]$title, [int]$x, [int]$y) {
    $log = Join-Path $env:TEMP "mobitop-win-$(Get-Random).log"
    $p = Start-Scrcpy @("--new-display=$phoneSize/$phoneDpi", '--no-vd-system-decorations', '--no-audio', '--window-borderless',
        '--display-ime-policy=local', "`"--window-title=$title`"", "--window-x=$x", "--window-y=$y", "--window-height=$appH") $log
    $m = Wait-Display $p $log
    if (-not $m) { if (-not $p.HasExited) { $p.Kill() }; return $null }
    return @{ p = $p; id = $m.Groups[4].Value }
}

# True when the app in this task asks to be upright only
function Test-Portrait([string]$task) {
    $lines = @(& $adb shell dumpsys activity activities)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch "Hist +#\d+: ActivityRecord\{[^}]* t$task\}") { continue }
        foreach ($l in $lines[$i..([Math]::Min($i + 80, $lines.Count - 1))]) {
            if ($l -match 'requestedOrientation=(\S+)') { return $Matches[1] -match 'PORTRAIT' }
        }
    }
    return $false
}

# Every task on the phone: display, root task, task id, package, bounds
function Get-Tasks {
    $disp = $null; $root = $null
    foreach ($line in (& $adb shell am stack list 2>$null)) {
        if ($line -match '^RootTask id=(\d+) .*displayId=(\d+)') { $root = $Matches[1]; $disp = $Matches[2]; continue }
        if ($line -match '^\s+taskId=(\d+): ([^/]+)/\S+ bounds=\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]') {
            [pscustomobject]@{ disp = $disp; root = $root; task = $Matches[1]; pkg = $Matches[2]
                l = [int]$Matches[3]; t = [int]$Matches[4]; r = [int]$Matches[5]; b = [int]$Matches[6] }
        }
    }
}

$log = Join-Path $env:TEMP 'mobitop-scrcpy.log'
$main = Start-Scrcpy @("--new-display=${deskW}x${deskH}/$dpi", '--window-x=0', "--window-y=$frame", "--window-width=$deskW", "--window-height=$deskH", '--no-vd-system-decorations', '--display-ime-policy=local', '--stay-awake', '--window-title=Mobi-Top') $log
$m = Wait-Display $main $log
$mainH = Get-Window $main # app windows go inside this one
if (-not $m) {
    Write-Host "Could not start. Check the phone is plugged in by USB, USB debugging is on,"
    Write-Host "you tapped Allow on the phone, and Mobi-Top is installed."
    Get-Content "$log.err" -ErrorAction SilentlyContinue | Select-Object -Last 5
    if (-not $main.HasExited) { $main.Kill() }
    exit 1
}
$dispW = [int]$m.Groups[1].Value; $dispH = [int]$m.Groups[2].Value
$phoneSize = ([regex]::Match((& $adb shell wm size) -join ' ', '\d+x\d+')).Value # e.g. 1080x2400
$phoneDpi = ([regex]::Match((& $adb shell wm density) -join ' ', '\d+')).Value    # e.g. 440
$taskbar = [int](48 * [int]$m.Groups[3].Value / 160)
$id = $m.Groups[4].Value
$usableH = $dispH - $taskbar
Add-Type -Path (Join-Path $PSScriptRoot 'Frames.cs') -ReferencedAssemblies System.Windows.Forms, System.Drawing -ErrorAction Stop
[AppFrame]::Start([Win]::GetDpiForSystem() / 96.0, $taskbar)

# Lets Mobi-Top (and only Mobi-Top) ask Android for full-screen app windows, a test-only option;
# without it HyperOS draws its phone-style floating frame inside each laptop window
# Only when missing: turning it on force-stops Mobi-Top, which drops its notification access until it's rebound
if (-not ((& $adb shell dumpsys platform_compat) -match 'ALLOW_TEST_API_ACCESS;.*com\.mobitop=true')) {
    & $adb shell am compat enable ALLOW_TEST_API_ACCESS com.mobitop | Out-Null
}
# Full-screen desktop, no HyperOS window frame
& $adb shell am start --display $id --windowingMode 1 -n com.mobitop/.DesktopActivity | Out-Null

$spare = $null; $wins = @(); $seen = @{}; $n = 0; $told = $null; $tick = 0
try {
    while (-not $main.HasExited) {
        for ($k = 0; $k -lt 20; $k++) { [AppFrame]::Pump(); Start-Sleep -Milliseconds 50 } # wait 1 s, keeping window messages moving (see Frames.cs)
        $tick++
        if ($spare -and $spare.p.HasExited) { $spare = $null }
        if (-not $spare) { $spare = New-Spare }
        if ($spare) {
            # scrcpy shows its window itself when the first frame arrives, which can be after we hid it
            if ([Win]::IsWindowVisible($spare.h)) { [Win]::ShowWindow($spare.h, 0) | Out-Null }
            # Tell Mobi-Top when the spare changes, and every 10 s in case Android restarted it (one adb call a second fewer)
            if ($told -ne $spare.id -or $tick % 10 -eq 0) {
                & $adb shell am broadcast -n com.mobitop/.SpareReceiver --ei display $spare.id | Out-Null
                $told = $spare.id
            }
        }

        $tasks = @(Get-Tasks)

        # An app landed on the spare screen: show its window and make a new spare
        if ($spare) {
            $t = $tasks | Where-Object { $_.disp -eq $spare.id -and $_.pkg -ne 'com.miui.home' } | Select-Object -First 1
            if ($t) {
                $h = $spare.h
                $title = Get-Title $t.pkg
                $named = @(& $adb logcat -d -s MobiTop:I 2>$null | Select-String "title $([regex]::Escape($t.pkg))=(.+)$")
                if ($named) { $title = $named[-1].Matches[0].Groups[1].Value.Trim() }
                $x = 40 + ($n % 4) * 24; $y = 20 + ($n % 4) * 24; $n++ # inside the Mobi-Top window
                # Upright-only apps (most of Xiaomi's own) keep their phone layout and zoom in any
                # window, so show them as the phone shows them: a phone-shaped screen at the phone's
                # own size and zoom, scaled down to fit. The spare stays free for the next app.
                $phone = $null
                if (Test-Portrait $t.task) { $phone = New-PhoneWindow $title $x $y }
                if ($phone) {
                    & $adb shell am display move-stack $t.root $phone.id | Out-Null
                    Add-Child (Get-Window $phone.p) $title $x $y
                    $wins += @{ p = $phone.p; root = $t.root; disp = $phone.id; empty = 0 }
                } else {
                    [Win]::SetWindowText($h, $title) | Out-Null
                    Add-Child $h $title $x $y
                    $wins += @{ p = $spare.p; root = $t.root; disp = $spare.id; empty = 0 }
                    $spare = $null
                }
            }
        }

        foreach ($w in $wins) {
            if ($w.p.HasExited) {
                # Window closed: close its app too (Android would otherwise move it back to the phone screen)
                if (-not $w.moved) { & $adb shell am stack remove $w.root | Out-Null }
                continue
            }
            # App reopened elsewhere (Android moved it to a newer window): drop this empty window, keep the app
            if (@($tasks | Where-Object { $_.disp -eq $w.disp -and $_.pkg -ne 'com.miui.home' }).Count -eq 0) {
                if (++$w.empty -ge 2) { $w.moved = $true; $w.p.Kill() }
            } else { $w.empty = 0 }
        }
        $wins = @($wins | Where-Object { -not $_.p.HasExited })

        # Fallback when no spare was ready: fit HyperOS floating windows on the desktop screen.
        # HyperOS sets a window's real size after its opening animation (when it briefly fills
        # the screen), and checks can stall while an app starts, so watch each window for its
        # first 4 checks rather than a fixed time.
        foreach ($t in $tasks) {
            if ($t.disp -ne $id -or $t.pkg -eq 'com.miui.home' -or $seen[$t.task] -ge 4) { continue }
            $full = $t.l -eq 0 -and $t.t -eq 0 -and $t.r -eq $dispW -and $t.b -eq $dispH
            if ($full -and $t.pkg -ne 'com.mobitop') { continue } # still opening; real size comes next
            $seen[$t.task]++
            if ($t.pkg -eq 'com.mobitop') {
                if (-not $full) { & $adb shell am task resize $t.task 0 0 $dispW $dispH | Out-Null; $seen[$t.task] = 4 }
                continue
            }
            if ($t.l -ge 0 -and $t.t -ge 0 -and $t.r -le $dispW -and $t.b -le $usableH) { continue } # already fits
            # Same cascade as DesktopActivity.windowBounds
            $w = [int]($dispW * 3 / 5); $h = [int]($usableH * 7 / 10); $step = $taskbar
            $x = [Math]::Max(0, [Math]::Min([int]($dispW / 8) + ($n % 6) * $step, $dispW - $w))
            $y = [Math]::Max(0, [Math]::Min([int]($usableH / 12) + ($n % 6) * $step, $usableH - $h))
            $n++
            & $adb shell am task resize $t.task $x $y ($x + $w) ($y + $h) | Out-Null
            $seen[$t.task] = 4 # done; leave it to the user from now on
        }
    }
} finally {
    # Desktop closed: close the spare and app windows too (apps stay open on the phone)
    foreach ($p in @($spare.p) + @($wins | ForEach-Object { $_.p })) { if ($p -and -not $p.HasExited) { $p.Kill() } }
    Start-Sleep -Milliseconds 500
    Remove-Item (Join-Path $env:TEMP 'mobitop-win-*.log*') -ErrorAction SilentlyContinue
}
