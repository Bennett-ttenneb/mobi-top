using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Slim dark window frames for app windows inside the Mobi-Top window. Windows gives child windows
// its old classic frame, so each borderless scrcpy window is put inside one of these instead:
// a thin title bar (title, minimise, maximise, close), drag to move, edges to resize,
// double-click to maximise. Minimised windows become small tabs above the Mobi-Top taskbar.
public class AppFrame : Control
{
    static Control ui;            // frames live on their own thread so the script's adb polling never freezes them
    static float scale = 1;
    static int dockBottom;        // pixels from the bottom of the Mobi-Top window kept for its taskbar
    static readonly List<AppFrame> frames = new List<AppFrame>();

    static readonly Color Bar = Color.FromArgb(0x10, 0x18, 0x26), BarActive = Color.FromArgb(0x14, 0x20, 0x34),
        Line = Color.FromArgb(0x1F, 0x3A, 0x5A), Fg = Color.FromArgb(0xE8, 0xF1, 0xFF), Muted = Color.FromArgb(0x7E, 0x93, 0xB0),
        Accent = Color.FromArgb(0x4F, 0xD1, 0xFF), Hover = Color.FromArgb(0x1F, 0x2C, 0x44), CloseHover = Color.FromArgb(0xE8, 0x11, 0x23);

    readonly IntPtr parent, app;
    readonly string title;
    Rectangle restore;
    bool max, mini, active;
    int hover = -1;               // 0 minimise, 1 maximise, 2 close
    readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer { Interval = 500 };

    int BarH { get { return (int)(30 * scale); } }
    int Edge { get { return (int)(5 * scale); } }
    int BtnW { get { return (int)(44 * scale); } }

    public static void Start(float dpiScale, int taskbarPx)
    {
        scale = dpiScale; dockBottom = taskbarPx;
        var ready = new ManualResetEvent(false);
        var t = new Thread(() => { ui = new Control(); ui.CreateControl(); ready.Set(); Application.Run(); });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        while (!ready.WaitOne(10)) Application.DoEvents();
    }

    // Puts the scrcpy window `app` (client size w x h) in a new frame at x,y inside `parent`
    public static IntPtr Wrap(IntPtr parent, IntPtr app, string title, int x, int y, int w, int h)
    {
        // A plain blocking Invoke: pumping messages while waiting here never returns once the frame
        // has been put inside the Mobi-Top window
        return (IntPtr)ui.Invoke(new Func<IntPtr>(() => new AppFrame(parent, app, title, x, y, w, h).Handle));
    }

    // The script's thread owns Windows Forms' hidden system window (it used Screen first); the frame
    // thread needs it to answer while starting up, so Start keeps handling its messages instead of
    // just blocking, and the script's main loop calls this between its sleeps.
    public static void Pump() { Application.DoEvents(); }

