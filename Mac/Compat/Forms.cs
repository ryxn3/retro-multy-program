// A small System.Windows.Forms look-alike on Avalonia, so the radio's window code runs on macOS.
// A Form becomes an Avalonia window; custom painting goes through the System.Drawing shim into a bitmap.
using System.ComponentModel;
using System.Drawing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using AvColor = Avalonia.Media.Color;
using AvBrush = Avalonia.Media.SolidColorBrush;
using AvRect = Avalonia.Rect;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace System.Windows.Forms
{
    public enum FormBorderStyle { None, FixedSingle, Fixed3D, FixedDialog, Sizable, FixedToolWindow, SizableToolWindow }
    public enum FormStartPosition { Manual, CenterScreen, WindowsDefaultLocation, WindowsDefaultBounds, CenterParent }
    public enum FormWindowState { Normal, Minimized, Maximized }
    public enum AutoScaleMode { None, Font, Dpi, Inherit }
    public enum BorderStyle { None, FixedSingle, Fixed3D }
    public enum FlatStyle { Flat, Popup, Standard, System }
    public enum DialogResult { None, OK, Cancel, Abort, Retry, Ignore, Yes, No, TryAgain = 10, Continue = 11 }
    public enum CloseReason { None, WindowsShutDown, MdiFormClosing, UserClosing, TaskManagerClosing, FormOwnerClosing, ApplicationExitCall }
    public enum MessageBoxButtons { OK, OKCancel, AbortRetryIgnore, YesNoCancel, YesNo, RetryCancel }
    public enum MessageBoxIcon { None = 0, Error = 16, Hand = 16, Stop = 16, Question = 32, Exclamation = 48, Warning = 48, Asterisk = 64, Information = 64 }

    [Flags]
    public enum ControlStyles
    {
        ContainerControl = 1, UserPaint = 2, Opaque = 4, ResizeRedraw = 16, FixedWidth = 32, FixedHeight = 64,
        StandardClick = 256, Selectable = 512, UserMouse = 1024, SupportsTransparentBackColor = 2048,
        StandardDoubleClick = 4096, AllPaintingInWmPaint = 8192, CacheText = 16384, EnableNotifyMessage = 32768,
        DoubleBuffer = 65536, OptimizedDoubleBuffer = 131072, UseTextForAccessibility = 262144,
    }

    [Flags]
    public enum MouseButtons { None = 0, Left = 0x100000, Right = 0x200000, Middle = 0x400000, XButton1 = 0x800000, XButton2 = 0x1000000 }

    [Flags]
    public enum DragDropEffects { None = 0, Copy = 1, Move = 2, Link = 4, Scroll = unchecked((int)0x80000000), All = unchecked((int)0x80000003) }

    [Flags]
    public enum Keys
    {
        None = 0, Back = 8, Tab = 9, Enter = 13, Return = 13, ShiftKey = 16, ControlKey = 17, Menu = 18, Pause = 19, CapsLock = 20,
        Escape = 27, Space = 32, PageUp = 33, Prior = 33, PageDown = 34, Next = 34, End = 35, Home = 36, Left = 37, Up = 38, Right = 39, Down = 40,
        Insert = 45, Delete = 46,
        D0 = 48, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        LWin = 91, RWin = 92, Apps = 93,
        NumPad0 = 96, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
        Multiply = 106, Add = 107, Separator = 108, Subtract = 109, Decimal = 110, Divide = 111,
        F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        VolumeMute = 173, VolumeDown = 174, VolumeUp = 175, MediaNextTrack = 176, MediaPreviousTrack = 177, MediaStop = 178, MediaPlayPause = 179,
        OemSemicolon = 186, Oem1 = 186, Oemplus = 187, Oemcomma = 188, OemMinus = 189, OemPeriod = 190, OemQuestion = 191, Oem2 = 191,
        Oemtilde = 192, Oem3 = 192, OemOpenBrackets = 219, Oem4 = 219, OemPipe = 220, Oem5 = 220, OemCloseBrackets = 221, Oem6 = 221,
        OemQuotes = 222, Oem7 = 222, OemBackslash = 226, Oem102 = 226,
        KeyCode = 0xFFFF, Shift = 0x10000, Control = 0x20000, Alt = 0x40000, Modifiers = unchecked((int)0xFFFF0000),
    }

    public struct Message
    {
        public IntPtr HWnd { get; set; }
        public int Msg { get; set; }
        public IntPtr WParam { get; set; }
        public IntPtr LParam { get; set; }
        public IntPtr Result { get; set; }
    }

    // ───────────────────────────── event args ─────────────────────────────

    public class PaintEventArgs(Graphics graphics, Rectangle clipRect) : EventArgs
    {
        public Graphics Graphics { get; } = graphics;
        public Rectangle ClipRectangle { get; } = clipRect;
    }

    public class MouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta) : EventArgs
    {
        public MouseButtons Button { get; } = button;
        public int Clicks { get; } = clicks;
        public int X { get; } = x;
        public int Y { get; } = y;
        public int Delta { get; } = delta;
        public Point Location => new(X, Y);
    }

    public class KeyEventArgs(Keys keyData) : EventArgs
    {
        public Keys KeyData { get; } = keyData;
        public Keys KeyCode => KeyData & Keys.KeyCode;
        public Keys Modifiers => KeyData & Keys.Modifiers;
        public bool Control => (KeyData & Keys.Control) != 0;
        public bool Shift => (KeyData & Keys.Shift) != 0;
        public bool Alt => (KeyData & Keys.Alt) != 0;
        public int KeyValue => (int)KeyCode;
        public bool Handled { get; set; }
        public bool SuppressKeyPress { get; set; }
    }

    public class KeyPressEventArgs(char keyChar) : EventArgs
    {
        public char KeyChar { get; set; } = keyChar;
        public bool Handled { get; set; }
    }

    public interface IDataObject
    {
        bool GetDataPresent(string format);
        object? GetData(string format);
    }

    public static class DataFormats
    {
        public const string FileDrop = "FileDrop";
        public const string Text = "Text";
        public const string UnicodeText = "UnicodeText";
    }

    sealed class FileDropData(string[] files) : IDataObject
    {
        public bool GetDataPresent(string format) => format == DataFormats.FileDrop && files.Length > 0;
        public object? GetData(string format) => format == DataFormats.FileDrop ? files : null;
    }

    public class DragEventArgs(IDataObject? data, int x, int y, DragDropEffects allowed) : EventArgs
    {
        public IDataObject? Data { get; } = data;
        public int X { get; } = x;
        public int Y { get; } = y;
        public DragDropEffects AllowedEffect { get; } = allowed;
        public DragDropEffects Effect { get; set; }
    }

    public class FormClosingEventArgs(CloseReason reason, bool cancel) : CancelEventArgs(cancel)
    {
        public CloseReason CloseReason { get; } = reason;
    }

    public class FormClosedEventArgs(CloseReason reason) : EventArgs
    {
        public CloseReason CloseReason { get; } = reason;
    }

    public class DpiChangedEventArgs(int oldDpi, int newDpi) : CancelEventArgs
    {
        public int DeviceDpiOld { get; } = oldDpi;
        public int DeviceDpiNew { get; } = newDpi;
    }

    public class LinkLabelLinkClickedEventArgs : EventArgs { }

    // ───────────────────────────── cursors, timer ─────────────────────────────

    public sealed class Cursor(StandardCursorType type)
    {
        internal readonly StandardCursorType Type = type;
        public static Point Position
        {
            get => Forms.Form.LastPointerScreen;
            set { }
        }
    }

    public static class Cursors
    {
        public static Cursor Default { get; } = new(StandardCursorType.Arrow);
        public static Cursor Arrow => Default;
        public static Cursor Hand { get; } = new(StandardCursorType.Hand);
        public static Cursor IBeam { get; } = new(StandardCursorType.Ibeam);
        public static Cursor SizeAll { get; } = new(StandardCursorType.SizeAll);
        public static Cursor SizeNS { get; } = new(StandardCursorType.SizeNorthSouth);
        public static Cursor SizeWE { get; } = new(StandardCursorType.SizeWestEast);
        public static Cursor WaitCursor { get; } = new(StandardCursorType.Wait);
        public static Cursor Cross { get; } = new(StandardCursorType.Cross);
    }

    /// <summary>A UI-thread timer, like WinForms' (it runs on Avalonia's dispatcher).</summary>
    public sealed class Timer : IDisposable
    {
        DispatcherTimer? timer;
        int interval = 100;

        public event EventHandler? Tick;

        public int Interval
        {
            get => interval;
            set
            {
                interval = Math.Max(1, value);
                if (timer != null) timer.Interval = TimeSpan.FromMilliseconds(interval);
            }
        }

        public bool Enabled
        {
            get => timer?.IsEnabled ?? false;
            set { if (value) Start(); else Stop(); }
        }

        public void Start()
        {
            timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(interval), DispatcherPriority.Background, (_, _) => Tick?.Invoke(this, EventArgs.Empty));
            timer.Interval = TimeSpan.FromMilliseconds(interval);
            timer.Start();
        }

        public void Stop() => timer?.Stop();
        public void Dispose() => Stop();
    }

    // ───────────────────────────── controls ─────────────────────────────

    public class Control : IDisposable
    {
        internal Avalonia.Controls.Control? View;
        Point location;
        Size size = new(100, 23);
        string text = "";
        bool visible = true;

        public Control? Parent { get; internal set; }
        public ControlCollection Controls { get; }
        public string Name { get; set; } = "";
        public object? Tag { get; set; }
        public bool AutoSize { get; set; }
        public bool Enabled { get; set; } = true;
        public Color BackColor { get; set; } = Color.FromArgb(240, 240, 240);
        public Color ForeColor { get; set; } = Color.Black;
        public virtual Font Font { get; set; } = new("Segoe UI", 9f);
        public virtual Cursor Cursor { get; set; } = Cursors.Default;

        public Control() => Controls = new ControlCollection(this);

        public event EventHandler? Click;
        public event EventHandler? TextChanged;

        public virtual string Text
        {
            get => text;
            set
            {
                text = value ?? "";
                OnTextChanged(EventArgs.Empty);
            }
        }

        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                if (View != null) View.IsVisible = value;
            }
        }

        public Point Location
        {
            get => location;
            set { location = value; Sync(); }
        }

        public virtual Size Size
        {
            get => size;
            set { size = value; Sync(); }
        }

        public int Left { get => location.X; set => Location = new Point(value, location.Y); }
        public int Top { get => location.Y; set => Location = new Point(location.X, value); }
        public int Width { get => Size.Width; set => Size = new Size(value, Size.Height); }
        public int Height { get => Size.Height; set => Size = new Size(Size.Width, value); }
        public int Right => Left + Width;
        public int Bottom => Top + Height;
        public Rectangle Bounds { get => new(Location, Size); set { Location = value.Location; Size = value.Size; } }

        protected virtual void OnTextChanged(EventArgs e) => TextChanged?.Invoke(this, e);
        protected virtual void OnClick(EventArgs e) => Click?.Invoke(this, e);
        internal void RaiseClick() => OnClick(EventArgs.Empty);

        internal virtual Avalonia.Controls.Control CreateView() => new Avalonia.Controls.Panel();

        internal void Sync()
        {
            if (View == null) return;
            Canvas.SetLeft(View, location.X);
            Canvas.SetTop(View, location.Y);
            if (!AutoSize)
            {
                View.Width = size.Width;
                View.Height = size.Height;
            }
        }

        public void Focus() => View?.Focus();
        public virtual void Invalidate() { }
        public void Invalidate(Rectangle r) => Invalidate();
        public void Invalidate(Region r) => Invalidate();
        public void Refresh() => Invalidate();
        public void Update() { }
        public void SuspendLayout() { }
        public void ResumeLayout(bool perform = true) { }
        public void PerformLayout() { }
        public void BringToFront() { }
        public void SendToBack() { }
        public virtual void Dispose() { }

        public sealed class ControlCollection(Control owner) : List<Control>
        {
            public new void Add(Control c)
            {
                base.Add(c);
                c.Parent = owner;
            }

            public new void AddRange(IEnumerable<Control> cs)
            {
                foreach (var c in cs) Add(c);
            }

            public void AddRange(Control[] cs) => AddRange((IEnumerable<Control>)cs);
        }
    }

    internal static class Av
    {
        public static AvBrush B(Color c) => new(AvColor.FromArgb(c.A, c.R, c.G, c.B));
        public static double FontPx(Font f) => f.SizeInPoints * 96 / 72.0;
    }

    public class Label : Control
    {
        internal override Avalonia.Controls.Control CreateView()
        {
            var t = new TextBlock
            {
                Text = Text,
                TextWrapping = AutoSize ? Avalonia.Media.TextWrapping.NoWrap : Avalonia.Media.TextWrapping.Wrap,
                FontSize = Av.FontPx(Font),
                Foreground = Av.B(ForeColor),
            };
            TextChanged += (_, _) => t.Text = Text;
            return t;
        }
    }

    public class LinkLabel : Label
    {
        public Color LinkColor { get; set; } = Color.FromArgb(0, 102, 204);
        public event EventHandler<LinkLabelLinkClickedEventArgs>? LinkClicked;

        internal override Avalonia.Controls.Control CreateView()
        {
            var t = new TextBlock
            {
                Text = Text,
                FontSize = Av.FontPx(Font),
                Foreground = Av.B(LinkColor),
                TextDecorations = Avalonia.Media.TextDecorations.Underline,
                Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand),
            };
            t.PointerReleased += (_, _) => LinkClicked?.Invoke(this, new LinkLabelLinkClickedEventArgs());
            return t;
        }
    }

    public class TextBox : Control
    {
        Avalonia.Controls.TextBox? box;
        string pending = "";

        public bool ReadOnly { get; set; }
        public bool UseSystemPasswordChar { get; set; }
        public char PasswordChar { get; set; }
        public bool Multiline { get; set; }
        public BorderStyle BorderStyle { get; set; }
        public string PlaceholderText { get; set; } = "";

        public override string Text
        {
            get => box?.Text ?? pending;
            set
            {
                pending = value ?? "";
                if (box != null) box.Text = pending;
                OnTextChanged(EventArgs.Empty);
            }
        }

        internal override Avalonia.Controls.Control CreateView()
        {
            box = new Avalonia.Controls.TextBox
            {
                Text = pending,
                IsReadOnly = ReadOnly,
                PasswordChar = UseSystemPasswordChar ? '•' : PasswordChar,
                AcceptsReturn = Multiline,
                FontSize = Av.FontPx(Font),
                Watermark = PlaceholderText,
                MinHeight = 0,
                Padding = new Thickness(6, 3),
            };
            box.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
            return box;
        }
    }

    public class Button : Control
    {
        public DialogResult DialogResult { get; set; }
        public FlatStyle FlatStyle { get; set; }

        internal override Avalonia.Controls.Control CreateView()
        {
            var b = new Avalonia.Controls.Button
            {
                Content = Text,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = Av.FontPx(Font),
                Padding = new Thickness(4, 2),
            };
            b.Click += (_, _) => PerformClick();
            return b;
        }

        public void PerformClick()
        {
            var form = FindForm();
            if (DialogResult != DialogResult.None && form != null) form.PendingResult = DialogResult;
            OnClick(EventArgs.Empty);
            form?.CommitPendingResult();
        }

        Form? FindForm()
        {
            var p = Parent;
            while (p != null && p is not Form) p = p.Parent;
            return p as Form;
        }
    }

    // ───────────────────────────── the form ─────────────────────────────

    public class Form : Control
    {
        HostWindow? window;
        FormBorderStyle borderStyle = FormBorderStyle.Sizable;
        Size clientSize = new(300, 300);
        Region? region;
        bool topMost;
        string title = "";
        ControlStyles styles;
        bool shown, closing;
        DialogResult dialogResult;
        internal DialogResult? PendingResult;

        public Form()
        {
            BackColor = Color.FromArgb(240, 240, 240);
        }

        public event EventHandler? Load;
        public event EventHandler? Shown;
        public event FormClosingEventHandler? FormClosing;
        public event FormClosedEventHandler? FormClosed;
        public event EventHandler? Activated;
        public event EventHandler? Deactivate;
        public event EventHandler? Resize;
        public event EventHandler? SizeChanged;
        public event EventHandler? LocationChanged;
        public event PaintEventHandler? Paint;
        public event MouseEventHandler? MouseDown;
        public event MouseEventHandler? MouseUp;
        public event MouseEventHandler? MouseMove;
        public event MouseEventHandler? MouseWheel;
        public event MouseEventHandler? MouseDoubleClick;
        public event KeyEventHandler? KeyDown;
        public event KeyPressEventHandler? KeyPress;

        public FormBorderStyle FormBorderStyle
        {
            get => borderStyle;
            set { borderStyle = value; window?.ApplyChrome(); }
        }

        public FormStartPosition StartPosition { get; set; } = FormStartPosition.WindowsDefaultLocation;
        public AutoScaleMode AutoScaleMode { get; set; } = AutoScaleMode.Font;
        public SizeF AutoScaleDimensions { get; set; }
        public bool AllowDrop { get; set; }
        public bool KeyPreview { get; set; }
        public bool MaximizeBox { get; set; } = true;
        public bool MinimizeBox { get; set; } = true;
        public bool ShowInTaskbar { get; set; } = true;
        public bool ShowIcon { get; set; } = true;
        public bool DoubleBuffered { get; set; }
        public Form? Owner { get; set; }
        public Button? AcceptButton { get; set; }
        public Button? CancelButton { get; set; }
        public Size MinimumSize { get; set; }
        public Size MaximumSize { get; set; }
        public IntPtr Handle => IntPtr.Zero;
        public bool IsHandleCreated => window != null;
        public bool IsDisposed { get; private set; }
        public bool InvokeRequired => !Dispatcher.UIThread.CheckAccess();
        public bool Modal { get; private set; }

        /// <summary>WinForms coordinates are real pixels when the form does its own scaling (AutoScaleMode.None), else 96-dpi units.</summary>
        internal bool PixelUnits => AutoScaleMode == AutoScaleMode.None;
        internal double Scale => window?.RenderScaling ?? PrimaryScaling;
        internal double UnitScale => PixelUnits ? Scale : 1;

        internal static double PrimaryScaling = 1;
        internal static Point LastPointerScreen;

        public int DeviceDpi => (int)Math.Round(96 * (PixelUnits ? Scale : 1));

        public override string Text
        {
            get => title;
            set
            {
                title = value ?? "";
                if (window != null) window.Title = title;
            }
        }

        public Size ClientSize
        {
            get => clientSize;
            set
            {
                if (value == clientSize) return;
                clientSize = value;
                window?.ApplySize();
                OnResize(EventArgs.Empty);
                SizeChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public override Size Size
        {
            get => clientSize;
            set => ClientSize = value;
        }

        public Rectangle ClientRectangle => new(Point.Empty, clientSize);

        public new Point Location
        {
            get => window == null ? Point.Empty : new Point(window.Position.X, window.Position.Y);
            set { if (window != null) window.Position = new PixelPoint(value.X, value.Y); }
        }

        public Region? Region
        {
            get => region;
            set
            {
                region = value;
                window?.ApplyChrome();
                Invalidate();
            }
        }

        public bool TopMost
        {
            get => topMost;
            set
            {
                topMost = value;
                if (window != null) window.Topmost = value;
            }
        }

        public FormWindowState WindowState
        {
            get => window?.WindowState switch { Avalonia.Controls.WindowState.Minimized => FormWindowState.Minimized, Avalonia.Controls.WindowState.Maximized => FormWindowState.Maximized, _ => FormWindowState.Normal };
            set { if (window != null) window.WindowState = value switch { FormWindowState.Minimized => Avalonia.Controls.WindowState.Minimized, FormWindowState.Maximized => Avalonia.Controls.WindowState.Maximized, _ => Avalonia.Controls.WindowState.Normal }; }
        }

        public override Cursor Cursor
        {
            get => base.Cursor;
            set
            {
                if (base.Cursor == value) return;
                base.Cursor = value;
                if (window != null) window.Cursor = new Avalonia.Input.Cursor(value.Type);
            }
        }

        public bool Capture
        {
            get => window?.CapturedPointer != null;
            set { if (!value) window?.ReleasePointer(); }
        }

        public DialogResult DialogResult
        {
            get => dialogResult;
            set
            {
                dialogResult = value;
                if (Modal && value != DialogResult.None) Close();
            }
        }

        internal void CommitPendingResult()
        {
            if (PendingResult is { } r && r != DialogResult.None) DialogResult = r;
            PendingResult = null;
        }

        protected void SetStyle(ControlStyles flags, bool value)
        {
            if (value) styles |= flags; else styles &= ~flags;
        }

        internal bool UserPaint => (styles & ControlStyles.UserPaint) != 0;

        public override void Invalidate() => window?.RequestPaint();

        public IAsyncResult? BeginInvoke(Action action)
        {
            Dispatcher.UIThread.Post(action);
            return null;
        }

        public IAsyncResult? BeginInvoke(Delegate method, params object?[] args)
        {
            Dispatcher.UIThread.Post(() => method.DynamicInvoke(args));
            return null;
        }

        public void Invoke(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Invoke(action);
        }

        public T Invoke<T>(Func<T> f) => Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.Invoke(f);

        public Point PointToClient(Point screen)
        {
            if (window == null) return screen;
            var p = window.PointToClient(new PixelPoint(screen.X, screen.Y));
            return new Point((int)(p.X * UnitScale), (int)(p.Y * UnitScale));
        }

        public Point PointToScreen(Point client)
        {
            if (window == null) return client;
            var p = window.PointToScreen(new Avalonia.Point(client.X / UnitScale, client.Y / UnitScale));
            return new Point(p.X, p.Y);
        }

        /// <summary>Starts moving the window with the mouse (the macOS stand-in for WM_NCLBUTTONDOWN/HTCAPTION).</summary>
        public void BeginWindowDrag() => window?.BeginDrag();

        public void Activate() => window?.Activate();
        public void BringToFront(bool _) => window?.Activate();

        public void Show()
        {
            EnsureWindow();
            window!.Show();
        }

        public void Show(Form owner)
        {
            Owner = owner;
            Show();
        }

        public void Hide() => window?.Hide();

        public void Close()
        {
            if (window == null || closing) return;
            window.Close();
        }

        public DialogResult ShowDialog() => ShowDialog(Owner);

        /// <summary>Runs the dialog modally: a nested dispatcher loop runs until it closes.</summary>
        public DialogResult ShowDialog(Form? owner)
        {
            Owner = owner;
            Modal = true;
            dialogResult = DialogResult.None;
            EnsureWindow();
            using var done = new CancellationTokenSource();
            window!.Closed += (_, _) => done.Cancel();
            if (owner?.window != null) _ = window.ShowDialog(owner.window);
            else window.Show();
            Dispatcher.UIThread.MainLoop(done.Token);
            Modal = false;
            if (dialogResult == DialogResult.None) dialogResult = DialogResult.Cancel;
            return dialogResult;
        }

        internal HostWindow EnsureWindow()
        {
            if (window != null) return window;
            window = new HostWindow(this);
            OnHandleCreated(EventArgs.Empty);
            OnLoad(EventArgs.Empty);
            window.ApplySize();
            window.PlaceAtStart();
            return window;
        }

        internal HostWindow? Window => window;

        // ─── overridable hooks (same names as WinForms) ───

        protected virtual void OnHandleCreated(EventArgs e) { }
        protected virtual void OnLoad(EventArgs e) => Load?.Invoke(this, e);
        protected virtual void OnShown(EventArgs e) => Shown?.Invoke(this, e);
        protected virtual void OnActivated(EventArgs e) => Activated?.Invoke(this, e);
        protected virtual void OnDeactivate(EventArgs e) => Deactivate?.Invoke(this, e);
        protected virtual void OnResize(EventArgs e) => Resize?.Invoke(this, e);
        protected virtual void OnDpiChanged(DpiChangedEventArgs e) { }
        protected virtual void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);
        protected virtual void OnPaint(PaintEventArgs e) => Paint?.Invoke(this, e);
        protected virtual void OnMouseDown(MouseEventArgs e) => MouseDown?.Invoke(this, e);
        protected virtual void OnMouseUp(MouseEventArgs e) => MouseUp?.Invoke(this, e);
        protected virtual void OnMouseMove(MouseEventArgs e) => MouseMove?.Invoke(this, e);
        protected virtual void OnMouseWheel(MouseEventArgs e) => MouseWheel?.Invoke(this, e);
        protected virtual void OnMouseDoubleClick(MouseEventArgs e) => MouseDoubleClick?.Invoke(this, e);
        protected virtual void OnMouseLeave(EventArgs e) { }
        protected virtual void OnMouseEnter(EventArgs e) { }
        protected virtual void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);
        protected virtual void OnKeyUp(KeyEventArgs e) { }
        protected virtual void OnKeyPress(KeyPressEventArgs e) => KeyPress?.Invoke(this, e);
        protected virtual bool ProcessCmdKey(ref Message msg, Keys keyData) => false;
        protected virtual void OnDragEnter(DragEventArgs e) { }
        protected virtual void OnDragOver(DragEventArgs e) { }
        protected virtual void OnDragDrop(DragEventArgs e) { }
        protected virtual void OnDragLeave(EventArgs e) { }
        protected virtual void OnFormClosing(FormClosingEventArgs e) => FormClosing?.Invoke(this, e);
        protected virtual void OnFormClosed(FormClosedEventArgs e) => FormClosed?.Invoke(this, e);

        // ─── called by the host window ───

        internal void RaiseShown()
        {
            if (shown) return;
            shown = true;
            OnShown(EventArgs.Empty);
        }

        internal void RaisePaint(Graphics g)
        {
            var e = new PaintEventArgs(g, ClientRectangle);
            OnPaintBackground(e);
            OnPaint(e);
        }

        internal void RaiseDpiChanged(int oldDpi, int newDpi) => OnDpiChanged(new DpiChangedEventArgs(oldDpi, newDpi));
        internal void RaiseMouseDown(MouseEventArgs e) { OnMouseDown(e); if (e.Clicks == 2) OnMouseDoubleClick(e); }
        internal void RaiseMouseUp(MouseEventArgs e) => OnMouseUp(e);
        internal void RaiseMouseMove(MouseEventArgs e) => OnMouseMove(e);
        internal void RaiseMouseWheel(MouseEventArgs e) => OnMouseWheel(e);
        internal void RaiseMouseLeave() => OnMouseLeave(EventArgs.Empty);
        internal void RaiseActivated(bool on) { if (on) OnActivated(EventArgs.Empty); else OnDeactivate(EventArgs.Empty); }
        internal void RaiseLocationChanged() => LocationChanged?.Invoke(this, EventArgs.Empty);
        internal void RaiseKeyPress(char c) => OnKeyPress(new KeyPressEventArgs(c));
        internal void RaiseDragEnter(DragEventArgs e) => OnDragEnter(e);
        internal void RaiseDragOver(DragEventArgs e) => OnDragOver(e);
        internal void RaiseDragDrop(DragEventArgs e) => OnDragDrop(e);

        internal bool RaiseKeyDown(Keys keyData)
        {
            var msg = new Message();
            if (ProcessCmdKey(ref msg, keyData)) return true;
            if (Modal && (keyData & Keys.KeyCode) == Keys.Enter && AcceptButton != null) { AcceptButton.PerformClick(); return true; }
            if (Modal && (keyData & Keys.KeyCode) == Keys.Escape && CancelButton != null) { CancelButton.PerformClick(); return true; }
            var e = new KeyEventArgs(keyData);
            OnKeyDown(e);
            return e.Handled;
        }

        /// <summary>Returns true when the form wants to stay open.</summary>
        internal bool RaiseClosing()
        {
            closing = true;
            var e = new FormClosingEventArgs(CloseReason.UserClosing, false);
            OnFormClosing(e);
            closing = false;
            return e.Cancel;
        }

        internal void RaiseClosed()
        {
            IsDisposed = true;
            OnFormClosed(new FormClosedEventArgs(CloseReason.UserClosing));
            Dispose();
        }
    }

    public delegate void PaintEventHandler(object? sender, PaintEventArgs e);
    public delegate void MouseEventHandler(object? sender, MouseEventArgs e);
    public delegate void KeyEventHandler(object? sender, KeyEventArgs e);
    public delegate void KeyPressEventHandler(object? sender, KeyPressEventArgs e);
    public delegate void FormClosingEventHandler(object? sender, FormClosingEventArgs e);
    public delegate void FormClosedEventHandler(object? sender, FormClosedEventArgs e);

    /// <summary>The Avalonia window behind a Form: paints it into a bitmap and forwards input.</summary>
    internal sealed class HostWindow : Window
    {
        readonly Form form;
        readonly Canvas root = new();
        readonly Surface surface;
        WriteableBitmap? bitmap;
        bool paintQueued, opened;
        readonly System.Diagnostics.Stopwatch sinceOpen = new();
        static string? DumpPath => dumpOnce;
        static string? dumpOnce = Environment.GetEnvironmentVariable("RETRO_WINDOW_DUMP");
        static readonly string? _unused = Environment.GetEnvironmentVariable("RETRO_WINDOW_DUMP");
        int lastDpi;
        PointerPressedEventArgs? lastPress;
        IPointer? pointer;

        public HostWindow(Form form)
        {
            this.form = form;
            Title = form.Text;
            Topmost = form.TopMost;
            CanResize = form.FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = form.ShowInTaskbar;
            surface = new Surface(this);
            root.Children.Add(surface);
            foreach (var c in form.Controls) AddView(c);
            Content = root;
            ApplyChrome();

            DragDrop.SetAllowDrop(this, form.AllowDrop);
            AddHandler(DragDrop.DragEnterEvent, OnDrag);
            AddHandler(DragDrop.DragOverEvent, OnDrag);
            AddHandler(DragDrop.DropEvent, OnDrop);

            surface.PointerPressed += OnPressed;
            surface.PointerReleased += OnReleased;
            surface.PointerMoved += OnMoved;
            surface.PointerExited += (_, _) => form.RaiseMouseLeave();
            surface.PointerWheelChanged += OnWheel;
            AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(TextInputEvent, OnText, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            Opened += (_, _) =>
            {
                opened = true;
                sinceOpen.Start();
                lastDpi = form.DeviceDpi;
                ApplySize();
                RequestPaint();
                Dispatcher.UIThread.Post(form.RaiseShown);
            };
            ScalingChanged += (_, _) =>
            {
                int dpi = form.DeviceDpi;
                if (dpi != lastDpi)
                {
                    int old = lastDpi;
                    lastDpi = dpi;
                    form.RaiseDpiChanged(old, dpi);
                    ApplySize();
                }
            };
            Activated += (_, _) => form.RaiseActivated(true);
            Deactivated += (_, _) => form.RaiseActivated(false);
            PositionChanged += (_, _) => form.RaiseLocationChanged();
            Closing += (_, e) =>
            {
                if (form.RaiseClosing()) e.Cancel = true;
            };
            Closed += (_, _) => form.RaiseClosed();
        }

        void AddView(Forms.Control c)
        {
            var v = c.CreateView();
            c.View = v;
            v.IsVisible = c.Visible;
            if (v is Avalonia.Controls.Primitives.TemplatedControl tc)
            {
                tc.Background = Av.B(c.BackColor);
                tc.Foreground = Av.B(c.ForeColor);
            }
            c.Sync();
            root.Children.Add(v);
        }

        public void ApplyChrome()
        {
            bool shaped = form.Region != null || form.FormBorderStyle == FormBorderStyle.None;
            SystemDecorations = form.FormBorderStyle == FormBorderStyle.None ? SystemDecorations.None : SystemDecorations.Full;
            if (form.Region != null)
            {
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
                Background = Avalonia.Media.Brushes.Transparent;
            }
            else
            {
                Background = Av.B(form.BackColor);
            }
            ExtendClientAreaToDecorationsHint = false;
            _ = shaped;
        }

        public void ApplySize()
        {
            double k = form.UnitScale;
            Width = form.ClientSize.Width / k;
            Height = form.ClientSize.Height / k;
            root.Width = Width;
            root.Height = Height;
            surface.Width = Width;
            surface.Height = Height;
        }

        /// <summary>Centres the window before it's shown, like StartPosition does.</summary>
        public void PlaceAtStart()
        {
            if (form.StartPosition == FormStartPosition.CenterParent && form.Owner?.Window is { } ow)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                return;
            }
            if (form.StartPosition == FormStartPosition.Manual) return;
            var screen = Screens.Primary;
            if (screen == null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }
            var wa = screen.WorkingArea;
            int w = (int)(Width * screen.Scaling), h = (int)(Height * screen.Scaling);
            Position = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2);
        }

        public void RequestPaint()
        {
            if (paintQueued || !opened) return;
            paintQueued = true;
            Dispatcher.UIThread.Post(Paint, DispatcherPriority.Render);
        }

        /// <summary>Renders the form into a bitmap at full pixel resolution (Retina included).</summary>
        void Paint()
        {
            paintQueued = false;
            if (form.IsDisposed) return;
            double s = RenderScaling;
            int pw = Math.Max(1, (int)Math.Ceiling(form.ClientSize.Width / form.UnitScale * s));
            int ph = Math.Max(1, (int)Math.Ceiling(form.ClientSize.Height / form.UnitScale * s));
            if (bitmap == null || bitmap.PixelSize.Width != pw || bitmap.PixelSize.Height != ph)
            {
                bitmap?.Dispose();
                bitmap = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96 * s, 96 * s), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
            using (var fb = bitmap.Lock())
            {
                var info = new SKImageInfo(fb.Size.Width, fb.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var skSurface = SKSurface.Create(info, fb.Address, fb.RowBytes);
                var canvas = skSurface.Canvas;
                canvas.Clear(form.Region != null ? SKColors.Transparent : new SKColor(form.BackColor.R, form.BackColor.G, form.BackColor.B));
                using (var g = new Graphics(canvas, false))
                {
                    // Pixel-unit forms draw 1:1; others draw in 96-dpi units, scaled up here.
                    if (!form.PixelUnits) g.ScaleTransform((float)s, (float)s);
                    try { form.RaisePaint(g); }
                    catch (Exception ex) { Console.Error.WriteLine(ex); }
                }
                if (form.Region?.Path is { } shape)
                {
                    // Cut the window to its shape (Form.Region).
                    canvas.Save();
                    canvas.ResetMatrix();
                    if (!form.PixelUnits) canvas.Scale((float)s);
                    canvas.ClipPath(shape, SKClipOperation.Difference, true);
                    canvas.Clear(SKColors.Transparent);
                    canvas.Restore();
                }
                canvas.Flush();
                // Debug aid: RETRO_WINDOW_DUMP=file.png saves what the window shows after a few seconds.
                if (DumpPath != null && sinceOpen.Elapsed.TotalSeconds > 3 && form == Application.MainForm)
                {
                    using (var img = skSurface.Snapshot())
                    using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                    using (var fs = File.Create(DumpPath))
                        data.SaveTo(fs);
                    dumpOnce = null;
                    Dispatcher.UIThread.Post(() => Environment.Exit(0));
                }
            }
            surface.Bitmap = bitmap;
            surface.InvalidateVisual();
        }

        // ─── input ───

        (int X, int Y) ToForm(Avalonia.Point p) => ((int)Math.Round(p.X * form.UnitScale), (int)Math.Round(p.Y * form.UnitScale));

        static MouseButtons Buttons(PointerPointProperties p, KeyModifiers mods)
        {
            var b = MouseButtons.None;
            if (p.IsLeftButtonPressed) b |= (mods & KeyModifiers.Control) != 0 && OperatingSystem.IsMacOS() ? MouseButtons.Right : MouseButtons.Left;
            if (p.IsRightButtonPressed) b |= MouseButtons.Right;
            if (p.IsMiddleButtonPressed) b |= MouseButtons.Middle;
            return b;
        }

        bool InsideShape(int x, int y) => form.Region?.Path is not { } shape || shape.Contains(x, y);

        void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(surface);
            var (x, y) = ToForm(pt.Position);
            if (!InsideShape(x, y)) return;
            lastPress = e;
            pointer = e.Pointer;
            RememberScreen(e);
            var b = Buttons(pt.Properties, e.KeyModifiers);
            if (b == MouseButtons.None) b = MouseButtons.Left;
            form.RaiseMouseDown(new MouseEventArgs(b, Math.Max(1, e.ClickCount), x, y, 0));
        }

        void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            var (x, y) = ToForm(e.GetPosition(surface));
            RememberScreen(e);
            var b = e.InitialPressMouseButton switch
            {
                MouseButton.Right => MouseButtons.Right,
                MouseButton.Middle => MouseButtons.Middle,
                _ => (e.KeyModifiers & KeyModifiers.Control) != 0 && OperatingSystem.IsMacOS() ? MouseButtons.Right : MouseButtons.Left,
            };
            form.RaiseMouseUp(new MouseEventArgs(b, 1, x, y, 0));
            pointer = null;
        }

        void OnMoved(object? sender, PointerEventArgs e)
        {
            var pt = e.GetCurrentPoint(surface);
            var (x, y) = ToForm(pt.Position);
            RememberScreen(e);
            form.RaiseMouseMove(new MouseEventArgs(Buttons(pt.Properties, e.KeyModifiers), 0, x, y, 0));
        }

        void OnWheel(object? sender, PointerWheelEventArgs e)
        {
            var (x, y) = ToForm(e.GetPosition(surface));
            form.RaiseMouseWheel(new MouseEventArgs(MouseButtons.None, 0, x, y, (int)Math.Round(e.Delta.Y * 120)));
        }

        void RememberScreen(PointerEventArgs e)
        {
            var p = this.PointToScreen(e.GetPosition(this));
            Form.LastPointerScreen = new Point(p.X, p.Y);
        }

        public IPointer? CapturedPointer => pointer?.Captured != null ? pointer : null;

        public void ReleasePointer()
        {
            pointer?.Capture(null);
            pointer = null;
        }

        public void BeginDrag()
        {
            if (lastPress != null)
            {
                try { BeginMoveDrag(lastPress); } catch (Exception) { }
            }
        }

        void OnKey(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            // Let text boxes in dialogs have their keys, except Enter/Escape for the dialog buttons.
            if (e.Source is Avalonia.Controls.TextBox && e.Key is not (Key.Enter or Key.Escape)) return;
            var keys = KeyMap.From(e.Key);
            if (keys == Keys.None) return;
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0) keys |= Keys.Shift;
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) keys |= Keys.Control;
            if ((e.KeyModifiers & KeyModifiers.Alt) != 0) keys |= Keys.Alt;
            if (form.RaiseKeyDown(keys)) e.Handled = true;
        }

        void OnText(object? sender, TextInputEventArgs e)
        {
            if (e.Source is Avalonia.Controls.TextBox || string.IsNullOrEmpty(e.Text)) return;
            foreach (char c in e.Text) form.RaiseKeyPress(c);
        }

        void OnDrag(object? sender, Avalonia.Input.DragEventArgs e)
        {
            var args = Args(e);
            if (e.RoutedEvent == DragDrop.DragEnterEvent) form.RaiseDragEnter(args);
            else form.RaiseDragOver(args);
            e.DragEffects = args.Effect.HasFlag(DragDropEffects.Copy) ? Avalonia.Input.DragDropEffects.Copy : Avalonia.Input.DragDropEffects.None;
        }

        void OnDrop(object? sender, Avalonia.Input.DragEventArgs e)
        {
            var args = Args(e);
            form.RaiseDragEnter(args);
            if (args.Effect != DragDropEffects.None) form.RaiseDragDrop(args);
        }

        Forms.DragEventArgs Args(Avalonia.Input.DragEventArgs e)
        {
            var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToArray() ?? [];
            var (x, y) = ToForm(e.GetPosition(surface));
            return new Forms.DragEventArgs(new FileDropData(files), x, y, DragDropEffects.Copy);
        }

        /// <summary>Shows the latest painted frame.</summary>
        sealed class Surface(HostWindow owner) : Avalonia.Controls.Control
        {
            public WriteableBitmap? Bitmap;

            public override void Render(Avalonia.Media.DrawingContext context)
            {
                if (Bitmap == null) return;
                double s = owner.RenderScaling;
                var size = new Avalonia.Size(Bitmap.PixelSize.Width / s, Bitmap.PixelSize.Height / s);
                context.DrawImage(Bitmap, new AvRect(Bitmap.Size), new AvRect(size));
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                base.OnPointerPressed(e);
                Focus();
            }
        }
    }

    internal static class KeyMap
    {
        public static Keys From(Key k) => k switch
        {
            Key.Enter => Keys.Enter,
            Key.Back => Keys.Back,
            Key.Escape => Keys.Escape,
            Key.Space => Keys.Space,
            Key.OemPlus => Keys.Oemplus,
            Key.OemComma => Keys.Oemcomma,
            Key.OemTilde => Keys.Oemtilde,
            Key.OemBackslash => Keys.OemBackslash,
            Key.MediaPlayPause => Keys.MediaPlayPause,
            Key.MediaNextTrack => Keys.MediaNextTrack,
            Key.MediaPreviousTrack => Keys.MediaPreviousTrack,
            Key.MediaStop => Keys.MediaStop,
            _ => Enum.TryParse<Keys>(k.ToString(), out var v) ? v : Keys.None,
        };
    }

    // ───────────────────────────── dialogs ─────────────────────────────

    internal static class Modal
    {
        public static TopLevel? Owner(Form? owner) =>
            owner?.Window ?? (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? Application.MainForm?.Window;

        /// <summary>Waits for a task on the UI thread by running a nested dispatcher loop.</summary>
        public static T Wait<T>(Task<T> task)
        {
            using var cts = new CancellationTokenSource();
            task.ContinueWith(_ => cts.Cancel(), TaskScheduler.Default);
            if (!task.IsCompleted) Dispatcher.UIThread.MainLoop(cts.Token);
            return task.GetAwaiter().GetResult();
        }
    }

    public abstract class CommonDialog : IDisposable
    {
        public DialogResult ShowDialog() => ShowDialog(null);
        public abstract DialogResult ShowDialog(Form? owner);
        public void Dispose() { }
    }

    public class OpenFileDialog : CommonDialog
    {
        public string Title { get; set; } = "Open";
        public string Filter { get; set; } = "";
        public int FilterIndex { get; set; } = 1;
        public bool Multiselect { get; set; }
        public string InitialDirectory { get; set; } = "";
        public string FileName { get; set; } = "";
        public string[] FileNames { get; private set; } = [];
        public bool CheckFileExists { get; set; } = true;
        public bool RestoreDirectory { get; set; }

        internal static List<FilePickerFileType> Types(string filter)
        {
            var list = new List<FilePickerFileType>();
            var parts = filter.Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                list.Add(new FilePickerFileType(parts[i]) { Patterns = patterns });
            }
            return list;
        }

        public override DialogResult ShowDialog(Form? owner)
        {
            var top = Modal.Owner(owner);
            if (top == null) return DialogResult.Cancel;
            var types = Types(Filter);
            if (FilterIndex > 1 && FilterIndex <= types.Count)
            {
                var first = types[FilterIndex - 1];
                types.RemoveAt(FilterIndex - 1);
                types.Insert(0, first);
            }
            var opts = new FilePickerOpenOptions { Title = Title, AllowMultiple = Multiselect, FileTypeFilter = types.Count > 0 ? types : null };
            var files = Modal.Wait(top.StorageProvider.OpenFilePickerAsync(opts));
            var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToArray();
            if (paths.Length == 0) return DialogResult.Cancel;
            FileNames = paths;
            FileName = paths[0];
            return DialogResult.OK;
        }
    }

    public class SaveFileDialog : CommonDialog
    {
        public string Title { get; set; } = "Save";
        public string Filter { get; set; } = "";
        public string FileName { get; set; } = "";
        public string DefaultExt { get; set; } = "";
        public string InitialDirectory { get; set; } = "";
        public bool OverwritePrompt { get; set; } = true;

        public override DialogResult ShowDialog(Form? owner)
        {
            var top = Modal.Owner(owner);
            if (top == null) return DialogResult.Cancel;
            var types = OpenFileDialog.Types(Filter);
            var opts = new FilePickerSaveOptions
            {
                Title = Title,
                SuggestedFileName = Path.GetFileName(FileName),
                DefaultExtension = DefaultExt,
                FileTypeChoices = types.Count > 0 ? types : null,
            };
            var file = Modal.Wait(top.StorageProvider.SaveFilePickerAsync(opts));
            var path = file?.TryGetLocalPath();
            if (path == null) return DialogResult.Cancel;
            FileName = path;
            return DialogResult.OK;
        }
    }

    public class FolderBrowserDialog : CommonDialog
    {
        public string Description { get; set; } = "";
        public bool UseDescriptionForTitle { get; set; }
        public string SelectedPath { get; set; } = "";
        public bool ShowNewFolderButton { get; set; } = true;

        public override DialogResult ShowDialog(Form? owner)
        {
            var top = Modal.Owner(owner);
            if (top == null) return DialogResult.Cancel;
            var folders = Modal.Wait(top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Description, AllowMultiple = false }));
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return DialogResult.Cancel;
            SelectedPath = path;
            return DialogResult.OK;
        }
    }

    public static class MessageBox
    {
        public static DialogResult Show(string text) => Show(null, text, "", MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(string text, string caption) => Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons) => Show(null, text, caption, buttons, MessageBoxIcon.None);
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(null, text, caption, buttons, icon);
        public static DialogResult Show(Form? owner, string text) => Show(owner, text, "", MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(Form? owner, string text, string caption) => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(Form? owner, string text, string caption, MessageBoxButtons buttons) => Show(owner, text, caption, buttons, MessageBoxIcon.None);

        /// <summary>A plain dark message window with the requested buttons.</summary>
        public static DialogResult Show(Form? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            DialogResult[] choices = buttons switch
            {
                MessageBoxButtons.OKCancel => [DialogResult.OK, DialogResult.Cancel],
                MessageBoxButtons.YesNo => [DialogResult.Yes, DialogResult.No],
                MessageBoxButtons.YesNoCancel => [DialogResult.Yes, DialogResult.No, DialogResult.Cancel],
                MessageBoxButtons.RetryCancel => [DialogResult.Retry, DialogResult.Cancel],
                MessageBoxButtons.AbortRetryIgnore => [DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore],
                _ => [DialogResult.OK],
            };
            var result = choices[^1] == DialogResult.Cancel || choices.Length == 1 ? choices[^1] : DialogResult.None;
            var win = new Window
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                MaxWidth = 520,
            };
            var panel = new Avalonia.Controls.StackPanel { Margin = new Thickness(22), Spacing = 18 };
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 460 });
            var row = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            foreach (var c in choices)
            {
                var b = new Avalonia.Controls.Button { Content = c.ToString(), MinWidth = 80, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
                b.Click += (_, _) => { result = c; win.Close(); };
                row.Children.Add(b);
            }
            panel.Children.Add(row);
            win.Content = panel;
            var top = Modal.Owner(owner) as Window;
            using var done = new CancellationTokenSource();
            win.Closed += (_, _) => done.Cancel();
            if (top != null) _ = win.ShowDialog(top); else win.Show();
            Dispatcher.UIThread.MainLoop(done.Token);
            return result == DialogResult.None ? choices[^1] : result;
        }
    }

    // ───────────────────────────── application ─────────────────────────────

    public static class Application
    {
        internal static Form? MainForm;
        static CancellationTokenSource? quit;

        /// <summary>Shows the main form and runs until it closes.</summary>
        public static void Run(Form form)
        {
            MainForm = form;
            quit = new CancellationTokenSource();
            form.FormClosed += (_, _) => quit.Cancel();
            form.Show();
            Dispatcher.UIThread.MainLoop(quit.Token);
        }

        public static void Exit() => quit?.Cancel();
        public static void DoEvents() => Dispatcher.UIThread.RunJobs();
        public static void EnableVisualStyles() { }
        public static void SetCompatibleTextRenderingDefault(bool value) { }
        public static string StartupPath => AppContext.BaseDirectory;
        public static string ExecutablePath => Environment.ProcessPath ?? "";
    }
}
