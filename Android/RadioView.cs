using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using RetroRadio.Droid;
using SkiaSharp;
using AColor = Android.Graphics.Color;
using ABitmap = Android.Graphics.Bitmap;
using ACanvas = Android.Graphics.Canvas;
using APaint = Android.Graphics.Paint;
using ARectF = Android.Graphics.RectF;
using Keys = System.Windows.Forms.Keys;
using MouseButtons = System.Windows.Forms.MouseButtons;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using DPoint = System.Drawing.Point;

namespace RetroRadio;

/// <summary>
/// Shows the radio: paints the form (through the System.Drawing shim on Skia) into a bitmap the size of the
/// radio, scales that onto the screen, and turns touches into the mouse clicks, drags and wheel turns it expects.
/// </summary>
sealed class RadioView : View
{
    const int LongPressMs = 450, DoubleTapMs = 350;

    RadioForm? form;
    ABitmap? frame;
    readonly APaint scaler = new() { FilterBitmap = true, AntiAlias = false };
    readonly float density;
    bool created, keyboardShown;
    int imeBottom;

    // Where the radio sits on the screen.
    float scale = 1, offX, offY;

    // The touch in progress.
    TouchMode mode;
    bool touching, scrolling, longPressed;
    float downX, downY, lastScrollY;
    long lastTapTime;
    DPoint lastTapAt;
    int touchId;
    long touchDownTime;

    public RadioView(Context context) : base(context)
    {
        density = context.Resources?.DisplayMetrics?.Density ?? 2;
        Focusable = true;
        FocusableInTouchMode = true;
        SetBackgroundColor(AColor.Black);
    }

    public RadioForm? Form
    {
        get => form;
        set
        {
            form = value;
            created = form?.IsHandleCreated == true;
            if (created) FitForm();
            Invalidate();
        }
    }

