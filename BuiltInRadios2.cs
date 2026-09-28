namespace RetroRadio;

/// <summary>Twenty more radio models, built from eight layouts, each with its own look.</summary>
static partial class BuiltInRadios
{
    public static IEnumerable<RadioDesign> More() =>
    [
        Kensei(), Nakamura(), Xplode(), Carrozza(), Kaido(), Touge(), Sakura(), Dekotora(), Vapor(), Miami(),
        Polar(), Milspec(), Cruisemaster(), Titan(), Pocketsonic(), Suburbia(), Lowrider(), Tapeworks(), Orbit(), Nebula(),
    ];

    // ───────────────────────────── looks ─────────────────────────────

    /// <summary>Colours and materials in one go.</summary>
    static void Look(RadioDesign d, FaceFinish finish, Mat faceMat, Color f1, Color f2, Color f3, Color print, Color brand, Color trim,
        Mat keyMat, Color k1, Color k2, Color keyText, Mat knobMat, Color n1, Color n2, Color pointer, Color glow, Color night, int palette,
        Color? keyEdge = null, Mat bezelMat = Mat.Gloss, Color? bezel1 = null, Color? bezel2 = null, Color? rim = null)
    {
        d.Finish = finish; d.FaceMaterial = faceMat;
        d.Face1 = f1; d.Face2 = f2; d.Face3 = f3;
        d.Print = print; d.BrandInk = brand; d.Trim = trim;
        d.BezelMaterial = bezelMat; d.Bezel1 = bezel1 ?? C(40, 42, 46); d.Bezel2 = bezel2 ?? C(8, 8, 10); d.BezelRim = rim ?? C(200, 204, 210);
        d.KeyMaterial = keyMat; d.Key1 = k1; d.Key2 = k2; d.KeyEdge = keyEdge ?? C(20, 20, 22); d.KeyText = keyText;
        d.TransportMaterial = keyMat; d.Transport1 = k1; d.Transport2 = k2; d.TransportText = keyText;
        d.KnobMaterial = knobMat; d.Knob1 = n1; d.Knob2 = n2; d.Pointer = pointer;
        d.Glow = glow; d.NightLight = night; d.Led = glow; d.Palette = palette;
    }

    // ───────────────────────────── layouts ─────────────────────────────

    static void Transport(RadioDesign d, KeyKind k, float cx, float y, float w, float h, bool seek = true)
    {
        Btn[] ids = seek ? [Btn.SeekBack, Btn.Prev, Btn.Play, Btn.Next, Btn.SeekFwd] : [Btn.Prev, Btn.Play, Btn.Next];
        float step = w + 8, x0 = cx - (ids.Length * step - 8) / 2;
        for (int i = 0; i < ids.Length; i++)
            d.Keys.Add(K(ids[i], k, x0 + i * step, y, w, h, ids[i] is Btn.SeekBack or Btn.SeekFwd ? "SEEK" : ""));
    }

