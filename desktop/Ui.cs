using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// Shared look for the launcher and the installer: dark glass card, thin cyan accents, glowing logo.
static class Ui
{
    public static readonly SolidColorBrush Accent = Hex("#4FD1FF"), Text = Hex("#E8F1FF"), Muted = Hex("#7E93B0"),
        Line = Hex("#1F3A5A"), Good = Hex("#4FFFB0"), Warn = Hex("#FFC857"), Bad = Hex("#FF5C7A");
    public static readonly FontFamily Font = new FontFamily("Bahnschrift, Segoe UI");
    public static string Dir = AppDomain.CurrentDomain.BaseDirectory;

    // Runs the app; any crash is written to %TEMP%\mobitop-error.txt and shown instead of vanishing silently
    public static void Run(Func<Window> make)
    {
        var log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mobitop-error.txt");
        AppDomain.CurrentDomain.UnhandledException += (s, e) => File.WriteAllText(log, e.ExceptionObject.ToString());
        var app = new Application();
        app.DispatcherUnhandledException += (s, e) =>
        {
            File.WriteAllText(log, e.Exception.ToString());
            MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message + "\n\nDetails saved to " + log, "Mobi-Top", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        app.Run(make());
    }

    public static SolidColorBrush Hex(string h)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFrom(h);
        b.Freeze();
        return b;
    }

    // Borderless rounded window with its own minimise/close; content goes in `body`.
    public static Window Frame(string title, double height, out StackPanel body)
    {
        var win = new Window
        {
            Title = title, Width = 460, Height = height, WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, ResizeMode = ResizeMode.CanMinimize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = Font,
        };
        var card = new Border
        {
            CornerRadius = new CornerRadius(20), BorderBrush = Line, BorderThickness = new Thickness(1), Margin = new Thickness(18),
            Background = new LinearGradientBrush(Color.FromRgb(0x0B, 0x12, 0x20), Color.FromRgb(0x10, 0x1D, 0x35), 90),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black },
        };
        var root = new Grid();
        card.Child = root;

        body = new StackPanel { Margin = new Thickness(36, 44, 36, 26) };
        root.Children.Add(body);

        var bar = new DockPanel { Height = 40, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, LastChildFill = false };
        bar.MouseLeftButtonDown += (s, e) => win.DragMove();
        var close = Glyph("✕", "Close"); close.MouseLeftButtonUp += (s, e) => win.Close();
        var min = Glyph("—", "Minimise"); min.MouseLeftButtonUp += (s, e) => win.WindowState = WindowState.Minimized;
        DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(min, Dock.Right);
        bar.Children.Add(close); bar.Children.Add(min);
        root.Children.Add(bar);

        win.Content = card;
        try { win.Icon = BitmapFrame.Create(new Uri(System.IO.Path.Combine(Dir, "mobitop.ico"))); } catch (Exception) { }
        return win;
    }

    static TextBlock Glyph(string g, string name)
    {
        var t = new TextBlock { Text = g, Foreground = Muted, FontSize = 13, Width = 40, Height = 40, Padding = new Thickness(0, 12, 0, 0),
            TextAlignment = TextAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        System.Windows.Automation.AutomationProperties.SetName(t, name);
        t.MouseEnter += (s, e) => t.Foreground = Text;
        t.MouseLeave += (s, e) => t.Foreground = Muted;
        return t;
    }

    // Glowing outline of a desktop monitor, same drawing as the phone app's icon.
    public static UIElement Logo()
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M33,35 h42 a4,4 0 0 1 4,4 v26 a4,4 0 0 1 -4,4 h-42 a4,4 0 0 1 -4,-4 v-26 a4,4 0 0 1 4,-4 z M54,69 v8 M43,79 h22"),
            Stroke = Accent, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { Color = Color.FromRgb(0x4F, 0xD1, 0xFF), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.9 },
        };
        var canvas = new Canvas { Width = 108, Height = 108 };
        canvas.Children.Add(path);
        return new Viewbox { Width = 92, Height = 92, Child = canvas, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-12, 0, 0, 4) };
    }

    public static TextBlock Label(string text, double size, Brush color, double bottom = 0)
    {
        return new TextBlock { Text = text, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };
    }

    // Flat outlined button; primary ones are cyan-tinted. Disabled = faded.
    public static Border Button(string text, bool primary, Action click)
    {
        var normal = primary ? Hex("#1A4FD1FF") : Brushes.Transparent;
        var hover = primary ? Hex("#334FD1FF") : Hex("#141F3550");
        var b = new Border
        {
            Height = 50, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = primary ? Accent : Line,
            Background = normal, Margin = new Thickness(0, 0, 0, 12), Cursor = Cursors.Hand, Focusable = true,
            Child = new TextBlock { Text = text, FontSize = 15, Foreground = primary ? Text : Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.MouseEnter += (s, e) => { if (b.IsEnabled) b.Background = hover; };
        b.MouseLeave += (s, e) => b.Background = normal;
        b.MouseLeftButtonUp += (s, e) => { if (b.IsEnabled) click(); };
        b.KeyDown += (s, e) => { if (b.IsEnabled && (e.Key == Key.Enter || e.Key == Key.Space)) click(); };
        b.IsEnabledChanged += (s, e) => b.Opacity = b.IsEnabled ? 1 : 0.35;
        return b;
    }

    public static void SetText(Border button, string text)
    {
        ((TextBlock)button.Child).Text = text;
        System.Windows.Automation.AutomationProperties.SetName(button, text);
    }

    // Status line: coloured dot + text.
    public static StackPanel Status(out Ellipse dot, out TextBlock text)
    {
        dot = new Ellipse { Width = 8, Height = 8, Fill = Muted, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        text = new TextBlock { FontSize = 14, Foreground = Text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 330 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 26) };
        row.Children.Add(dot); row.Children.Add(text);
        return row;
    }
}