    // ───────────────────────────── size ─────────────────────────────

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (form == null || w <= 0 || h <= 0) return;
        if (!created)
        {
            created = true;
            form.Create((int)Math.Round(96 * density));
        }
        FitForm();
    }

    /// <summary>
    /// Picks the radio's drawing scale (its "DPI") so it fills the screen: the radio then redraws its
    /// faceplate sharply at that size, instead of being blown up from a small picture.
    /// </summary>
    public void FitForm()
    {
        if (form == null || !created || Width <= 0 || Height <= 0) return;
        var cs = form.ClientSize;
        if (cs.Width <= 0 || cs.Height <= 0) return;
        double k = Math.Min((double)Width / cs.Width, (double)Height / cs.Height);
        if (Math.Abs(k - 1) < 0.01) return;
        int dpi = (int)Math.Floor(form.DeviceDpi * k);
        form.SetDpi(Math.Clamp(dpi, 24, 2000));
        Invalidate();
    }

    // ───────────────────────────── drawing ─────────────────────────────

    protected override void OnDraw(ACanvas canvas)
    {
        canvas.DrawColor(AColor.Black);
        if (form == null || !created || form.IsDisposed) return;
        var cs = form.ClientSize;
        if (cs.Width <= 0 || cs.Height <= 0) return;

        if (frame == null || frame.Width != cs.Width || frame.Height != cs.Height)
        {
            frame?.Recycle();
            frame?.Dispose();
            frame = ABitmap.CreateBitmap(cs.Width, cs.Height, ABitmap.Config.Argb8888!);
        }

        IntPtr pixels = frame.LockPixels();
        try
        {
            var info = new SKImageInfo(cs.Width, cs.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, pixels, frame.RowBytes);
            var c = surface.Canvas;
            c.Clear(SKColors.Black);
            using (var g = new Graphics(c, false))
            {
                try { form.RaisePaint(g); }
                catch (Exception ex) { Android.Util.Log.Error("RetroRadio", ex.ToString()); }
            }
            if (form.Region?.Path is { } shape)
            {
                // Outside the radio's outline is the black screen, not whatever was drawn there.
                c.ResetMatrix();
                c.ClipPath(shape, SKClipOperation.Difference, true);
                c.Clear(SKColors.Black);
            }
            c.Flush();
        }
        finally
        {
            frame.UnlockPixels();
        }

        // Fit into what the keyboard leaves free (the radio shrinks a little while typing).
        int h = Math.Max(1, Height - imeBottom);
        scale = Math.Min((float)Width / cs.Width, (float)h / cs.Height);
        offX = (Width - cs.Width * scale) / 2;
        offY = (h - cs.Height * scale) / 2;
        scaler.FilterBitmap = Math.Abs(scale - 1) > 0.001f;
        using var dst = new ARectF(offX, offY, offX + cs.Width * scale, offY + cs.Height * scale);
        canvas.DrawBitmap(frame, null, dst, scaler);

        UpdateKeyboard();
    }

    // ───────────────────────────── touch ─────────────────────────────

    DPoint ToForm(float x, float y) => new((int)Math.Round((x - offX) / scale), (int)Math.Round((y - offY) / scale));

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null || form == null || !created) return false;
        // A real mouse (a Chromebook, DeX, a USB mouse) is a mouse: right button included.
        if (e.IsFromSource(InputSourceType.Mouse)) return OnMouse(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                touching = true;
                touchId = e.GetPointerId(0);
                touchDownTime = e.DownTime;
                downX = e.GetX();
                downY = lastScrollY = e.GetY();
                scrolling = longPressed = false;
                var p = ToForm(downX, downY);
                mode = form.TouchModeAt(p);
                if (mode == TouchMode.Press)
                {
                    form.RaiseMouseDown(new MouseEventArgs(MouseButtons.Left, TapCount(p), p.X, p.Y, 0));
                }
                else
                {
                    long downAt = e.DownTime;
                    PostDelayed(() => LongPress(downAt), LongPressMs);
                }
                break;

            case MotionEventActions.Move:
                if (!touching || e.FindPointerIndex(touchId) is not (>= 0 and var i)) break;
                float x = e.GetX(i), y = e.GetY(i);
                if (mode == TouchMode.Press)
                {
                    var mp = ToForm(x, y);
                    form.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 0, mp.X, mp.Y, 0));
                }
                else if (!longPressed)
                {
                    if (!scrolling && Math.Abs(y - downY) > 10 * density) scrolling = true;
                    if (scrolling) Scroll(x, y);
                }
                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (!touching) break;
                touching = false;
                var up = ToForm(e.GetX(), e.GetY());
                if (mode == TouchMode.Press)
                {
                    form.RaiseMouseUp(new MouseEventArgs(MouseButtons.Left, 1, up.X, up.Y, 0));
                }
                else if (!scrolling && !longPressed && e.ActionMasked == MotionEventActions.Up)
                {
                    // A tap on a menu row: a click (two quick taps are a double-click).
                    var at = ToForm(downX, downY);
                    form.RaiseMouseDown(new MouseEventArgs(MouseButtons.Left, TapCount(at), at.X, at.Y, 0));
                    form.RaiseMouseUp(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
                }
                form.RaiseMouseLeave();
                break;
        }
        return true;
    }

    /// <summary>1 for a tap, 2 when it follows the last one quickly at the same spot.</summary>
    int TapCount(DPoint p)
    {
        long t = SystemClock.UptimeMillis();
        bool dbl = t - lastTapTime < DoubleTapMs && Math.Abs(p.X - lastTapAt.X) < 40 * density / scale && Math.Abs(p.Y - lastTapAt.Y) < 40 * density / scale;
        lastTapTime = dbl ? 0 : t;
        lastTapAt = p;
        return dbl ? 2 : 1;
    }

    /// <summary>Held still on a menu: a right-click, which goes back (or steps a setting the other way).</summary>
    void LongPress(long downAt)
    {
        // Only for the touch that asked for it (a later touch has its own check).
        if (!touching || scrolling || longPressed || downAt != touchDownTime || form == null) return;
        longPressed = true;
        PerformHapticFeedback(FeedbackConstants.LongPress);
        var p = ToForm(downX, downY);
        form.RaiseMouseDown(new MouseEventArgs(MouseButtons.Right, 1, p.X, p.Y, 0));
        form.RaiseMouseUp(new MouseEventArgs(MouseButtons.Right, 1, p.X, p.Y, 0));
    }

    /// <summary>Swiping a list moves it one row per finger-width of travel, like turning a mouse wheel.</summary>
    void Scroll(float x, float y)
    {
        float step = 28 * density;
        var p = ToForm(x, y);
        while (Math.Abs(y - lastScrollY) >= step)
        {
            int dir = y < lastScrollY ? -1 : 1;
            lastScrollY += dir * step;
            form!.RaiseMouseWheel(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, dir * 120));
        }
    }

    bool OnMouse(MotionEvent e)
    {
        var p = ToForm(e.GetX(), e.GetY());
        var button = (e.ButtonState & MotionEventButtonState.Secondary) != 0 ? MouseButtons.Right : MouseButtons.Left;
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                form!.RaiseMouseDown(new MouseEventArgs(button, TapCount(p), p.X, p.Y, 0));
                break;
            case MotionEventActions.Move:
                form!.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 0, p.X, p.Y, 0));
                break;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                form!.RaiseMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
                break;
        }
        return true;
    }

    public override bool OnGenericMotionEvent(MotionEvent? e)
    {
        if (e == null || form == null || !created) return false;
        var p = ToForm(e.GetX(), e.GetY());
        if (e.ActionMasked == MotionEventActions.Scroll)
        {
            float v = e.GetAxisValue(Axis.Vscroll);
            if (v != 0) form.RaiseMouseWheel(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, (int)Math.Round(v * 120)));
            return true;
        }
        if (e.ActionMasked == MotionEventActions.HoverMove)
        {
            form.RaiseMouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));
            return true;
        }
        return base.OnGenericMotionEvent(e);
    }

    // ───────────────────────────── keys ─────────────────────────────

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (form == null || !created || e == null) return base.OnKeyDown(keyCode, e);
        var key = Map(keyCode);
        if (key == Keys.None && e.UnicodeChar == 0) return base.OnKeyDown(keyCode, e);
        if (e.IsShiftPressed) key |= Keys.Shift;
        if (e.IsCtrlPressed) key |= Keys.Control;
        if (e.IsAltPressed) key |= Keys.Alt;
        bool handled = (key & Keys.KeyCode) != Keys.None && form.RaiseKeyDown(key);
        if (!handled && e.UnicodeChar is > 31 and var ch)
        {
            form.RaiseKeyPress((char)ch);
            handled = true;
        }
        return handled || base.OnKeyDown(keyCode, e);
    }

    public void SendKey(Keys key)
    {
        if (form != null && created) form.RaiseKeyDown(key);
    }

    static Keys Map(Keycode k) => k switch
    {
        >= Keycode.A and <= Keycode.Z => Keys.A + (k - Keycode.A),
        >= Keycode.Num0 and <= Keycode.Num9 => Keys.D0 + (k - Keycode.Num0),
        Keycode.Space => Keys.Space,
        Keycode.Enter or Keycode.NumpadEnter or Keycode.DpadCenter => Keys.Enter,
        Keycode.Del => Keys.Back,
        Keycode.ForwardDel => Keys.Delete,
        Keycode.Escape => Keys.Escape,
        Keycode.DpadUp => Keys.Up,
        Keycode.DpadDown => Keys.Down,
        Keycode.DpadLeft => Keys.Left,
        Keycode.DpadRight => Keys.Right,
        Keycode.Slash => Keys.OemQuestion,
        Keycode.LeftBracket => Keys.OemOpenBrackets,
        Keycode.RightBracket => Keys.OemCloseBrackets,
        Keycode.Backslash => Keys.OemPipe,
        Keycode.Minus => Keys.OemMinus,
        Keycode.Equals => Keys.Oemplus,
        Keycode.Comma => Keys.Oemcomma,
        Keycode.Period => Keys.OemPeriod,
        Keycode.Tab => Keys.Tab,
        // Headset and car buttons.
        Keycode.MediaPlayPause or Keycode.MediaPlay or Keycode.MediaPause or Keycode.Headsethook => Keys.Space,
        Keycode.MediaNext => Keys.N,
        Keycode.MediaPrevious => Keys.P,
        Keycode.MediaStop => Keys.S,
        _ => Keys.None,
    };

    // ───────────────────────────── soft keyboard ─────────────────────────────

    public override bool OnCheckIsTextEditor() => form?.WantsTextInput == true;

    public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs != null)
        {
            // Plain key presses (the radio's search works letter by letter), and no full-screen typing box in landscape.
            outAttrs.InputType = Android.Text.InputTypes.Null;
            outAttrs.ImeOptions = ImeFlags.NoExtractUi | ImeFlags.NoFullscreen | (ImeFlags)ImeAction.Done;
        }
        return new BaseInputConnection(this, false);
    }

    void UpdateKeyboard()
    {
        bool want = form?.WantsTextInput == true;
        if (want == keyboardShown) return;
        keyboardShown = want;
        var imm = (InputMethodManager?)Context?.GetSystemService(Context.InputMethodService);
        if (imm == null) return;
        if (want)
        {
            RequestFocus();
            imm.RestartInput(this);
            imm.ShowSoftInput(this, ShowFlags.Implicit);
        }
        else
        {
            imm.HideSoftInputFromWindow(WindowToken, HideSoftInputFlags.None);
        }
    }

    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets != null && Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            int ime = insets.GetInsets(WindowInsets.Type.Ime()).Bottom;
            if (ime != imeBottom)
            {
                imeBottom = ime;
                Invalidate();
            }
        }
        return base.OnApplyWindowInsets(insets);
    }
}