    /// <summary>Single DIN: display in the middle, function keys under it, controls along the bottom, knobs either side.</summary>
    static void LayoutDin(RadioDesign d, KeyKind fn, KeyKind ctl, KeyKind tr)
    {
        float w = d.Width;
        Glass(d, (w - 632 * 0.72f) / 2, 58, 632, 0.72f, 14, 16);
        var g = d.Glass;
        d.BrandAt = new(40, 24); d.ModelAt = new(w - 40, 26);
        for (int i = 0; i < 6; i++)
        {
            float step = g.Width / 6;
            d.Keys.Add(K(Fn[i].Id, fn, g.X + i * step + 3, 284, step - 6, 34, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, ctl, g.X + i * step + 3, 328, step - 6, 26, Ctl[i].Label));
        }
        Transport(d, tr, w / 2, 368, 84, 36);
        d.Keys.Add(Knob(Btn.Knob, 124, 190, 72, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, w - 124, 190, 46, LabelAt.Below));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, w - 160, 372, 76, 24, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 44, 380, 90, 22, "DETACH"));
        d.ShowAux = true; d.Aux = new(w - 62, 386);
        d.TheftLed = new(w - 190, 30);
    }

    /// <summary>Double DIN: a big display on top, a big knob and a key grid below.</summary>
    static void LayoutDoubleDin(RadioDesign d, KeyKind keys, KeyKind tr)
    {
        Glass(d, 40, 58, 632, 1.3f, 14, 18);
        d.BrandAt = new(40, 20); d.ModelAt = new(d.Width - 40, 22);
        d.BrandSize = 13;
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Fn[i].Id, keys, 232 + i * 94, 426, 86, 36, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, keys, 232 + i * 94, 472, 86, 28, Ctl[i].Label));
        }
        Transport(d, tr, 510, 534, 92, 42);
        d.Keys.Add(Knob(Btn.Knob, 120, 506, 80, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, 832, 556, 34, LabelAt.Above));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, 800, 426, 70, 36));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 800, 472, 70, 28, "DETACH"));
        d.ShowUsb = true; d.Usb = new(40, 590, 40, 16);
        d.TheftLed = new(860, 20);
    }

    /// <summary>Key columns on both sides of the display, transport under it, knobs at the bottom.</summary>
    static void LayoutColumns(RadioDesign d, KeyKind keys, KeyKind tr)
    {
        float w = d.Width;
        Glass(d, (w - 632) / 2, 40, 632, 1f, 14, 16);
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Fn[i].Id, keys, 30, 40 + i * 54, 110, 44, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, keys, w - 140, 40 + i * 54, 110, 44, Ctl[i].Label));
        }
        Transport(d, tr, w / 2, 348, 100, 42);
        d.Keys.Add(Knob(Btn.Knob, 190, 440, 56, LabelAt.Right));
        d.Keys.Add(Knob(Btn.SpeedKnob, w - 190, 440, 40, LabelAt.Left));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, w / 2 - 110, 414, 100, 26, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, w / 2 + 10, 414, 100, 26, "DETACH"));
        d.BrandAt = new(w / 2, 478); d.BrandAlign = TextAlign.Center; d.BrandSize = 15;
        d.ModelAt = new(w / 2, 20); d.ModelAlign = TextAlign.Center;
        d.TheftLed = new(w / 2 + 150, 427);
    }

    /// <summary>A long, slim unit: knob, display, then a panel of keys.</summary>
    static void LayoutSlim(RadioDesign d, KeyKind keys, KeyKind tr)
    {
        Glass(d, 230, 34, 632, 0.62f, 12, 14);
        d.BrandAt = new(236, 232); d.BrandSize = 14;
        d.ModelAt = new(616, 236);
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Fn[i].Id, keys, 664 + i * 82, 36, 76, 30, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, keys, 664 + i * 82, 80, 76, 30, Ctl[i].Label));
        }
        Transport(d, tr, 908, 136, 88, 40);
        d.Keys.Add(Knob(Btn.Knob, 118, 150, 84, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, 700, 238, 26, LabelAt.Right));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, 820, 226, 84, 26, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 914, 226, 84, 26, "DETACH"));
        d.ShowAux = true; d.Aux = new(1060, 240);
        d.TheftLed = new(1110, 236);
    }

    /// <summary>A giant knob with keys hugging it, the display beside it.</summary>
    static void LayoutHub(RadioDesign d, KeyKind arc, KeyKind keys, KeyKind tr)
    {
        float w = d.Width, cx = w - 190, cy = 240;
        Glass(d, 40, 64, 632, 0.88f, 14, 16);
        d.BrandAt = new(40, 22); d.ModelAt = new(600, 24);
        for (int i = 0; i < 6; i++)
        {
            double a = (122 + i * 23) * Math.PI / 180;
            float kx = cx + 150 * (float)Math.Cos(a), ky = cy + 150 * (float)Math.Sin(a);
            d.Keys.Add(K(Fn[i].Id, arc, kx - 24, ky - 20, 48, 40, FnShort[i]));
            d.Keys.Add(K(Ctl[i].Id, keys, 40 + i * 94, 336, 88, 30, Ctl[i].Label));
        }
        Transport(d, tr, 318, 386, 92, 40);
        d.Keys.Add(Knob(Btn.Knob, cx, cy, 104, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, cx, 440, 30, LabelAt.Right));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, w - 120, 20, 70, 20));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 40, 444, 100, 24, "DETACH"));
        d.ShowUsb = true; d.Usb = new(160, 448, 40, 16);
        d.TheftLed = new(w - 150, 30);
    }

    /// <summary>Fifties dashboard radio: chrome keys top and bottom, a big knob at each end.</summary>
    static void LayoutDial(RadioDesign d, KeyKind keys)
    {
        float w = d.Width;
        Glass(d, (w - 632 * 0.8f) / 2, 104, 632, 0.8f, 14, 14);
        var g = d.Glass;
        for (int i = 0; i < 6; i++)
        {
            float step = g.Width / 6;
            d.Keys.Add(K(Fn[i].Id, keys, g.X + i * step + 4, 32, step - 8, 48, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, keys, g.X + i * step + 4, 334, step - 8, 24, Ctl[i].Label));
        }
        float lx = g.X - 104, rx = g.Right + 12;
        d.Keys.Add(K(Btn.Prev, keys, lx, 110, 92, 36));
        d.Keys.Add(K(Btn.Play, keys, lx, 158, 92, 36));
        d.Keys.Add(K(Btn.Next, keys, lx, 206, 92, 36));
        d.Keys.Add(K(Btn.SeekBack, keys, rx, 110, 92, 36, "SEEK"));
        d.Keys.Add(K(Btn.SeekFwd, keys, rx, 158, 92, 36, "SEEK"));
        d.Keys.Add(K(Btn.Eject, keys, rx, 206, 92, 36, "EJECT"));
        d.Keys.Add(Knob(Btn.Knob, 92, 200, 62, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, w - 92, 200, 62, LabelAt.Below));
        d.BrandAt = new(w / 2, 362); d.BrandAlign = TextAlign.Center; d.BrandSize = 13; d.BrandFont = "Georgia";
        d.ModelAt = new(w - 40, 10);
        d.TheftLed = new(g.X + 20, 318);
    }

    /// <summary>Tall unit: display on top, a keypad under it, knobs at the bottom corners.</summary>
    static void LayoutTall(RadioDesign d, KeyKind keys, KeyKind tr)
    {
        Glass(d, 40, 58, 632, 0.88f, 14, 16);
        Btn[] pad = [.. Fn.Select(f => f.Id), .. Ctl.Select(c => c.Id)];
        string[] labels = [.. Fn.Select(f => f.Label), .. Ctl.Select(c => c.Label)];
        for (int i = 0; i < 12; i++)
            d.Keys.Add(K(pad[i], keys, 40 + i % 3 * 190, 326 + i / 3 * 52, 178, 42, labels[i]));
        Transport(d, tr, d.Width / 2, 540, 100, 44);
        d.Keys.Add(Knob(Btn.Knob, 110, 636, 48, LabelAt.Right));
        d.Keys.Add(Knob(Btn.SpeedKnob, d.Width - 110, 636, 36, LabelAt.Left));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, d.Width / 2 - 50, 616, 100, 28, "EJECT"));
        d.BrandAt = new(d.Width / 2, 16); d.BrandAlign = TextAlign.Center; d.BrandSize = 14;
        d.ModelAt = new(d.Width / 2, 662); d.ModelAlign = TextAlign.Center;
        d.TheftLed = new(d.Width - 60, 20);
    }

    /// <summary>A wide display across the top, a band of keys, and knobs in the bottom corners.</summary>
    static void LayoutBand(RadioDesign d, KeyKind keys, KeyKind tr)
    {
        float w = d.Width;
        Glass(d, 60, 38, 900, 1.08f, 14, 16);
        Btn[] band = [.. Fn.Select(f => f.Id), .. Ctl.Select(c => c.Id)];
        string[] labels = [.. FnShort, .. CtlShort];
        float step = (w - 300) / 12;
        for (int i = 0; i < 12; i++) d.Keys.Add(K(band[i], keys, 150 + i * step + 3, 358, step - 6, 34, labels[i]));
        Transport(d, tr, w / 2, 410, 90, 40);
        d.Keys.Add(Knob(Btn.Knob, 96, 426, 42, LabelAt.Right));
        d.Keys.Add(Knob(Btn.SpeedKnob, w - 96, 426, 34, LabelAt.Left));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, w - 130, 12, 70, 18));
        d.BrandAt = new(60, 16); d.BrandSize = 13;
        d.ModelAt = new(w - 150, 18);
        d.TheftLed = new(w / 2, 20);
    }

    // ───────────────────────────── the models ─────────────────────────────

    static RadioDesign Kensei()
    {
        var d = New("KENSEI DDX-9", "KENSEI", "DDX-9  ·  DOUBLE DIN", 900, 620, 16);
        LayoutDoubleDin(d, KeyKind.Rubber, KeyKind.Pill);
        Look(d, FaceFinish.Gloss, Mat.Gloss, C(46, 48, 54), C(24, 25, 29), C(10, 10, 12), C(255, 170, 60), C(235, 238, 242), C(255, 150, 40),
            Mat.Rubber, C(52, 54, 60), C(20, 21, 24), C(255, 175, 70), Mat.Chrome, C(210, 212, 218), C(70, 72, 80), C(255, 150, 40),
            C(255, 150, 40), C(255, 160, 60), 2);
        d.BacklitLabels = true;
        return d;
    }

    static RadioDesign Nakamura()
    {
        var d = New("NAKAMURA TD-1200", "NAKAMURA", "TD-1200  ·  DIGITAL REFERENCE", 1180, 300, 8);
        LayoutSlim(d, KeyKind.Flat, KeyKind.Flat);
        Look(d, FaceFinish.Brushed, Mat.Brushed, C(58, 56, 52), C(36, 35, 32), C(20, 19, 18), C(212, 180, 110), C(230, 200, 130), C(212, 180, 110),
            Mat.Brushed, C(70, 67, 62), C(34, 32, 30), C(225, 195, 125), Mat.Chrome, C(236, 214, 160), C(120, 100, 60), C(40, 30, 10),
            C(255, 190, 90), C(255, 190, 90), 2, rim: C(212, 180, 110));
        d.BrandFont = "Times New Roman"; d.BrandItalic = false;
        d.Pinstripe = true;
        return d;
    }

    static RadioDesign Xplode()
    {
        var d = New("XPLODE GT-2001", "XPLODE", "CDX-GT2001  ·  4 × 52W", 980, 500, 26);
        LayoutHub(d, KeyKind.OvalTilt, KeyKind.Pill, KeyKind.Pill);
        Look(d, FaceFinish.Brushed, Mat.Brushed, C(222, 226, 232), C(160, 166, 176), C(110, 116, 126), C(20, 60, 140), C(20, 80, 200), C(40, 130, 255),
            Mat.Gloss, C(70, 120, 210), C(20, 50, 120), C(230, 240, 255), Mat.Chrome, C(230, 234, 240), C(90, 96, 108), C(40, 130, 255),
            C(40, 130, 255), C(80, 160, 255), 4, rim: C(40, 130, 255));
        d.NeonKeys = true; d.DomeColor = C(40, 130, 255);
        d.BrandFont = "Arial Black"; d.BrandSize = 16;
        return d;
    }

    static RadioDesign Carrozza()
    {
        var d = New("CARROZZA DEH-9", "CARROZZA", "DEH-P9  ·  DETACHABLE", 980, 420, 12);
        LayoutDin(d, KeyKind.Angled, KeyKind.Angled, KeyKind.ChevronL);
        d.Keys.RemoveAll(k => k.Id is Btn.Prev or Btn.Next);
        d.Keys.Add(K(Btn.Prev, KeyKind.ChevronL, 345, 368, 84, 36));
        d.Keys.Add(K(Btn.Next, KeyKind.ChevronR, 551, 368, 84, 36));
        Look(d, FaceFinish.Brushed, Mat.Brushed, C(200, 202, 206), C(140, 142, 148), C(92, 94, 100), C(40, 40, 44), C(200, 20, 30), C(220, 30, 40),
            Mat.Plastic, C(60, 62, 68), C(24, 25, 28), C(240, 240, 240), Mat.Plastic, C(70, 72, 80), C(20, 21, 24), C(255, 60, 60),
            C(255, 50, 50), C(255, 60, 60), 5);
        d.BrandFont = "Arial Black";
        return d;
    }

    static RadioDesign Kaido()
    {
        var d = New("KAIDO RACER", "KAIDO", "KR-86  ·  STREET SPEC", 1000, 520, 10);
        LayoutColumns(d, KeyKind.Angled, KeyKind.Angled);
        Look(d, FaceFinish.Carbon, Mat.Clearcoat, C(48, 48, 52), C(26, 26, 29), C(12, 12, 14), C(255, 60, 50), C(255, 255, 255), C(255, 40, 40),
            Mat.Rubber, C(40, 40, 44), C(16, 16, 18), C(255, 80, 70), Mat.Brushed, C(200, 30, 30), C(90, 10, 10), C(255, 255, 255),
            C(255, 40, 40), C(255, 50, 50), 5, rim: C(255, 40, 40));
        d.Bolts = true;
        return d;
    }

    static RadioDesign Touge()
    {
        var d = New("TOUGE 86", "TOUGE", "AE-86  ·  HACHIROKU", 980, 420, 6);
        LayoutDin(d, KeyKind.OvalTilt, KeyKind.Rubber, KeyKind.Pill);
        Look(d, FaceFinish.Matte, Mat.Plastic, C(40, 40, 42), C(26, 26, 28), C(14, 14, 16), C(235, 235, 235), C(255, 255, 255), C(230, 230, 230),
            Mat.Rubber, C(50, 50, 54), C(20, 20, 22), C(240, 240, 240), Mat.Rubber, C(44, 44, 48), C(14, 14, 16), C(255, 255, 255),
            C(120, 220, 255), C(120, 220, 255), 0);
        d.BrandFont = "Impact"; d.BrandItalic = true; d.BrandSize = 22;
        d.Pinstripe = true; d.Trim = C(250, 250, 250);
        Print(d, "トレノ  ·  DRIFT KING EDITION", 340, 22, 300, 16, TextAlign.Center, 7.5f);
        return d;
    }

    static RadioDesign Sakura()
    {
        var d = New("SAKURA POP", "sakura", "SK-1  ·  KEI CAR AUDIO", 640, 700, 40);
        LayoutTall(d, KeyKind.Round, KeyKind.Round);
        Look(d, FaceFinish.Gloss, Mat.Gloss, C(252, 244, 248), C(240, 214, 226), C(214, 176, 196), C(200, 70, 130), C(230, 80, 150), C(255, 120, 180),
            Mat.Gloss, C(255, 170, 205), C(230, 110, 160), C(255, 255, 255), Mat.Gloss, C(255, 190, 215), C(220, 110, 160), C(255, 255, 255),
            C(255, 120, 180), C(255, 140, 200), 6, keyEdge: C(210, 120, 160), rim: C(255, 160, 200));
        d.BrandFont = "Segoe UI Black"; d.BrandItalic = false;
        Print(d, "さくら  ·  かわいい", 220, 594, 200, 12, TextAlign.Center, 8);
        return d;
    }

    static RadioDesign Dekotora()
    {
        var d = New("DEKOTORA GOLD", "DEKOTORA", "DT-7  ·  TRUCK SPECIAL", 1040, 400, 18);
        LayoutDial(d, KeyKind.Chrome);
        Look(d, FaceFinish.Brushed, Mat.Chrome, C(250, 226, 150), C(214, 172, 70), C(150, 110, 30), C(90, 20, 10), C(160, 20, 20), C(255, 60, 60),
            Mat.Chrome, C(252, 232, 170), C(180, 140, 50), C(110, 20, 10), Mat.Chrome, C(255, 238, 180), C(170, 128, 40), C(220, 30, 30),
            C(255, 70, 200), C(255, 80, 200), 6, bezelMat: Mat.Chrome, bezel1: C(250, 220, 140), bezel2: C(150, 110, 30), rim: C(255, 70, 200));
        d.NeonBezel = true; d.ChromeTrim = true; d.Bolts = true;
        d.BrandFont = "Impact"; d.BrandSize = 16;
        Print(d, "デコトラ  ·  一番星", 40, 10, 240, 14, TextAlign.Left, 8);
        return d;
    }

    static RadioDesign Vapor()
    {
        var d = New("VAPOR 1989", "VAPOR", "V-89  ·  AESTHETIC", 1100, 480, 28);
        LayoutBand(d, KeyKind.Round, KeyKind.Round);
        Look(d, FaceFinish.Gloss, Mat.Gloss, C(120, 220, 214), C(90, 180, 200), C(170, 110, 200), C(255, 255, 255), C(255, 110, 200), C(255, 110, 200),
            Mat.Gloss, C(255, 150, 210), C(200, 90, 170), C(255, 255, 255), Mat.Gloss, C(255, 200, 230), C(180, 90, 160), C(80, 230, 255),
            C(255, 110, 220), C(255, 120, 220), 6, rim: C(255, 255, 255));
        d.BrandFont = "Segoe UI Light"; d.BrandItalic = false; d.BrandSize = 16;
        Print(d, "Ｖ Ａ Ｐ Ｏ Ｒ Ｗ Ａ Ｖ Ｅ", 380, 458, 340, 14, TextAlign.Center, 8);
        return d;
    }

    static RadioDesign Miami()
    {
        var d = New("MIAMI NIGHTS", "MIAMI", "MX-85  ·  NIGHT DRIVE", 1180, 300, 14);
        LayoutSlim(d, KeyKind.Pill, KeyKind.Pill);
        Look(d, FaceFinish.Gloss, Mat.Gloss, C(30, 26, 40), C(16, 14, 24), C(6, 6, 10), C(255, 90, 200), C(80, 230, 255), C(255, 90, 200),
            Mat.Gloss, C(44, 36, 60), C(16, 14, 24), C(255, 120, 220), Mat.Gloss, C(60, 50, 80), C(14, 12, 20), C(80, 230, 255),
            C(255, 70, 200), C(255, 80, 210), 6, rim: C(255, 70, 200));
        d.NeonBezel = true; d.NeonKeys = true;
        d.BrandFont = "Segoe UI Black"; d.BrandItalic = true;
        return d;
    }

    static RadioDesign Polar()
    {
        var d = New("POLAR WHITE", "POLAR", "P1  ·  NORDIC", 980, 420, 20);
        LayoutDin(d, KeyKind.Flat, KeyKind.Flat, KeyKind.Flat);
        Look(d, FaceFinish.Matte, Mat.Plastic, C(246, 247, 248), C(226, 228, 231), C(200, 203, 208), C(70, 76, 86), C(40, 44, 52), C(120, 180, 230),
            Mat.Plastic, C(250, 251, 252), C(214, 217, 222), C(60, 66, 76), Mat.Plastic, C(250, 251, 252), C(196, 200, 206), C(60, 140, 230),
            C(90, 160, 255), C(120, 180, 255), 4, keyEdge: C(180, 184, 190), bezel1: C(30, 32, 36), rim: C(230, 232, 236));
        d.BrandFont = "Segoe UI Light"; d.BrandItalic = false; d.BrandSize = 20;
        d.GlowRings = false;
        return d;
    }

    static RadioDesign Milspec()
    {
        var d = New("MILSPEC MS-4", "MILSPEC", "MS-4  ·  RUGGED", 1000, 520, 6);
        LayoutColumns(d, KeyKind.Rubber, KeyKind.Rubber);
        Look(d, FaceFinish.Matte, Mat.Plastic, C(96, 104, 72), C(70, 78, 50), C(46, 52, 32), C(230, 226, 190), C(240, 236, 200), C(200, 190, 120),
            Mat.Rubber, C(50, 54, 40), C(24, 26, 18), C(220, 220, 180), Mat.Rubber, C(44, 48, 34), C(18, 20, 14), C(255, 200, 60),
            C(140, 255, 120), C(140, 255, 120), 3);
        d.Bolts = true;
        d.BrandFont = "Consolas"; d.BrandItalic = false;
        return d;
    }

    static RadioDesign Cruisemaster()
    {
        var d = New("CRUISEMASTER 70", "Cruisemaster", "CM-70  ·  AUTO TUNE", 1040, 400, 26);
        LayoutDial(d, KeyKind.Chrome);
        Look(d, FaceFinish.Wood, Mat.Varnish, C(150, 90, 50), C(110, 62, 30), C(70, 38, 18), C(245, 230, 190), C(250, 236, 200), C(230, 230, 225),
            Mat.Chrome, C(244, 244, 240), C(130, 130, 126), C(60, 30, 10), Mat.Chrome, C(240, 240, 236), C(120, 120, 116), C(200, 40, 30),
            C(255, 180, 90), C(255, 180, 90), 2, bezelMat: Mat.Chrome, bezel1: C(236, 236, 232), bezel2: C(140, 140, 135), rim: C(220, 220, 215));
        d.ChromeTrim = true;
        return d;
    }

    static RadioDesign Titan()
    {
        var d = New("TITAN AMP 2400", "TITAN", "TA-2400  ·  CLASS A", 900, 620, 8);
        LayoutDoubleDin(d, KeyKind.Rubber, KeyKind.Rubber);
        Look(d, FaceFinish.Brushed, Mat.Brushed, C(150, 154, 160), C(104, 108, 114), C(66, 70, 76), C(20, 22, 26), C(20, 22, 26), C(120, 220, 255),
            Mat.Rubber, C(44, 46, 52), C(18, 19, 22), C(210, 235, 255), Mat.Brushed, C(200, 204, 210), C(90, 94, 100), C(120, 220, 255),
            C(100, 210, 255), C(120, 220, 255), 0);
        d.Bolts = true;
        d.BrandFont = "Arial Black"; d.BrandItalic = false;
        return d;
    }

    static RadioDesign Pocketsonic()
    {
        var d = New("POCKETSONIC PS-89", "POCKETSONIC", "PS-89  ·  HANDHELD", 640, 700, 30);
        LayoutTall(d, KeyKind.Rubber, KeyKind.Round);
        Look(d, FaceFinish.Matte, Mat.Plastic, C(200, 198, 196), C(180, 178, 176), C(150, 148, 146), C(40, 40, 110), C(40, 40, 110), C(140, 30, 90),
            Mat.Rubber, C(120, 116, 128), C(80, 76, 88), C(240, 240, 250), Mat.Plastic, C(150, 30, 90), C(90, 10, 50), C(255, 255, 255),
            C(150, 255, 120), C(150, 255, 120), 3, bezel1: C(90, 92, 100), bezel2: C(50, 52, 58));
        d.BrandFont = "Segoe UI Black"; d.BrandItalic = true;
        d.DomeColor = C(150, 30, 90);
        return d;
    }

    static RadioDesign Suburbia()
    {
        var d = New("SUBURBIA 99", "SUBURBIA", "SB-99  ·  CD/MP3", 980, 420, 14);
        LayoutDin(d, KeyKind.Rubber, KeyKind.Rubber, KeyKind.OvalTilt);
        Look(d, FaceFinish.Matte, Mat.Plastic, C(40, 60, 110), C(26, 40, 80), C(14, 22, 50), C(210, 225, 255), C(255, 255, 255), C(120, 170, 255),
            Mat.Rubber, C(50, 70, 120), C(22, 32, 64), C(220, 235, 255), Mat.Plastic, C(60, 80, 130), C(20, 30, 60), C(255, 255, 255),
            C(100, 160, 255), C(110, 170, 255), 4);
        return d;
    }

    static RadioDesign Lowrider()
    {
        var d = New("LOWRIDER GOLD", "LOWRIDER", "LR-64  ·  HYDRAULIC", 980, 500, 30);
        LayoutHub(d, KeyKind.Chrome, KeyKind.Chrome, KeyKind.Chrome);
        Look(d, FaceFinish.Gloss, Mat.Gloss, C(110, 20, 40), C(70, 10, 24), C(36, 4, 12), C(250, 220, 140), C(255, 226, 150), C(240, 200, 100),
            Mat.Chrome, C(252, 232, 170), C(180, 140, 50), C(90, 10, 20), Mat.Chrome, C(255, 236, 170), C(170, 128, 40), C(140, 10, 30),
            C(255, 200, 90), C(255, 200, 90), 2, bezelMat: Mat.Chrome, bezel1: C(250, 220, 140), bezel2: C(150, 110, 30), rim: C(255, 220, 140));
        d.ChromeTrim = true; d.Pinstripe = true;
        d.BrandFont = "Georgia"; d.BrandSize = 16;
        return d;
    }

    static RadioDesign Tapeworks()
    {
        var d = New("TAPEWORKS 84", "TAPEWORKS", "TW-84  ·  AUTO REVERSE", 1180, 300, 10);
        LayoutSlim(d, KeyKind.Pill, KeyKind.Pill);
        d.ShowSlot = true; d.Slot = new(240, 24, 372, 6);
        d.GlassY += 6;
        Look(d, FaceFinish.Brushed, Mat.Brushed, C(210, 212, 214), C(160, 162, 166), C(118, 120, 124), C(40, 44, 50), C(40, 44, 50), C(40, 160, 90),
            Mat.Plastic, C(70, 72, 78), C(28, 29, 32), C(230, 232, 236), Mat.Chrome, C(236, 238, 242), C(110, 112, 118), C(40, 160, 90),
            C(80, 255, 140), C(90, 255, 150), 3);
        d.BrandFont = "Segoe UI Semibold"; d.BrandItalic = false;
        Print(d, "DOLBY-FREE  ·  CrO2 / METAL", 700, 268, 300, 14, TextAlign.Center, 7);
        return d;
    }

    static RadioDesign Orbit()
    {
        var d = New("ORBIT O8", "ORBIT", "O8  ·  8-INCH HD", 1060, 560, 24);
        Glass(d, 40, 40, 466, 1.45f, 12, 12);
        ModernGlass(d, C(255, 150, 60));
        d.Finish = FaceFinish.Matte; d.FaceMaterial = Mat.Rubber;
        d.Face1 = C(44, 44, 48); d.Face2 = C(26, 26, 29); d.Face3 = C(12, 12, 14);
        d.BrandAt = new(40, 520); d.BrandSize = 13;
        d.ModelAt = new(700, 522);
        d.TheftLed = new(1030, 30);
        // A big round controller on the right, touch keys around it.
        float cx = 908, cy = 250;
        Btn[] ring = [Btn.Open, Btn.Folder, Btn.List, Btn.Settings, Btn.Vis, Btn.Mute, Btn.Shuffle, Btn.Repeat, Btn.Stop, Btn.Power];
        for (int i = 0; i < ring.Length; i++)
        {
            double a = (-90 + i * 36) * Math.PI / 180;
            d.Keys.Add(T(ring[i], KeyKind.Touch, cx + 130 * (float)Math.Cos(a) - 22, cy + 130 * (float)Math.Sin(a) - 22, 44, 44));
        }
        d.Keys.Add(Knob(Btn.Knob, cx, cy, 78, LabelAt.None));
        d.Keys.Add(T(Btn.Prev, KeyKind.Touch, 180, 488, 60, 40));
        d.Keys.Add(T(Btn.Play, KeyKind.Touch, 250, 486, 64, 44));
        d.Keys.Add(T(Btn.Next, KeyKind.Touch, 324, 488, 60, 40));
        d.Keys.Add(T(Btn.SeekBack, KeyKind.Touch, 400, 488, 56, 40));
        d.Keys.Add(T(Btn.SeekFwd, KeyKind.Touch, 464, 488, 56, 40));
        d.Keys.Add(T(Btn.VolDown, KeyKind.Touch, 812, 470, 56, 40));
        d.Keys.Add(T(Btn.VolUp, KeyKind.Touch, 948, 470, 56, 40));
        d.Keys.Add(T(Btn.Eject, KeyKind.Touch, 560, 488, 56, 40));
        d.Keys.Add(Knob(Btn.SpeedKnob, 908, 490, 20, LabelAt.None));
        d.Palette = 2;
        return d;
    }

    static RadioDesign Nebula()
    {
        var d = New("NEBULA X", "NEBULA", "X  ·  12-INCH PANORAMA", 1200, 520, 20);
        Glass(d, 120, 30, 700, 1.37f, 10, 12);
        ModernGlass(d, C(170, 110, 255));
        d.Finish = FaceFinish.Gloss; d.FaceMaterial = Mat.Gloss;
        d.Face1 = C(28, 24, 40); d.Face2 = C(16, 14, 24); d.Face3 = C(6, 6, 10);
        d.NeonBezel = true; d.BezelRim = C(170, 110, 255);
        d.BrandAt = new(1080, 18); d.BrandSize = 12; d.BrandAlign = TextAlign.Right;
        d.ModelAt = new(1080, 494);
        d.TheftLed = new(30, 30);
        Btn[] left = [Btn.Power, Btn.Open, Btn.Folder, Btn.List, Btn.Settings, Btn.Vis];
        Btn[] right = [Btn.VolUp, Btn.VolDown, Btn.Mute, Btn.Shuffle, Btn.Repeat, Btn.Stop];
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(T(left[i], KeyKind.Touch, 34, 50 + i * 66, 52, 52));
            d.Keys.Add(T(right[i], KeyKind.Touch, 1114, 50 + i * 66, 52, 52));
        }
        d.Keys.Add(T(Btn.SeekBack, KeyKind.Touch, 30, 452, 60, 40));
        d.Keys.Add(T(Btn.SeekFwd, KeyKind.Touch, 1110, 452, 60, 40));
        d.Keys.Add(T(Btn.Prev, KeyKind.Touch, 520, 466, 64, 40));
        d.Keys.Add(T(Btn.Play, KeyKind.Touch, 594, 462, 70, 48));
        d.Keys.Add(T(Btn.Next, KeyKind.Touch, 674, 466, 64, 40));
        d.Keys.Add(T(Btn.Eject, KeyKind.Touch, 760, 466, 56, 40));
        d.Keys.Add(Knob(Btn.Knob, 300, 486, 26, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, 900, 486, 22, LabelAt.None));
        d.Palette = 4;
        return d;
    }
}
