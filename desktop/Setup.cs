using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Microsoft.Win32;

// Mobi-Top installer: unpacks the bundled app (launcher, scrcpy + adb, phone APK) for this user,
// adds Start menu and Desktop shortcuts and an Apps & features entry. No admin rights needed.
class Setup
{
    static readonly string Target = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mobi-Top");

    readonly Window win;
    readonly Ellipse dot;
    readonly TextBlock status;
    readonly Border go, fill;
    bool done;

    [STAThread]
    static void Main() { Ui.Run(() => new Setup().win); }

    Setup()
    {
        StackPanel body;
        win = Ui.Frame("Mobi-Top Setup", 480, out body);
        body.Children.Add(Ui.Logo());
        body.Children.Add(Ui.Label("MOBI-TOP", 34, Ui.Text));
        body.Children.Add(Ui.Label("Setup", 15, Ui.Muted));
        body.Children.Add(Ui.Status(out dot, out status));
        status.Text = "Ready to install";
        dot.Fill = Ui.Accent;

        var track = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = Ui.Line, Margin = new Thickness(0, 0, 0, 26) };
        fill = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(0x4F, 0xD1, 0xFF), BlurRadius = 10, ShadowDepth = 0 } };
        track.Child = fill;
        body.Children.Add(track);

        go = Ui.Button("INSTALL", true, Go);
        body.Children.Add(go);
        body.Children.Add(Ui.Label("Installs for you only, in " + Target + ". Includes everything it needs: the Mobi-Top desktop, scrcpy and adb, and the phone app.", 12, Ui.Muted));
    }

    void Go()
    {
        if (done) { Process.Start(System.IO.Path.Combine(Target, "MobiTop.exe")); win.Close(); return; }
        go.IsEnabled = false;
        dot.Fill = Ui.Accent; status.Text = "Installing…";
        Task.Run(() =>
        {
            try { Install(p => win.Dispatcher.Invoke(() => fill.Width = ((FrameworkElement)fill.Parent).ActualWidth * p)); }
            catch (Exception e)
            {
                win.Dispatcher.Invoke(() => { dot.Fill = Ui.Bad; status.Text = "Install failed: " + e.Message; go.IsEnabled = true; });
                return;
            }
            win.Dispatcher.Invoke(() =>
            {
                done = true;
                dot.Fill = Ui.Good; status.Text = "Installed · shortcuts on your Desktop and Start menu";
                Ui.SetText(go, "LAUNCH MOBI-TOP");
                go.IsEnabled = true;
            });
        });
    }

    static void Install(Action<double> progress)
    {
        // Close a running copy first, or its files are locked
        foreach (var p in Process.GetProcesses().Where(p => p.ProcessName == "MobiTop" || p.ProcessName == "scrcpy" || p.ProcessName == "adb"))
        {
            try { if (p.MainModule.FileName.StartsWith(Target, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); } } catch (Exception) { }
        }

        Directory.CreateDirectory(Target);
        using (var zip = new ZipArchive(typeof(Setup).Assembly.GetManifestResourceStream("payload.zip")))
        {
            var files = zip.Entries.Where(e => e.Name != "").ToList();
            for (int i = 0; i < files.Count; i++)
            {
                var dest = System.IO.Path.Combine(Target, files[i].FullName);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                files[i].ExtractToFile(dest, true);
                progress((i + 1.0) / files.Count);
            }
        }

        var exe = System.IO.Path.Combine(Target, "MobiTop.exe");
        var icon = System.IO.Path.Combine(Target, "mobitop.ico");
        Shortcut(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mobi-Top.lnk"), exe, icon);
        Shortcut(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mobi-Top.lnk"), exe, icon);

        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MobiTop"))
        {
            key.SetValue("DisplayName", "Mobi-Top");
            key.SetValue("DisplayVersion", "1.0");
            key.SetValue("Publisher", "BBotnics Industries");
            key.SetValue("DisplayIcon", icon);
            key.SetValue("InstallLocation", Target);
            key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new DirectoryInfo(Target).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024), RegistryValueKind.DWord);
        }
    }

    static void Shortcut(string path, string target, string icon)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.WorkingDirectory = System.IO.Path.GetDirectoryName(target);
        link.IconLocation = icon;
        link.Description = "Use your phone as a desktop";
        link.Save();
    }
}
