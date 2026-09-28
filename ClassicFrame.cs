using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RetroRadio;

/// <summary>
/// A Windows 7 Aero Glass frame. Windows 11 can blur behind a whole window but not just its edges,
/// so the frame is its own blurred window, and the program's (solid) window sits exactly inside it.
/// The two move, resize, minimize, maximize and close together; the taskbar shows the frame.
/// </summary>
sealed class ClassicFrame : Form
{
    const int Caption = 31, Border = 8;
    static readonly Color GlassTint = Color.FromArgb(116, 184, 252); // Windows 7 "Sky"

    readonly Form content;
    readonly bool resizable;
    enum Part { None, Min, Max, Close }
    Part hover, down;
    float k = 1;
    bool syncing, contentClosed;

    /// <summary>Call at the end of a form's constructor.</summary>
    public static void Apply(Form content)
    {
        bool resizable = content.FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
        content.FormBorderStyle = FormBorderStyle.None;
        content.Load += (_, _) =>
        {
            var frame = new ClassicFrame(content, resizable);
            frame.Attach();
        };
    }

    ClassicFrame(Form content, bool resizable)
    {
        this.content = content;
        this.resizable = resizable;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        Text = content.Text;
        Icon = content.Icon;
        MinimizeBox = content.MinimizeBox;
        MaximizeBox = content.MaximizeBox && resizable;
        BackColor = Color.Black;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    int C => (int)(Caption * k);
    int B => (int)(Border * k);

    void Attach()
    {
        k = content.DeviceDpi / 96f;
        var inner = content.Bounds;
        if (content.StartPosition is FormStartPosition.CenterScreen or FormStartPosition.WindowsDefaultLocation)
        {
            var wa = Screen.FromControl(content).WorkingArea;
            inner.Location = new Point(wa.X + (wa.Width - inner.Width) / 2, wa.Y + (wa.Height - inner.Height - C + B) / 2 + C);
        }
        Bounds = new Rectangle(inner.X - B, inner.Y - C, inner.Width + B * 2, inner.Height + C + B);
        if (content.MinimumSize != Size.Empty)
            MinimumSize = new Size(content.MinimumSize.Width + B * 2, content.MinimumSize.Height + C + B);

        // A dialog's frame belongs to the dialog's owner and stays out of the taskbar.
        var originalOwner = content.Owner;
        if (originalOwner != null)
        {
            ShowInTaskbar = false;
            Owner = originalOwner;
        }
        content.ShowInTaskbar = false;
        Show();
        content.Owner = this; // the program window always stays on top of its frame
        SyncContent();

        LocationChanged += (_, _) => SyncContent();
        SizeChanged += (_, _) => { SyncContent(); Invalidate(); };
        content.TextChanged += (_, _) => { Text = content.Text; Invalidate(new Rectangle(0, 0, Width, C)); };
        content.Activated += (_, _) => Invalidate(new Rectangle(0, 0, Width, C));
        content.Deactivate += (_, _) => Invalidate(new Rectangle(0, 0, Width, C));
        content.FormClosed += (_, _) =>
        {
            if (contentClosed) return; // closing the frame closes its owned windows too
            contentClosed = true;
            content.Owner = null;
            Close();
        };
        // If the program moves or resizes itself, the frame follows.
        content.LocationChanged += (_, _) => FollowContent();
        content.SizeChanged += (_, _) => FollowContent();
        Activated += (_, _) =>
        {
            Invalidate(new Rectangle(0, 0, Width, C));
            if (WindowState != FormWindowState.Minimized) content.Activate();
        };
        content.Activate();
    }

    void SyncContent()
    {
        if (syncing || WindowState == FormWindowState.Minimized) return;
        syncing = true;
        content.Bounds = new Rectangle(Left + B, Top + C, Width - B * 2, Height - C - B);
        syncing = false;
    }

    void FollowContent()
    {
        if (syncing || content.WindowState != FormWindowState.Normal) return;
        syncing = true;
        var r = content.Bounds;
        Bounds = new Rectangle(r.X - B, r.Y - C, r.Width + B * 2, r.Height + C + B);
        syncing = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Blur whatever is behind the frame window.
        try
        {
            var sheet = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 }; // whole window is glass
            DwmExtendFrameIntoClientArea(Handle, ref sheet);
            var accent = new AccentPolicy { AccentState = 3 /* ACCENT_ENABLE_BLURBEHIND */ };
            int size = Marshal.SizeOf(accent);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(Handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            int corners = 3; // DWMWCP_ROUNDSMALL, closest to Windows 7's corners
            DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corners, sizeof(int));
        }
        catch (Exception)
        {
            // Without DWM the frame is simply drawn as tinted glass without the blur.
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The close button asks the program to close (it may want to save first); the frame follows it.
        if (!contentClosed && !content.IsDisposed)
        {
            e.Cancel = true;
            content.Close();
            return;
        }
        base.OnFormClosing(e);
    }

    void ToggleMax()
    {
        if (!MaximizeBox) return;
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    // ───────────────────────────── mouse on the caption buttons ─────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = PartAt(e.Location);
        if (p == hover) return;
        hover = p;
        Invalidate(ButtonsRect());
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = Part.None;
        Invalidate(ButtonsRect());
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        down = PartAt(e.Location);
        Invalidate(ButtonsRect());
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var p = PartAt(e.Location);
        var was = down;
        down = Part.None;
        Invalidate(ButtonsRect());
        if (p != was) return;
        switch (p)
        {
            case Part.Min: WindowState = FormWindowState.Minimized; break;
            case Part.Max: ToggleMax(); break;
            case Part.Close: Close(); break;
        }
    }

    // ───────────────────────────── caption buttons (Aero layout) ─────────────────────────────

    Rectangle ButtonRect(Part part)
    {
        // Min and max share one glass pill; close is wider and red. They hang from the top edge.
        int h = (int)(20 * k), top = (int)(1 * k), right = Width - (int)(7 * k);
        int wClose = (int)(46 * k), wSmall = (int)(27 * k);
        int closeX = right - wClose;
        return part switch
        {
            Part.Close => new Rectangle(closeX, top, wClose, h),
            Part.Max => new Rectangle(closeX - wSmall, top, wSmall, h),
            _ => new Rectangle(closeX - wSmall * (MaximizeBox ? 2 : 1), top, wSmall, h),
        };
    }

    IEnumerable<Part> Parts()
    {
        if (MinimizeBox) yield return Part.Min;
        if (MaximizeBox) yield return Part.Max;
        yield return Part.Close;
    }

    Rectangle ButtonsRect()
    {
        var r = Rectangle.Empty;
        foreach (var p in Parts()) r = r.IsEmpty ? ButtonRect(p) : Rectangle.Union(r, ButtonRect(p));
        r.Inflate(4, 4);
        return r;
    }

    Part PartAt(Point pt)
    {
        foreach (var p in Parts())
            if (ButtonRect(p).Contains(pt)) return p;
        return Part.None;
    }

    // ───────────────────────────── painting ─────────────────────────────

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return;
        bool active = ActiveForm == this || ActiveForm == content || ActiveForm?.Owner == content;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            PaintGlass(g, w, h, active);
            PaintCaption(g, active);
            foreach (var p in Parts()) PaintButton(g, p, active);
        }

