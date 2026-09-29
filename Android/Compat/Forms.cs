// A small System.Windows.Forms look-alike for Android, so the radio's window code runs on a phone.
// There is one Form (the radio). It is painted through the System.Drawing shim onto the app's view,
// touches arrive as mouse events, and everything runs on Android's UI thread.
using System.ComponentModel;
using System.Drawing;
using RetroRadio.Droid;

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

    public delegate void PaintEventHandler(object? sender, PaintEventArgs e);
    public delegate void MouseEventHandler(object? sender, MouseEventArgs e);
    public delegate void KeyEventHandler(object? sender, KeyEventArgs e);
    public delegate void KeyPressEventHandler(object? sender, KeyPressEventArgs e);
    public delegate void FormClosingEventHandler(object? sender, FormClosingEventArgs e);
    public delegate void FormClosedEventHandler(object? sender, FormClosedEventArgs e);

    // ───────────────────────────── cursors, timer ─────────────────────────────

    /// <summary>Phones have no mouse pointer; cursors are remembered and otherwise ignored.</summary>
    public sealed class Cursor
    {
        public static Point Position { get => Form.LastTouch; set { } }
    }

    public static class Cursors
    {
        public static Cursor Default { get; } = new();
        public static Cursor Arrow => Default;
        public static Cursor Hand { get; } = new();
        public static Cursor IBeam { get; } = new();
        public static Cursor SizeAll { get; } = new();
        public static Cursor SizeNS { get; } = new();
        public static Cursor SizeWE { get; } = new();
        public static Cursor WaitCursor { get; } = new();
        public static Cursor Cross { get; } = new();
    }

    /// <summary>A UI-thread timer, like WinForms' (it runs on Android's main looper).</summary>
    public sealed class Timer : IDisposable
    {
        int interval = 100, generation;
        bool enabled;

        public event EventHandler? Tick;

        public int Interval
        {
            get => interval;
            set => interval = Math.Max(1, value);
        }

        public bool Enabled
        {
            get => enabled;
            set { if (value) Start(); else Stop(); }
        }

        public void Start()
        {
            if (enabled) return;
            enabled = true;
            Schedule(++generation);
        }

        void Schedule(int gen) => Host.Current.PostDelayed(() =>
        {
            if (!enabled || gen != generation) return;
            Tick?.Invoke(this, EventArgs.Empty);
            if (enabled && gen == generation) Schedule(gen);
        }, interval);

        public void Stop()
        {
            enabled = false;
            generation++;
        }

        public void Dispose() => Stop();
    }

    // ───────────────────────────── controls ─────────────────────────────

    public class Control : IDisposable
    {
        public Control? Parent { get; internal set; }
        public string Name { get; set; } = "";
        public object? Tag { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Visible { get; set; } = true;
        public Color BackColor { get; set; } = Color.Black;
        public Color ForeColor { get; set; } = Color.White;
        public virtual Font Font { get; set; } = new("Segoe UI", 9f);
        public virtual Cursor Cursor { get; set; } = Cursors.Default;
        public virtual string Text { get; set; } = "";

        public void Focus() { }
        public virtual void Invalidate() { }
        public virtual void Invalidate(Rectangle r) => Invalidate();
        public void Invalidate(Region r) => Invalidate();
        public void Refresh() => Invalidate();
        public void Update() { }
        public void SuspendLayout() { }
        public void ResumeLayout(bool perform = true) { }
        public void PerformLayout() { }
        public virtual void Dispose() { }
    }

    // ───────────────────────────── the form ─────────────────────────────

    public class Form : Control
    {
        Size clientSize = new(300, 300);
        Region? region;
        ControlStyles styles;
        bool created, shown, closing;
        int dpi = 96;
        bool dirtyAll = true;
        Rectangle dirty;

        internal static Point LastTouch;

        public event EventHandler? Load;
        public event EventHandler? Shown;
        public event FormClosingEventHandler? FormClosing;
        public event FormClosedEventHandler? FormClosed;
        public event EventHandler? Resize;
        public event EventHandler? SizeChanged;
        public event PaintEventHandler? Paint;
        public event MouseEventHandler? MouseDown;
        public event MouseEventHandler? MouseUp;
        public event MouseEventHandler? MouseMove;
        public event MouseEventHandler? MouseWheel;
        public event MouseEventHandler? MouseDoubleClick;
        public event KeyEventHandler? KeyDown;
        public event KeyPressEventHandler? KeyPress;

        public FormBorderStyle FormBorderStyle { get; set; } = FormBorderStyle.Sizable;
        public FormStartPosition StartPosition { get; set; }
        public AutoScaleMode AutoScaleMode { get; set; } = AutoScaleMode.Font;
        public SizeF AutoScaleDimensions { get; set; }
        public bool AllowDrop { get; set; }
        public bool KeyPreview { get; set; }
        public bool MaximizeBox { get; set; } = true;
        public bool MinimizeBox { get; set; } = true;
        public bool ShowInTaskbar { get; set; } = true;
        public bool ShowIcon { get; set; } = true;
        public bool DoubleBuffered { get; set; }
        public bool TopMost { get; set; }
        public bool Capture { get; set; }
        public Size MinimumSize { get; set; }
        public Size MaximumSize { get; set; }
        public Point Location { get; set; }
        public IntPtr Handle => IntPtr.Zero;
        public bool IsHandleCreated => created;
        public bool IsDisposed { get; private set; }
        public bool InvokeRequired => !Host.Current.IsUiThread;

        /// <summary>The radio draws in real pixels; the Android view picks this so the radio fills the screen.</summary>
        public int DeviceDpi => dpi;

        public Size ClientSize
        {
            get => clientSize;
            set
            {
                if (value == clientSize) return;
                clientSize = value;
                OnResize(EventArgs.Empty);
                SizeChanged?.Invoke(this, EventArgs.Empty);
                if (created) Host.Current.FormResized();
                Invalidate();
            }
        }

        public Size Size
        {
            get => clientSize;
            set => ClientSize = value;
        }

        public Rectangle ClientRectangle => new(Point.Empty, clientSize);
        public int Left { get => Location.X; set => Location = new Point(value, Location.Y); }
        public int Top { get => Location.Y; set => Location = new Point(Location.X, value); }
        public int Width { get => clientSize.Width; set => ClientSize = new Size(value, clientSize.Height); }
        public int Height { get => clientSize.Height; set => ClientSize = new Size(clientSize.Width, value); }
        public int Right => Left + Width;
        public int Bottom => Top + Height;
        public Rectangle Bounds => new(Location, clientSize);

        public Region? Region
        {
            get => region;
            set { region = value; Invalidate(); }
        }

        /// <summary>Minimizing sends the app to the background (the music keeps playing).</summary>
        public FormWindowState WindowState
        {
            get => FormWindowState.Normal;
            set { if (value == FormWindowState.Minimized) Host.Current.Minimize(); }
        }

        protected void SetStyle(ControlStyles flags, bool value)
        {
            if (value) styles |= flags; else styles &= ~flags;
        }

        public override void Invalidate()
        {
            dirtyAll = true;
            if (created) Host.Current.RequestPaint();
        }

        /// <summary>Only this part changed: the next frame repaints just that (a phone has no power to spare).</summary>
        public override void Invalidate(Rectangle r)
        {
            if (!dirtyAll) dirty = dirty.IsEmpty ? r : Rectangle.Union(dirty, r);
            if (created) Host.Current.RequestPaint();
        }

        /// <summary>What needs repainting since the last frame: everything, or one rectangle (empty = nothing).</summary>
        internal (bool All, Rectangle Rect) TakeDirty()
        {
            var result = (dirtyAll, Rectangle.Intersect(dirty, ClientRectangle));
            dirtyAll = false;
            dirty = Rectangle.Empty;
            return result;
        }

        public IAsyncResult? BeginInvoke(Action action)
        {
            Host.Current.Post(action);
            return null;
        }

        public IAsyncResult? BeginInvoke(Delegate method, params object?[] args)
        {
            Host.Current.Post(() => method.DynamicInvoke(args));
            return null;
        }

        public void Invoke(Action action)
        {
            if (Host.Current.IsUiThread)
            {
                action();
                return;
            }
            using var done = new ManualResetEventSlim();
            Exception? error = null;
            Host.Current.Post(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait();
            if (error != null) throw error;
        }

        public T Invoke<T>(Func<T> f)
        {
            T result = default!;
            Invoke(() => { result = f(); });
            return result;
        }

        // The form fills the view, so client and "screen" coordinates are the same.
        public Point PointToClient(Point screen) => screen;
        public Point PointToScreen(Point client) => client;

        public void BeginWindowDrag() { }
        public void Activate() { }
        public void Show() { }
        public void Hide() => Host.Current.Minimize();

        public void Close()
        {
            if (closing || IsDisposed) return;
            if (RaiseClosing()) return;
            RaiseClosed();
            Host.Current.Quit();
        }

        // ─── overridable hooks (same names as WinForms) ───

        protected virtual void OnHandleCreated(EventArgs e) { }
        protected virtual void OnLoad(EventArgs e) => Load?.Invoke(this, e);
        protected virtual void OnShown(EventArgs e) => Shown?.Invoke(this, e);
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

        /// <summary>How a touch starting here behaves: pressed at once (keys, knobs) or scrolled (menus on the display).</summary>
        protected internal virtual TouchMode TouchModeAt(Point p) => TouchMode.Press;

        // ─── called by the Android view ───

        /// <summary>Creates the "window": the same order of events WinForms uses when a form is first shown.</summary>
        internal void Create(int screenDpi)
        {
            if (created) return;
            dpi = screenDpi;
            OnHandleCreated(EventArgs.Empty);
            OnLoad(EventArgs.Empty);
            created = true;
            Host.Current.FormResized();
            Host.Current.Post(() =>
            {
                if (shown) return;
                shown = true;
                OnShown(EventArgs.Empty);
            });
        }

        /// <summary>Changes the drawing scale, like moving the window to a screen with another DPI.</summary>
        internal void SetDpi(int newDpi)
        {
            if (newDpi == dpi || newDpi <= 0) return;
            int old = dpi;
            dpi = newDpi;
            OnDpiChanged(new DpiChangedEventArgs(old, newDpi));
            Invalidate();
        }

        internal void RaisePaint(Graphics g) => RaisePaint(g, ClientRectangle);

        internal void RaisePaint(Graphics g, Rectangle clip)
        {
            var e = new PaintEventArgs(g, clip);
            OnPaintBackground(e);
            OnPaint(e);
        }

        internal void RaiseMouseDown(MouseEventArgs e)
        {
            LastTouch = e.Location;
            OnMouseDown(e);
            if (e.Clicks == 2) OnMouseDoubleClick(e);
        }

        internal void RaiseMouseUp(MouseEventArgs e) { LastTouch = e.Location; OnMouseUp(e); }
        internal void RaiseMouseMove(MouseEventArgs e) { LastTouch = e.Location; OnMouseMove(e); }
        internal void RaiseMouseWheel(MouseEventArgs e) => OnMouseWheel(e);
        internal void RaiseMouseLeave() => OnMouseLeave(EventArgs.Empty);
        internal void RaiseKeyPress(char c) => OnKeyPress(new KeyPressEventArgs(c));

        internal bool RaiseKeyDown(Keys keyData)
        {
            var msg = new Message();
            if (ProcessCmdKey(ref msg, keyData)) return true;
            var e = new KeyEventArgs(keyData);
            OnKeyDown(e);
            return e.Handled;
        }

        /// <summary>Files picked on the phone arrive like files dropped on the radio.</summary>
        internal void RaiseFilesDropped(string[] files, Point at)
        {
            var e = new DragEventArgs(new FileDropData(files), at.X, at.Y, DragDropEffects.Copy);
            OnDragEnter(e);
            if (e.Effect != DragDropEffects.None) OnDragDrop(e);
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

        void RaiseClosed()
        {
            IsDisposed = true;
            OnFormClosed(new FormClosedEventArgs(CloseReason.UserClosing));
            Dispose();
        }
    }

    // ───────────────────────────── dialogs ─────────────────────────────

    /// <summary>
    /// Android's pickers can't block the way WinForms dialogs do, so ShowDialog opens the picker and returns
    /// Cancel at once. The files arrive later: through <see cref="Picked"/> if it's set, otherwise dropped on the radio.
    /// </summary>
    public abstract class CommonDialog : IDisposable
    {
        public Action<string[]>? Picked { get; set; }
        public DialogResult ShowDialog() => ShowDialog(null);
        public abstract DialogResult ShowDialog(Form? owner);
        public void Dispose() { }

        private protected DialogResult Open(Form? owner, string title, string[] exts, bool multiple, bool folder)
        {
            var form = owner ?? Application.MainForm;
            Host.Current.Pick(new PickRequest(title, exts, multiple, folder, files =>
            {
                if (files.Length == 0) return;
                if (Picked != null) Picked(files);
                else form?.RaiseFilesDropped(files, new Point(form.ClientSize.Width / 2, form.ClientSize.Height / 2));
            }));
            return DialogResult.Cancel;
        }

        /// <summary>The extensions in a WinForms filter ("Audio|*.mp3;*.wav|All files|*.*" → .mp3, .wav).</summary>
        private protected static string[] Extensions(string filter)
        {
            var parts = filter.Split('|');
            var list = new List<string>();
            for (int i = 1; i < parts.Length; i += 2)
                foreach (var p in parts[i].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (p == "*.*" || p == "*") return list.Count > 0 ? [.. list] : [];
                    list.Add(p.TrimStart('*'));
                }
            return [.. list];
        }
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

        public override DialogResult ShowDialog(Form? owner) => Open(owner, Title, Extensions(Filter), Multiselect, folder: false);
    }

    public class FolderBrowserDialog : CommonDialog
    {
        public string Description { get; set; } = "";
        public bool UseDescriptionForTitle { get; set; }
        public string SelectedPath { get; set; } = "";
        public bool ShowNewFolderButton { get; set; } = true;

        public override DialogResult ShowDialog(Form? owner) => Open(owner, Description, [], multiple: false, folder: true);
    }

    // ───────────────────────────── application ─────────────────────────────

    public static class Application
    {
        public static Form? MainForm { get; internal set; }

        public static void Exit() => Host.Current.Quit();
        public static void DoEvents() { }
        public static void EnableVisualStyles() { }
        public static void SetCompatibleTextRenderingDefault(bool value) { }
        public static string StartupPath => AppContext.BaseDirectory;
        public static string ExecutablePath => Environment.ProcessPath ?? "";
    }
}

namespace RetroRadio.Droid
{
    public enum TouchMode
    {
        /// <summary>The finger is a mouse: down at once, drags turn knobs, holding repeats.</summary>
        Press,

        /// <summary>A list on the display: a tap clicks, a swipe scrolls, a long press is a right-click (back).</summary>
        Scroll,
    }
}