    AppFrame(IntPtr parent, IntPtr app, string title, int x, int y, int w, int h)
    {
        this.parent = parent; this.app = app; this.title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Color.Black;
        Font = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel);
        Bounds = new Rectangle(x, y, w + 2 * Edge, h + BarH + Edge);
        CreateControl(); // as a hidden top-level window: creating it straight inside another program's window hangs
        SetWindowLong(Handle, -16, (GetWindowLong(Handle, -16) & 0x7FFFFFFF) | 0x40000000); // WS_POPUP off, WS_CHILD on
        SetParent(Handle, parent);
        SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0020 | 0x0040); // NOSIZE|NOZORDER|FRAMECHANGED|SHOWWINDOW
        int style = GetWindowLong(app, -16);
        SetWindowLong(app, -16, (style & ~unchecked((int)0x80CF0000)) | 0x40000000); // no popup/caption/frame, WS_CHILD
        SetParent(app, Handle);
        Layout_();
        ShowWindow(app, 5);
        frames.Add(this);
        Activate_();
        watch.Tick += (s, e) => { if (!IsWindow(app)) Close_(); }; // the app's window was closed: go with it
        watch.Start();
    }


    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Parent = IntPtr.Zero;
            // WS_POPUP|WS_CLIPCHILDREN|WS_CLIPSIBLINGS (WS_CHILD once inside) + WS_THICKFRAME, which Windows
            // needs before it will resize from the edges; WM_NCCALCSIZE below gives that frame zero width
            cp.Style = unchecked((int)0x80000000) | 0x02000000 | 0x04000000 | 0x00040000;
            cp.ExStyle = 0x80; // WS_EX_TOOLWINDOW: never on the Windows taskbar
            return cp;
        }
    }

    void Layout_()
    {
        if (mini) return;
        MoveWindow(app, Edge, BarH, Width - 2 * Edge, Height - BarH - Edge, true);
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Layout_(); Invalidate(); }

    Rectangle Btn(int i) { return new Rectangle(Width - (3 - i) * BtnW, 0, BtnW, BarH); }
    bool Shown(int i) { return !mini || i == 2; } // a minimised tab keeps only its close button

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(active ? BarActive : Bar);
        using (var p = new Pen(Line)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        // accent dot + title
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float d = 6 * scale;
        using (var b = new SolidBrush(active ? Accent : Muted)) g.FillEllipse(b, 12 * scale, (BarH - d) / 2, d, d);
        var tr = new Rectangle((int)(26 * scale), 0, Math.Max(0, Width - (mini ? 1 : 3) * BtnW - (int)(30 * scale)), BarH);
        TextRenderer.DrawText(g, title, Font, tr, active ? Fg : Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        // buttons, drawn as thin lines
        for (int i = 0; i < 3; i++)
        {
            if (!Shown(i)) continue;
            var r = Btn(i);
            if (hover == i) using (var b = new SolidBrush(i == 2 ? CloseHover : Hover)) g.FillRectangle(b, r);
            int cx = r.X + r.Width / 2, cy = r.Height / 2, s = (int)(5 * scale);
            using (var p = new Pen(hover == i ? Color.White : (active ? Fg : Muted), Math.Max(1, scale)))
            {
                if (i == 0) g.DrawLine(p, cx - s, cy, cx + s, cy);
                else if (i == 1 && !max) g.DrawRectangle(p, cx - s, cy - s, 2 * s, 2 * s);
                else if (i == 1) { g.DrawRectangle(p, cx - s, cy - s + 2, 2 * s - 2, 2 * s - 2); g.DrawLine(p, cx - s + 2, cy - s, cx + s, cy - s); g.DrawLine(p, cx + s, cy - s, cx + s, cy + s - 2); }
                else { g.DrawLine(p, cx - s, cy - s, cx + s, cy + s); g.DrawLine(p, cx - s, cy + s, cx + s, cy - s); }
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1;
        for (int i = 0; i < 3; i++) if (Shown(i) && Btn(i).Contains(e.Location)) h = i;
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (!mini && Btn(0).Contains(e.Location)) Minimise();
        else if (!mini && Btn(1).Contains(e.Location)) ToggleMax();
        else if (Btn(2).Contains(e.Location)) PostMessage(app, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE: scrcpy exits, the script closes the app
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDBLCLK = 0xA3, WM_PARENTNOTIFY = 0x210, WM_MOUSEACTIVATE = 0x21, WM_NCLBUTTONDOWN = 0xA1;
        if (m.Msg == 0x83 && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; } // WM_NCCALCSIZE: no visible frame, we draw our own
        if (m.Msg == WM_NCHITTEST)
        {
            var pt = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            m.Result = (IntPtr)Hit(pt);
            return;
        }
        if (m.Msg == WM_NCLBUTTONDBLCLK && (int)m.WParam == 2) { if (mini) Restore_(); else ToggleMax(); return; }
        if (m.Msg == WM_NCLBUTTONDOWN && (int)m.WParam == 2 && mini) { Restore_(); return; } // a tab: click to bring it back
        if (m.Msg == WM_NCLBUTTONDOWN || m.Msg == WM_MOUSEACTIVATE || (m.Msg == WM_PARENTNOTIFY && ((int)m.WParam & 0xFFFF) == 0x201)) Activate_();
        base.WndProc(ref m);
    }

    int Hit(Point p)
    {
        const int CLIENT = 1, CAPTION = 2, LEFT = 10, RIGHT = 11, TOP = 12, TOPLEFT = 13, TOPRIGHT = 14, BOTTOM = 15, BOTTOMLEFT = 16, BOTTOMRIGHT = 17;
        for (int i = 0; i < 3; i++) if (Shown(i) && Btn(i).Contains(p)) return CLIENT;
        if (!max && !mini)
        {
            bool l = p.X < Edge, r = p.X >= Width - Edge, t = p.Y < Edge, b = p.Y >= Height - Edge;
            if (t && l) return TOPLEFT; if (t && r) return TOPRIGHT; if (b && l) return BOTTOMLEFT; if (b && r) return BOTTOMRIGHT;
            if (l) return LEFT; if (r) return RIGHT; if (t) return TOP; if (b) return BOTTOM;
        }
        return p.Y < BarH ? CAPTION : CLIENT;
    }

    void Activate_()
    {
        foreach (var f in frames) if (f != this && f.active) { f.active = false; f.Invalidate(); }
        active = true; Invalidate();
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0003); // HWND_TOP, no move/size
    }

    Rectangle Area()
    {
        RECT r; GetClientRect(parent, out r);
        return new Rectangle(0, 0, r.R, r.B - dockBottom);
    }

    void ToggleMax()
    {
        if (mini) return;
        if (max) { max = false; Bounds = restore; }
        else { restore = Bounds; max = true; Bounds = Area(); }
        Activate_();
    }

    void Minimise()
    {
        if (!max) restore = Bounds;
        mini = true;
        ShowWindow(app, 0);
        Dock_();
    }

    void Restore_()
    {
        mini = false;
        Bounds = max ? Area() : restore;
        ShowWindow(app, 5);
        Layout_();
        Dock_();
        Activate_();
    }

    // Minimised windows sit in a row of tabs just above the Mobi-Top taskbar
    void Dock_()
    {
        var a = Area();
        int x = (int)(8 * scale), w = (int)(200 * scale);
        foreach (var f in frames)
        {
            if (!f.mini) continue;
            f.Bounds = new Rectangle(x, a.Bottom - f.BarH - (int)(6 * scale), w, f.BarH);
            x += w + (int)(6 * scale);
        }
    }

    void Close_()
    {
        watch.Stop();
        frames.Remove(this);
        Dock_();
        DestroyHandle();
        Dispose();
    }

    struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
}