        // Copy with the alpha channel intact, so the blur shows through the glass.
        var hdc = e.Graphics.GetHdc();
        try
        {
            var hBmp = CreateAlphaDib(bmp, out var memDc, out var old);
            try
            {
                BitBlt(hdc, 0, 0, w, h, memDc, 0, 0, 0x00CC0020 /* SRCCOPY */);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBmp);
                DeleteDC(memDc);
            }
        }
        finally
        {
            e.Graphics.ReleaseHdc(hdc);
        }
    }

    void PaintGlass(Graphics g, int w, int h, bool active)
    {
        // Tinted glass over the blurred desktop.
        int alpha = active ? 120 : 85;
        var tint = active ? GlassTint : Color.FromArgb(170, 190, 210);
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(alpha, Lighten(tint, 0.3f)), Color.FromArgb(alpha, tint), 90f))
            g.FillRectangle(fill, 0, 0, w, h);

        // Reflective diagonal streaks, the signature of Aero.
        using (var streak = new SolidBrush(Color.FromArgb(active ? 30 : 18, 255, 255, 255)))
        {
            for (int x = -h; x < w + h; x += (int)(260 * k))
            {
                g.FillPolygon(streak, new PointF[] { new(x, 0), new(x + 70 * k, 0), new(x + 70 * k - h * 0.45f, h), new(x - h * 0.45f, h) });
                g.FillPolygon(streak, new PointF[] { new(x + 90 * k, 0), new(x + 105 * k, 0), new(x + 105 * k - h * 0.45f, h), new(x + 90 * k - h * 0.45f, h) });
            }
        }
        using (var sheen = new LinearGradientBrush(new Rectangle(0, 0, w, C), Color.FromArgb(80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            g.FillRectangle(sheen, 0, 0, w, C);

        // Crisp edges: dark outline, light inner line, and a dark line around the program area.
        using (var pen = new Pen(Color.FromArgb(210, 30, 40, 55), 1)) g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
        using (var pen = new Pen(Color.FromArgb(140, 255, 255, 255), 1)) g.DrawRectangle(pen, 1, 1, w - 3, h - 3);
        using (var edge = new Pen(Color.FromArgb(220, 40, 50, 62), 1)) g.DrawRectangle(edge, B - 1, C - 1, w - B * 2 + 1, h - C - B + 1);
        using (var edgeHi = new Pen(Color.FromArgb(120, 255, 255, 255), 1)) g.DrawRectangle(edgeHi, B - 2, C - 2, w - B * 2 + 3, h - C - B + 3);
    }

    static Color Lighten(Color c, float t) => Color.FromArgb((int)(c.R + (255 - c.R) * t), (int)(c.G + (255 - c.G) * t), (int)(c.B + (255 - c.B) * t));

    void PaintCaption(Graphics g, bool active)
    {
        int iconSize = (int)(16 * k), x = (int)(9 * k);
        if (Icon != null)
        {
            using var icon = new Icon(Icon, iconSize, iconSize);
            g.DrawIcon(icon, new Rectangle(x, (C - iconSize) / 2, iconSize, iconSize));
            x += iconSize + (int)(7 * k);
        }
        using var f = new Font("Segoe UI", 9.5f * k, GraphicsUnit.Point);
        var rect = new RectangleF(x, 0, ButtonsRect().Left - x - 6, C);
        var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        // Aero's white glow behind the black caption text.
        using (var glow = new SolidBrush(Color.FromArgb(active ? 46 : 30, 255, 255, 255)))
        {
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -5; dx <= 5; dx++)
                    if (dx * dx / 25f + dy * dy / 9f <= 1)
                        g.DrawString(Text, f, glow, new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height), sf);
        }
        using var ink = new SolidBrush(active ? Color.FromArgb(10, 10, 12) : Color.FromArgb(70, 72, 78));
        g.DrawString(Text, f, ink, rect, sf);
    }

    void PaintButton(Graphics g, Part part, bool active)
    {
        var r = ButtonRect(part);
        bool hot = hover == part, pressed = down == part && hot;
        float rad = 4 * k;

        // The group is rounded only at its outer bottom corners.
        bool leftEnd = part == Parts().First(), rightEnd = part == Part.Close;
        using var path = new GraphicsPath();
        path.AddLine(r.Left, r.Top, r.Right, r.Top);
        if (rightEnd) path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
        else path.AddLine(r.Right, r.Top, r.Right, r.Bottom);
        if (leftEnd) path.AddArc(r.Left, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
        else path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
        path.CloseFigure();

        Color top, mid, bottom;
        if (part == Part.Close)
        {
            (top, mid, bottom) = pressed ? (Color.FromArgb(220, 150, 60, 50), Color.FromArgb(230, 130, 20, 10), Color.FromArgb(230, 190, 60, 40))
                : hot ? (Color.FromArgb(240, 250, 170, 150), Color.FromArgb(240, 230, 60, 40), Color.FromArgb(240, 250, 150, 90))
                : active ? (Color.FromArgb(230, 230, 150, 140), Color.FromArgb(230, 199, 60, 45), Color.FromArgb(230, 215, 110, 80))
                : (Color.FromArgb(150, 220, 225, 235), Color.FromArgb(140, 170, 185, 205), Color.FromArgb(140, 200, 210, 225));
        }
        else
        {
            (top, mid, bottom) = pressed ? (Color.FromArgb(200, 120, 160, 200), Color.FromArgb(200, 40, 90, 150), Color.FromArgb(200, 80, 160, 220))
                : hot ? (Color.FromArgb(220, 230, 245, 255), Color.FromArgb(220, 120, 190, 245), Color.FromArgb(220, 160, 230, 255))
                : (Color.FromArgb(active ? 150 : 110, 235, 242, 250), Color.FromArgb(active ? 110 : 80, 170, 200, 230), Color.FromArgb(active ? 130 : 90, 205, 225, 245));
        }
        using (var fill = new LinearGradientBrush(r, top, bottom, 90f)
        {
            InterpolationColors = new ColorBlend { Colors = [top, mid, bottom], Positions = [0f, 0.5f, 1f] },
        })
            g.FillPath(fill, path);
        using (var gloss = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, r.Height / 2 + 1), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(25, 255, 255, 255), 90f))
            g.FillRectangle(gloss, r.X + 1, r.Y + 1, r.Width - 2, r.Height / 2);
        using (var edge = new Pen(Color.FromArgb(220, 40, 50, 70), 1)) g.DrawPath(edge, path);
        using (var inner = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
            g.DrawRectangle(inner, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);

        // White glyph with a dark outline.
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f + 0.5f;
        using var outline = new Pen(Color.FromArgb(230, 30, 35, 50), 3.2f * k) { LineJoin = LineJoin.Round };
        using var white = new Pen(Color.White, 1.6f * k) { LineJoin = LineJoin.Round };
        switch (part)
        {
            case Part.Min:
                foreach (var pen in new[] { outline, white }) g.DrawLine(pen, cx - 4.5f * k, cy + 3 * k, cx + 4.5f * k, cy + 3 * k);
                break;
            case Part.Max:
                var box = new RectangleF(cx - 4.5f * k, cy - 3.5f * k, 9 * k, 7 * k);
                foreach (var pen in new[] { outline, white }) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                break;
            case Part.Close:
                float s = 3.8f * k;
                foreach (var pen in new[] { outline, white })
                {
                    g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                    g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
                }
                break;
        }
    }

    // ───────────────────────────── native ─────────────────────────────

    /// <summary>A GDI bitmap with the (premultiplied) alpha intact, selected into a memory DC.</summary>
    static IntPtr CreateAlphaDib(Bitmap bmp, out IntPtr memDc, out IntPtr old)
    {
        var bi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = bmp.Width,
            biHeight = -bmp.Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        memDc = CreateCompatibleDC(IntPtr.Zero);
        var hBmp = CreateDIBSection(memDc, ref bi, 0, out var bits, IntPtr.Zero, 0);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int rowBytes = bmp.Width * 4;
            var row = new byte[rowBytes];
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, rowBytes);
                Marshal.Copy(row, 0, bits + y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        old = SelectObject(memDc, hBmp);
        return hBmp;
    }

    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

    // ───────────────────────────── window messages ─────────────────────────────

    const int WM_NCHITTEST = 0x84, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
        HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE && PartAt(PointToClient(Cursor.Position)) != Part.None)
        {
            // Clicking the frame hands focus to the program window, and Windows then drops the
            // button-down meant for the frame. Don't activate on the caption buttons so the click lands.
            m.Result = MA_NOACTIVATE;
            return;
        }
        if (m.Msg == WM_NCHITTEST)
        {
            var screen = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
            var p = PointToClient(screen);
            int grip = (int)(7 * k);
            if (resizable && WindowState == FormWindowState.Normal)
            {
                bool l = p.X < grip, r = p.X >= Width - grip, t = p.Y < grip, b = p.Y >= Height - grip;
                int hit = (t, b, l, r) switch
                {
                    (true, _, true, _) => HTTOPLEFT,
                    (true, _, _, true) => HTTOPRIGHT,
                    (_, true, true, _) => HTBOTTOMLEFT,
                    (_, true, _, true) => HTBOTTOMRIGHT,
                    (true, _, _, _) => HTTOP,
                    (_, true, _, _) => HTBOTTOM,
                    (_, _, true, _) => HTLEFT,
                    (_, _, _, true) => HTRIGHT,
                    _ => 0,
                };
                if (hit != 0)
                {
                    m.Result = hit;
                    return;
                }
            }
            if (p.Y < C && PartAt(p) == Part.None)
            {
                m.Result = HTCAPTION; // native drag, double-click maximize and Aero Snap
                return;
            }
            m.Result = HTCLIENT;
            return;
        }
        base.WndProc(ref m);
    }
}
