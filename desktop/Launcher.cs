using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.IO.Path;

// Mobi-Top for Windows: checks for the phone, starts the desktop (mobitop-laptop.ps1) or a plain mirror.
class Launcher
{
    enum Phone { None, Unauthorized, NoApp, Ready }

    static string Scrcpy { get { return Path.Combine(Ui.Dir, "scrcpy-win64-v5.0.1"); } }
    static string Adb { get { return Path.Combine(Scrcpy, "adb.exe"); } }

    readonly Window win;
    readonly Ellipse dot;
    readonly TextBlock status;
    readonly Border launch, mirror, installApp, accel;
    Process desktop, mirrorProc;
    bool probing, installing;
    string error;

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--selftest")) { SelfTest(); return; }
        if (args.Contains("--uninstall")) { Uninstall(); return; }
        Ui.Run(() => new Launcher().win);
    }

    Launcher()
    {
        StackPanel body;
        win = Ui.Frame("Mobi-Top", 602, out body);
        body.Children.Add(Ui.Logo());
        body.Children.Add(Ui.Label("MOBI-TOP", 34, Ui.Text));
        body.Children.Add(Ui.Label("Your phone, as a desktop.", 15, Ui.Muted));
        body.Children.Add(Ui.Status(out dot, out status));
        launch = Ui.Button("LAUNCH DESKTOP", true, ToggleDesktop);
        mirror = Ui.Button("MIRROR PHONE", false, ToggleMirror);
        installApp = Ui.Button("INSTALL PHONE APP", false, InstallApp);
        accel = Ui.Button(AccelLabel(), false, ToggleAccel);
        body.Children.Add(launch); body.Children.Add(mirror); body.Children.Add(installApp); body.Children.Add(accel);
        body.Children.Add(Ui.Label("Connect your phone by USB with USB debugging on, and tap Allow on the phone the first time.", 12, Ui.Muted));

        status.Text = "Looking for your phone…";
        launch.IsEnabled = mirror.IsEnabled = false;
        installApp.Visibility = Visibility.Collapsed;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (s, e) => { if (!Alive(desktop) && !Alive(mirrorProc)) Probe(); }; // no adb polling while a session runs
        timer.Start();
        win.Loaded += (s, e) => Probe();
        win.Closed += (s, e) => { Stop(ref desktop); Stop(ref mirrorProc); }; // closing the launcher ends what it started
    }

    // ---------- Phone status ----------

    void Probe()
    {
        if (probing || installing) return;
        probing = true;
        Task.Run(() =>
        {
            var state = ParseDevices(Run(Adb, "devices", 10));
            string model = null;
            if (state == Phone.Ready)
            {
                model = Run(Adb, "shell getprop ro.product.model", 10).Trim();
                if (!Run(Adb, "shell pm list packages com.mobitop", 10).Split('\n').Any(l => l.Trim() == "package:com.mobitop")) state = Phone.NoApp;
            }
            win.Dispatcher.Invoke(() => { Show(state, model); probing = false; });
        });
    }

    // "List of devices attached\n<serial>\tdevice" -> Ready (app not checked yet), "unauthorized" -> Unauthorized
    static Phone ParseDevices(string output)
    {
        var rows = output.Split('\n').Skip(1).Select(l => l.Trim()).Where(l => l.Contains('\t')).ToList();
        if (rows.Any(r => r.EndsWith("\tdevice"))) return Phone.Ready;
        if (rows.Any(r => r.EndsWith("\tunauthorized"))) return Phone.Unauthorized;
        return Phone.None;
    }

    void Show(Phone state, string model)
    {
        var running = Alive(desktop) || Alive(mirrorProc);
        switch (state)
        {
            case Phone.None: dot.Fill = Ui.Bad; status.Text = "No phone connected"; break;
            case Phone.Unauthorized: dot.Fill = Ui.Warn; status.Text = "Tap Allow on your phone"; break;
            case Phone.NoApp: dot.Fill = Ui.Warn; status.Text = "Connected · Mobi-Top isn't on the phone yet"; break;
            default: dot.Fill = Ui.Good; status.Text = "Connected · " + model + (running ? " · running" : ""); break;
        }
        if (error != null) { dot.Fill = Ui.Bad; status.Text = error; }
        launch.IsEnabled = state == Phone.Ready || Alive(desktop);
        mirror.IsEnabled = state == Phone.Ready || state == Phone.NoApp || Alive(mirrorProc);
        installApp.Visibility = state == Phone.NoApp ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Actions ----------

    void ToggleDesktop()
    {
        error = null;
        if (Alive(desktop)) { Stop(ref desktop); Ui.SetText(launch, "LAUNCH DESKTOP"); return; }
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(Ui.Dir, "mobitop-laptop.ps1") + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.EnvironmentVariables["MOBITOP_SCRCPY_ARGS"] = AccelArgs();
        desktop = Start(psi, () =>
        {
            Ui.SetText(launch, "LAUNCH DESKTOP");
            if (desktop != null && desktop.ExitCode != 0) error = "Couldn't start the desktop. Check USB debugging and tap Allow on the phone.";
            desktop = null;
            Probe();
        });
        if (desktop != null) { Ui.SetText(launch, "STOP DESKTOP"); Probe(); }
    }

    void ToggleMirror()
    {
        error = null;
        if (Alive(mirrorProc)) { Stop(ref mirrorProc); Ui.SetText(mirror, "MIRROR PHONE"); return; }
        var psi = new ProcessStartInfo(Path.Combine(Scrcpy, "scrcpy.exe"), "\"--window-title=Mobi-Top · Phone\" --stay-awake " + AccelArgs())
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.EnvironmentVariables["SCRCPY_ICON_DIR"] = Ui.Dir; // Mobi-Top icon on the mirror window
        mirrorProc = Start(psi, () =>
        {
            Ui.SetText(mirror, "MIRROR PHONE");
            if (mirrorProc != null && mirrorProc.ExitCode != 0) error = "Couldn't mirror the phone. Check USB debugging and tap Allow on the phone.";
            mirrorProc = null;
            Probe();
        });
        if (mirrorProc != null) { Ui.SetText(mirror, "STOP MIRROR"); Probe(); }
    }

    void InstallApp()
    {
        installing = true;
        installApp.IsEnabled = false;
        dot.Fill = Ui.Accent; status.Text = "Installing Mobi-Top on your phone…";
        Task.Run(() =>
        {
            var output = Run(Adb, "install -r \"" + Path.Combine(Ui.Dir, "Mobi-Top.apk") + "\"", 180);
            win.Dispatcher.Invoke(() =>
            {
                installing = false;
                installApp.IsEnabled = true;
                // Xiaomi phones block installs over USB unless "Install via USB" is on in Developer options
                error = output.Contains("Success") ? null : "The phone refused the install. On Xiaomi phones turn on Install via USB in Developer options, or copy Mobi-Top.apk to the phone and open it there.";
                Probe();
            });
        });
    }

    // ---------- Hardware acceleration ----------
    // On: the phone's hardware video encoder (scrcpy's default pick), then GPU decoding (D3D11VA, which
    // needs the Direct3D 11 renderer) and GPU drawing on the laptop; the CPU barely touches the video.
    // Off: Android's built-in software encoder, CPU decoding and drawing, 30 fps; slower, but a way
    // round black or glitchy windows on phones or PCs with buggy graphics drivers.
    const string Settings = @"Software\MobiTop";

    static bool Accel
    {
        get { using (var k = Registry.CurrentUser.OpenSubKey(Settings)) return k == null || !0.Equals(k.GetValue("HardwareAcceleration")); }
        set { using (var k = Registry.CurrentUser.CreateSubKey(Settings)) k.SetValue("HardwareAcceleration", value ? 1 : 0, RegistryValueKind.DWord); }
    }

    static string AccelArgs()
    {
        return Accel ? "--render-driver=direct3d11" : "--video-encoder=c2.android.avc.encoder --render-driver=software --max-fps=30";
    }

    static string AccelLabel() { return "HARDWARE ACCELERATION · " + (Accel ? "ON" : "OFF"); }

    void ToggleAccel()
    {
        Accel = !Accel;
        Ui.SetText(accel, AccelLabel());
        if (Alive(desktop) || Alive(mirrorProc)) { dot.Fill = Ui.Warn; status.Text = "Stop and start again to apply"; }
    }

    // ---------- Helpers ----------

    Process Start(ProcessStartInfo psi, Action onExit)
    {
        try
        {
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Exited += (s, e) => win.Dispatcher.BeginInvoke(onExit);
            p.Start();
            p.BeginOutputReadLine(); p.BeginErrorReadLine(); // keep the pipes drained
            return p;
        }
        catch (Exception e) { error = "Couldn't start: " + e.Message; return null; }
    }

    static bool Alive(Process p) { return p != null && !p.HasExited; }

    // Ends the process and everything it started (the desktop script starts several scrcpy windows)
    static void Stop(ref Process p)
    {
        if (Alive(p))
        {
            var kill = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true });
            if (kill != null) kill.WaitForExit(5000);
        }
        p = null;
    }

    static string Run(string exe, string args, int seconds)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            var output = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(seconds * 1000)) { try { p.Kill(); } catch (Exception) { } return ""; }
            return output.Result + err.Result;
        }
        catch (Exception) { return ""; }
    }

    // ---------- Uninstall (run from Apps & features) ----------

    static void Uninstall()
    {
        if (MessageBox.Show("Remove Mobi-Top from this PC?\n\nThe Mobi-Top app on your phone stays installed.", "Mobi-Top",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var dir = Ui.Dir.TrimEnd('\\');
        foreach (var p in Process.GetProcesses().Where(p => p.ProcessName == "scrcpy" || p.ProcessName == "adb"))
        {
            try { if (p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch (Exception) { }
        }
        foreach (var lnk in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mobi-Top.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mobi-Top.lnk") })
        {
            try { File.Delete(lnk); } catch (Exception) { }
        }
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MobiTop", false);
        Registry.CurrentUser.DeleteSubKeyTree(Settings, false);
        // This exe lives in the folder being removed, so let cmd delete it after we exit
        Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"")
        { UseShellExecute = false, CreateNoWindow = true });
    }

    static void SelfTest()
    {
        var cases = new[] {
            Tuple.Create("List of devices attached\r\n\r\n", Phone.None),
            Tuple.Create("List of devices attached\r\nabc123\tunauthorized\r\n", Phone.Unauthorized),
            Tuple.Create("* daemon started successfully\r\nList of devices attached\r\nizlr6\tdevice\r\n", Phone.Ready),
            Tuple.Create("List of devices attached\nizlr6\toffline\n", Phone.None),
        };
        foreach (var c in cases) if (ParseDevices(c.Item1) != c.Item2) Environment.Exit(1);
        Environment.Exit(0);
    }
}
