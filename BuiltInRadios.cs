namespace RetroRadio;

/// <summary>The radio models that ship with the app.</summary>
static partial class BuiltInRadios
{
    static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    static readonly (Btn Id, string Label)[] Fn =
        [(Btn.Open, "OPEN"), (Btn.Folder, "FOLDER"), (Btn.List, "LIST"), (Btn.Settings, "SETTINGS"), (Btn.Vis, "VIS"), (Btn.Mute, "MUTE")];
    static readonly (Btn Id, string Label)[] Ctl =
        [(Btn.VolUp, "VOL +"), (Btn.VolDown, "VOL -"), (Btn.Shuffle, "SHUF"), (Btn.Repeat, "RPT"), (Btn.Stop, "STOP"), (Btn.Power, "POWER")];
    static readonly string[] FnShort = ["OPEN", "DIR", "LIST", "SET", "VIS", "MUTE"];
    static readonly string[] CtlShort = ["VOL+", "VOL-", "SHUF", "RPT", "STOP", "PWR"];

    static KeyDesign K(Btn id, KeyKind kind, float x, float y, float w, float h, string label = "", LabelAt at = LabelAt.On, float tilt = 0, bool mirror = false) =>
        new() { Id = id, Kind = kind, X = x, Y = y, W = w, H = h, Label = label, LabelAt = at, Tilt = tilt, Mirror = mirror };

    static KeyDesign Knob(Btn id, float cx, float cy, float r, LabelAt at) =>
        new() { Id = id, Kind = KeyKind.Knob, X = cx - r, Y = cy - r, W = r * 2, H = r * 2, Label = id == Btn.Knob ? "VOLUME" : "SPEED", LabelAt = at };

    static RadioDesign New(string name, string brand, string model, float w, float h, float corner)
    {
        var d = new RadioDesign
        {
            Name = name, Brand = brand, Model = model,
            Width = w, Height = h,
            Face = new(14, 14, w - 28, h - 28),
            Corner = corner,
        };
        return d;
    }

    static void Glass(RadioDesign d, float x, float y, float dispW, float k, float bezelPad = 16, float bezelBottom = 22)
    {
        d.GlassX = x; d.GlassY = y; d.DisplayWidth = dispW; d.DisplayScale = k;
        var g = d.Glass;
        d.Bezel = new(g.X - bezelPad, g.Y - bezelPad, g.Width + bezelPad * 2, g.Height + bezelPad + bezelBottom);
        d.Leds = [new(g.X + 8, g.Bottom + bezelBottom / 2 - 2), new(g.Right - 20, g.Bottom + bezelBottom / 2 - 2)];
        d.Prints.Add(new() { Text = "DOT MATRIX VFD  ·  3D SPECTRUM ANALYZER", X = d.Bezel.X, Y = g.Bottom + 3, W = d.Bezel.W, H = bezelBottom - 6, Align = TextAlign.Center, Size = 7 });
    }

    static void Print(RadioDesign d, string text, float x, float y, float w, float h, TextAlign align = TextAlign.Center, float size = 7.5f) =>
        d.Prints.Add(new() { Text = text, X = x, Y = y, W = w, H = h, Align = align, Size = size });

    public static List<RadioDesign> All()
    {
        List<RadioDesign> list =
        [
            Silver(), Midnight(), Neon(), Woodgrain(), Carbon(),
            Royal(), Field(), Aura(), Imperial(), Xplosion(),
            BelAire(), Shadow(), Groove(), Hauler(), Bubble(),
            Prism(), NovaTab(), AuroraStrip(), VectorPortrait(),
        ];
        // Night illumination colour of each model.
        Color[] night =
        [
            C(90, 170, 255), C(120, 160, 255), C(255, 90, 210), C(255, 170, 70), C(255, 60, 50),
            C(120, 255, 140), C(120, 255, 120), C(180, 215, 255), C(255, 190, 100), C(255, 140, 40),
            C(255, 180, 90), C(255, 50, 50), C(255, 170, 70), C(120, 255, 140), C(255, 110, 200),
        ];
        for (int i = 0; i < night.Length; i++) list[i].NightLight = night[i];
        list.AddRange(More());
        return list;
    }

    // ───────────────────────────── 1. Silver 2000 ─────────────────────────────

    static RadioDesign Silver()
    {
        var d = New("SILVER 2000", "SONARIX", "RD-W2026  DSP", 960, 540, 18);
        Glass(d, 164, 90, 632, 1f, 14, 24);
        d.BrandAt = new(40, 26); d.BrandSize = 20;
        d.ModelAt = new(918, 32);
        d.ShowSlot = true; d.Slot = new(330, 42, 300, 6);
        d.ShowIr = true; d.Ir = new(742, 37, 26, 16);
        d.ShowAux = true; d.Aux = new(196, 498);
        d.ShowUsb = true; d.Usb = new(222, 489, 40, 18);
        d.TheftLed = new(150, 398);
        for (int i = 0; i < 6; i++)
        {
            float y = 92 + i * 44;
            d.Keys.Add(K(Fn[i].Id, KeyKind.OvalTilt, 34, y, 54, 22, Fn[i].Label, LabelAt.Right, -12));
            d.Keys.Add(K(Ctl[i].Id, KeyKind.OvalTilt, 872, y, 54, 22, Ctl[i].Label, LabelAt.Left, 12));
        }
        d.Keys.Add(K(Btn.Play, KeyKind.Dome, 434, 408, 92, 52));
        d.Keys.Add(K(Btn.Prev, KeyKind.ChevronL, 312, 410, 146, 48));
        d.Keys.Add(K(Btn.Next, KeyKind.ChevronR, 502, 410, 146, 48));
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Flat, 178, 418, 104, 32, "SEEK"));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Flat, 678, 418, 104, 32, "SEEK"));
        d.Keys.Add(K(Btn.Eject, KeyKind.Flat, 664, 35, 50, 20));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 744, 488, 76, 20, "DETACH"));
        d.Keys.Add(Knob(Btn.SpeedKnob, 96, 446, 32, LabelAt.Above));
        d.Keys.Add(Knob(Btn.Knob, 864, 446, 32, LabelAt.Above));
        d.Beds.Add(new() { X = 300, Y = 402, W = 360, H = 64, Radius = 30 });
        Print(d, "MOSFET 50W × 4   ·   MP3 / WMA / FLAC   ·   24-BIT DSP", 290, 486, 440, 22);
        return d; // colors: the RadioDesign defaults are this model
    }

    // ───────────────────────────── 2. Midnight 99 ─────────────────────────────

    static RadioDesign Midnight()
    {
        var d = New("MIDNIGHT 99", "KAIZEN", "KX-990  MD/CD", 900, 560, 8);
        Glass(d, 58, 84, 600, 1f, 18, 22);
        d.BrandAt = new(40, 22); d.BrandSize = 17;
        d.ModelAt = new(40, 46); d.ModelAlign = TextAlign.Left;
        d.ShowSlot = true; d.Slot = new(212, 34, 356, 6);
        d.ShowIr = true; d.Ir = new(604, 29, 26, 16);
        d.ShowAux = true; d.Aux = new(676, 406);
        d.ShowUsb = true; d.Usb = new(698, 397, 44, 18);
        d.TheftLed = new(170, 420);
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Ctl[i].Id, KeyKind.Rubber, 702, 70 + i * 50, 156, 38, Ctl[i].Label));
            d.Keys.Add(K(Fn[i].Id, KeyKind.Round, 198 + i * 64, 386, 44, 44, Fn[i].Label, LabelAt.Below));
        }
        d.Keys.Add(K(Btn.Prev, KeyKind.Rubber, 196, 472, 84, 34));
        d.Keys.Add(K(Btn.Play, KeyKind.Rubber, 286, 472, 84, 34));
        d.Keys.Add(K(Btn.Next, KeyKind.Rubber, 376, 472, 84, 34));
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Rubber, 466, 472, 84, 34, "SEEK"));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Rubber, 556, 472, 84, 34, "SEEK"));
        d.Keys.Add(K(Btn.Eject, KeyKind.Rubber, 778, 26, 84, 22, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 664, 478, 84, 22, "DETACH"));
        d.Keys.Add(Knob(Btn.Knob, 110, 464, 46, LabelAt.Above));
        d.Keys.Add(Knob(Btn.SpeedKnob, 810, 476, 30, LabelAt.Above));
        d.Beds.Add(new() { X = 184, Y = 378, W = 470, H = 140, Radius = 10 });
        Print(d, "DSP   ·   MD / CD RECEIVER   ·   45W × 4", 184, 522, 470, 16);

        d.Finish = FaceFinish.Matte; d.FaceMaterial = Mat.Plastic;
        d.Face1 = C(62, 64, 70); d.Face2 = C(36, 37, 41); d.Face3 = C(20, 20, 23);
        d.Print = C(165, 172, 185); d.BrandInk = C(215, 222, 235); d.Trim = C(230, 40, 40);
        d.Bezel1 = C(18, 19, 22); d.Bezel2 = C(3, 3, 4); d.BezelRim = C(84, 88, 96);
        d.KeyMaterial = Mat.Backlit; d.Key1 = C(46, 86, 175); d.Key2 = C(12, 26, 70); d.KeyEdge = C(8, 12, 24); d.KeyText = C(225, 238, 255);
        d.TransportMaterial = Mat.Backlit; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.BacklitLabels = true;
        d.KnobMaterial = Mat.Rubber; d.Knob1 = C(54, 56, 62); d.Knob2 = C(12, 12, 14); d.Pointer = C(120, 180, 255);
        d.Glow = C(40, 110, 255); d.Led = C(255, 40, 40); d.Palette = 4;
        return d;
    }

    // ───────────────────────────── 3. Neon 2003 ─────────────────────────────

    static RadioDesign Neon()
    {
        var d = New("NEON 2003", "NEON TRAX", "NX-7  GLOSS", 1180, 340, 26);
        Glass(d, 345, 58, 700, 0.7f, 16, 16);
        d.BrandAt = new(40, 14); d.BrandSize = 14;
        d.ModelAt = new(1146, 16);
        d.ShowSeam = true; d.Seam = new(30, 38, 1120, 2);
        d.ShowIr = true; d.Ir = new(930, 18, 24, 14);
        d.ShowAux = true; d.Aux = new(62, 294);
        d.ShowUsb = true; d.Usb = new(84, 285, 44, 18);
        d.TheftLed = new(196, 244);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 2; c++)
            {
                int i = r * 2 + c;
                d.Keys.Add(K(Fn[i].Id, KeyKind.Pill, 196 + c * 66, 68 + r * 42, 60, 28, Fn[i].Label));
                d.Keys.Add(K(Ctl[i].Id, KeyKind.Pill, 858 + c * 66, 68 + r * 42, 60, 28, Ctl[i].Label));
            }
        }
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Pill, 196, 196, 126, 28, "SEEK"));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Pill, 858, 196, 126, 28, "SEEK"));
        d.Keys.Add(K(Btn.Play, KeyKind.Dome, 563, 263, 54, 54));
        d.Keys.Add(K(Btn.Prev, KeyKind.Pill, 440, 274, 112, 32));
        d.Keys.Add(K(Btn.Next, KeyKind.Pill, 628, 274, 112, 32));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, 862, 17, 56, 18));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 146, 284, 76, 20, "DETACH"));
        d.Keys.Add(Knob(Btn.Knob, 112, 164, 60, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, 1068, 164, 60, LabelAt.Below));
        d.Beds.Add(new() { X = 428, Y = 268, W = 324, H = 44, Radius = 22 });
        Print(d, "250W MAX  ·  MP3 · WMA · FLAC", 860, 286, 290, 18, TextAlign.Right);

        d.Finish = FaceFinish.Gloss; d.FaceMaterial = Mat.Gloss;
        d.Face1 = C(48, 42, 62); d.Face2 = C(14, 12, 20); d.Face3 = C(4, 3, 6);
        d.Print = C(225, 150, 255); d.BrandInk = C(255, 120, 230); d.Trim = C(255, 60, 200);
        d.Bezel1 = C(8, 6, 12); d.Bezel2 = C(0, 0, 0); d.BezelRim = C(255, 60, 200); d.NeonBezel = true;
        d.KeyMaterial = Mat.Gloss; d.Key1 = C(42, 32, 54); d.Key2 = C(10, 8, 14); d.KeyEdge = C(255, 60, 200); d.KeyText = C(255, 195, 245);
        d.TransportMaterial = Mat.Gloss; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.NeonKeys = true; d.DomeColor = C(255, 60, 200);
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(215, 215, 225); d.Knob2 = C(70, 70, 82); d.Pointer = C(255, 80, 210);
        d.Glow = C(255, 60, 200); d.Led = C(255, 60, 200); d.Palette = 6;
        return d;
    }

    // ───────────────────────────── 4. Woodgrain 84 ─────────────────────────────

    static RadioDesign Woodgrain()
    {
        var d = New("WOODGRAIN 84", "CRESTLINE", "CR-84  STEREO", 1000, 400, 4);
        Glass(d, 254, 40, 632, 0.78f, 18, 22);
        d.Prints.Clear();
        Print(d, "AM/FM STEREO  ·  AUTO REVERSE  ·  DOLBY NR", d.Bezel.X, d.Glass.Bottom + 4, d.Bezel.W, 14, TextAlign.Center, 7);
        d.BrandAt = new(110, 30); d.BrandSize = 17; d.BrandAlign = TextAlign.Center; d.BrandFont = "Georgia";
        d.ModelAt = new(890, 36); d.ModelAlign = TextAlign.Center;
        d.ShowAux = true; d.Aux = new(110, 346);
        d.TheftLed = new(944, 36);
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Fn[i].Id, KeyKind.Chrome, 236 + i * 55, 276, 51, 52, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, KeyKind.Chrome, 236 + i * 55, 336, 51, 28, Ctl[i].Label));
        }
        d.Keys.Add(K(Btn.Prev, KeyKind.Chrome, 576, 276, 60, 52));
        d.Keys.Add(K(Btn.Play, KeyKind.Chrome, 640, 276, 60, 52));
        d.Keys.Add(K(Btn.Next, KeyKind.Chrome, 704, 276, 60, 52));
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Chrome, 576, 336, 92, 28, "SEEK"));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Chrome, 672, 336, 92, 28, "SEEK"));
        d.Keys.Add(K(Btn.Eject, KeyKind.Chrome, 850, 292, 80, 28, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Chrome, 70, 292, 80, 28, "RELEASE"));
        d.Keys.Add(Knob(Btn.Knob, 110, 170, 56, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, 890, 170, 56, LabelAt.Below));
        d.Beds.Add(new() { X = 226, Y = 268, W = 548, H = 104, Radius = 4 });

        d.Finish = FaceFinish.Wood; d.FaceMaterial = Mat.Varnish; d.ChromeTrim = true;
        d.Face1 = C(136, 80, 40); d.Face2 = C(100, 57, 27); d.Face3 = C(62, 34, 15);
        d.Print = C(245, 232, 200); d.BrandInk = C(240, 226, 190); d.Trim = C(215, 215, 210);
        d.BezelMaterial = Mat.Chrome; d.Bezel1 = C(225, 225, 220); d.Bezel2 = C(140, 140, 135); d.BezelRim = C(215, 215, 210);
        d.KeyMaterial = Mat.Chrome; d.Key1 = C(242, 242, 238); d.Key2 = C(128, 128, 124); d.KeyEdge = C(60, 60, 60); d.KeyText = C(40, 40, 40);
        d.TransportMaterial = Mat.Chrome; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(238, 238, 232); d.Knob2 = C(112, 112, 106); d.Pointer = C(30, 30, 30);
        d.GlowRings = false; d.Led = C(255, 170, 40); d.Palette = 3;
        return d;
    }

    // ───────────────────────────── 5. Carbon Race ─────────────────────────────

    static RadioDesign Carbon()
    {
        var d = New("CARBON RACE", "VORTEX", "GT-R 5000", 900, 600, 10);
        Glass(d, 50, 60, 740, 1.08f, 16, 18);
        d.BrandAt = new(40, 14); d.BrandSize = 17; d.BrandFont = "Arial Black";
        d.ModelAt = new(862, 18);
        d.ShowSlot = true; d.Slot = new(310, 25, 280, 6);
        d.ShowIr = true; d.Ir = new(724, 21, 24, 15);
        d.ShowAux = true; d.Aux = new(588, 562);
        d.ShowUsb = true; d.Usb = new(606, 553, 40, 18);
        d.TheftLed = new(566, 392);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 2; c++)
            {
                int i = r * 2 + c;
                d.Keys.Add(K(Fn[i].Id, KeyKind.Angled, 40 + c * 112, 382 + r * 44, 106, 34, Fn[i].Label));
                d.Keys.Add(K(Ctl[i].Id, KeyKind.Angled, 642 + c * 112, 382 + r * 44, 106, 34, Ctl[i].Label, mirror: true));
            }
        }
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Angled, 40, 516, 218, 34, "SEEK"));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Angled, 642, 516, 218, 34, "SEEK", mirror: true));
        d.Keys.Add(K(Btn.Prev, KeyKind.Angled, 280, 450, 88, 38));
        d.Keys.Add(K(Btn.Next, KeyKind.Angled, 532, 450, 88, 38, mirror: true));
        d.Keys.Add(K(Btn.Play, KeyKind.Angled, 400, 548, 100, 30));
        d.Keys.Add(K(Btn.Eject, KeyKind.Angled, 640, 20, 64, 20));
        d.Keys.Add(K(Btn.Detach, KeyKind.Angled, 760, 558, 100, 22, "DETACH", mirror: true));
        d.Keys.Add(Knob(Btn.Knob, 450, 470, 70, LabelAt.Above));
        d.Keys.Add(Knob(Btn.SpeedKnob, 318, 404, 22, LabelAt.Below));
        d.Beds.Add(new() { X = 272, Y = 382, W = 356, H = 200, Radius = 14 });
        Print(d, "4 × 60W  ·  MP3 / FLAC", 40, 556, 218, 20, TextAlign.Left);

        d.Finish = FaceFinish.Carbon; d.FaceMaterial = Mat.Clearcoat; d.Pinstripe = true;
        d.Face1 = C(46, 46, 50); d.Face2 = C(26, 26, 28); d.Face3 = C(10, 10, 11);
        d.Print = C(225, 225, 228); d.BrandInk = C(255, 45, 40); d.Trim = C(255, 45, 40);
        d.Bezel1 = C(10, 10, 12); d.Bezel2 = C(1, 1, 1); d.BezelRim = C(255, 45, 40); d.NeonBezel = true;
        d.KeyMaterial = Mat.Rubber; d.Key1 = C(62, 62, 66); d.Key2 = C(18, 18, 20); d.KeyEdge = C(255, 45, 40); d.KeyText = C(240, 240, 240);
        d.TransportMaterial = Mat.Rubber; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Rubber; d.Knob1 = C(64, 64, 68); d.Knob2 = C(12, 12, 13); d.Pointer = C(255, 70, 50);
        d.Glow = C(255, 40, 30); d.Led = C(255, 45, 40); d.Palette = 5;
        return d;
    }

    // ───────────────────────────── 6. Royal 76 ─────────────────────────────

    static RadioDesign Royal()
    {
        var d = New("ROYAL 76", "ROYALTONE", "RT-76  AUTOREVERSE", 1040, 300, 3);
        Glass(d, 44, 58, 632, 0.62f, 14, 16);
        d.BrandAt = new(44, 22); d.BrandSize = 14; d.BrandFont = "Times New Roman";
        d.ModelAt = new(800, 22);
        d.ShowSlot = true; d.Slot = new(484, 74, 302, 5);
        d.ShowAux = true; d.Aux = new(990, 254);
        d.TheftLed = new(846, 40);
        // Cassette door with its keys underneath.
        d.Beds.Add(new() { X = 470, Y = 50, W = 330, H = 114, Radius = 4 });
        Print(d, "AUTO REVERSE  ·  DOLBY B NR  ·  METAL", 480, 144, 310, 14);
        Btn[] deck = [Btn.SeekBack, Btn.Prev, Btn.Play, Btn.Next, Btn.SeekFwd, Btn.Eject];
        for (int i = 0; i < deck.Length; i++) d.Keys.Add(K(deck[i], KeyKind.Chrome, 470 + i * 56, 182, 50, 40));
        for (int i = 0; i < 12; i++)
        {
            var (id, label) = i < 6 ? Fn[i] : Ctl[i - 6];
            d.Keys.Add(K(id, KeyKind.Chrome, 44 + i * 63.5f, 244, 58, 26, label));
        }
        d.Keys.Add(Knob(Btn.Knob, 912, 112, 44, LabelAt.Left));
        d.Keys.Add(Knob(Btn.SpeedKnob, 912, 214, 30, LabelAt.Left));

        d.Finish = FaceFinish.Plain; d.FaceMaterial = Mat.Plastic; d.ChromeTrim = true;
        d.Face1 = C(38, 38, 40); d.Face2 = C(26, 26, 28); d.Face3 = C(16, 16, 17);
        d.Print = C(200, 200, 205); d.BrandInk = C(232, 232, 236); d.Trim = C(200, 200, 200);
        d.Bezel1 = C(20, 20, 22); d.Bezel2 = C(5, 5, 6); d.BezelRim = C(160, 160, 160);
        d.KeyMaterial = Mat.Chrome; d.Key1 = C(240, 240, 236); d.Key2 = C(126, 126, 122); d.KeyEdge = C(50, 50, 50); d.KeyText = C(35, 35, 35);
        d.TransportMaterial = Mat.Chrome; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(236, 236, 232); d.Knob2 = C(110, 110, 106); d.Pointer = C(25, 25, 25);
        d.GlowRings = false; d.Led = C(255, 170, 40); d.Palette = 2;
        return d;
    }

    // ───────────────────────────── 7. Field Unit ─────────────────────────────

    static RadioDesign Field()
    {
        var d = New("FIELD UNIT", "FIELDCOM", "FX-1  RUGGED", 760, 640, 14);
        Glass(d, 57, 64, 680, 0.95f, 16, 18);
        d.BrandAt = new(44, 20); d.BrandSize = 18; d.BrandFont = "Consolas"; d.BrandItalic = false;
        d.ModelAt = new(560, 24);
        d.ShowAux = true; d.Aux = new(604, 604);
        d.ShowUsb = true; d.Usb = new(624, 595, 44, 18);
        d.TheftLed = new(716, 350);
        for (int i = 0; i < 12; i++)
        {
            var (id, label) = i < 6 ? Fn[i] : Ctl[i - 6];
            d.Keys.Add(K(id, KeyKind.Rubber, 60 + i % 4 * 122, 350 + i / 4 * 56, 110, 44, label));
        }
        Btn[] tr = [Btn.SeekBack, Btn.Prev, Btn.Play, Btn.Next, Btn.SeekFwd];
        for (int i = 0; i < tr.Length; i++)
            d.Keys.Add(K(tr[i], KeyKind.Rubber, 60 + i * 98, 530, 90, 46, tr[i] is Btn.SeekBack or Btn.SeekFwd ? "SEEK" : ""));
        d.Keys.Add(K(Btn.Eject, KeyKind.Rubber, 604, 20, 80, 24, "EJECT"));
        d.Keys.Add(Knob(Btn.Knob, 640, 418, 64, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, 640, 546, 28, LabelAt.Right));
        d.Beds.Add(new() { X = 46, Y = 338, W = 510, H = 250, Radius = 10 });
        Print(d, "WATERPROOF  ·  SHOCKPROOF  ·  MIL-SPEC", 46, 592, 510, 16);

        d.Finish = FaceFinish.Matte; d.FaceMaterial = Mat.Plastic; d.Bolts = true;
        d.Face1 = C(104, 110, 78); d.Face2 = C(78, 84, 56); d.Face3 = C(52, 56, 38);
        d.Print = C(232, 226, 190); d.BrandInk = C(240, 235, 200); d.Trim = C(200, 190, 140);
        d.BezelMaterial = Mat.Plastic; d.Bezel1 = C(32, 34, 26); d.Bezel2 = C(9, 9, 7); d.BezelRim = C(92, 98, 72);
        d.KeyMaterial = Mat.Rubber; d.Key1 = C(72, 76, 58); d.Key2 = C(40, 42, 30); d.KeyEdge = C(20, 22, 14); d.KeyText = C(232, 226, 190);
        d.TransportMaterial = Mat.Rubber; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Rubber; d.Knob1 = C(62, 64, 52); d.Knob2 = C(24, 25, 20); d.Pointer = C(255, 200, 60);
        d.GlowRings = false; d.Led = C(120, 255, 120); d.Palette = 3;
        return d;
    }

    // ───────────────────────────── 8. Aura ─────────────────────────────

    static RadioDesign Aura()
    {
        var d = New("AURA WHITE", "aura", "A1  TOUCH", 940, 480, 36);
        Glass(d, 170, 60, 600, 1f, 16, 20);
        d.BrandAt = new(40, 22); d.BrandSize = 20; d.BrandFont = "Segoe UI Light"; d.BrandItalic = false;
        d.ModelAt = new(900, 26);
        d.ShowSlot = true; d.Slot = new(320, 28, 300, 4);
        d.ShowAux = true; d.Aux = new(470, 448);
        d.TheftLed = new(900, 440);
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Fn[i].Id, KeyKind.Touch, 40, 70 + i * 46, 100, 34, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, KeyKind.Touch, 800, 70 + i * 46, 100, 34, Ctl[i].Label));
        }
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Touch, 230, 388, 90, 32, "SEEK"));
        d.Keys.Add(K(Btn.Prev, KeyKind.Touch, 330, 388, 90, 32));
        d.Keys.Add(K(Btn.Play, KeyKind.Dome, 430, 380, 80, 48));
        d.Keys.Add(K(Btn.Next, KeyKind.Touch, 520, 388, 90, 32));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Touch, 620, 388, 90, 32, "SEEK"));
        d.Keys.Add(K(Btn.Eject, KeyKind.Touch, 640, 20, 50, 20));
        d.Keys.Add(Knob(Btn.SpeedKnob, 100, 404, 36, LabelAt.None));
        d.Keys.Add(Knob(Btn.Knob, 840, 404, 36, LabelAt.None));

        d.Finish = FaceFinish.Plain; d.FaceMaterial = Mat.Gloss;
        d.Face1 = C(252, 252, 254); d.Face2 = C(234, 236, 240); d.Face3 = C(206, 209, 215);
        d.Print = C(135, 140, 150); d.BrandInk = C(110, 116, 128); d.Trim = C(200, 200, 205);
        d.Bezel1 = C(22, 22, 26); d.Bezel2 = C(4, 4, 6); d.BezelRim = C(200, 200, 205);
        d.KeyMaterial = Mat.Gloss; d.Key1 = d.Face1; d.Key2 = d.Face2; d.KeyEdge = C(215, 218, 224); d.KeyText = C(120, 126, 138);
        d.TransportMaterial = Mat.Gloss; d.Transport1 = d.Face1; d.Transport2 = d.Face2; d.TransportText = d.KeyText;
        d.DomeColor = C(235, 238, 245);
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(236, 237, 241); d.Knob2 = C(150, 152, 160); d.Pointer = C(60, 130, 255);
        d.Glow = C(150, 200, 255); d.Led = C(120, 180, 255); d.Palette = 0;
        return d;
    }

    // ───────────────────────────── 9. Imperial Gold ─────────────────────────────

    static RadioDesign Imperial()
    {
        var d = New("IMPERIAL GOLD", "IMPERIAL", "G-9000  REFERENCE", 960, 530, 12);
        Glass(d, 165, 56, 700, 0.9f, 16, 20);
        d.BrandAt = new(480, 476); d.BrandSize = 17; d.BrandAlign = TextAlign.Center; d.BrandFont = "Georgia";
        d.ModelAt = new(480, 500); d.ModelAlign = TextAlign.Center;
        d.ShowAux = true; d.Aux = new(78, 282);
        d.ShowUsb = true; d.Usb = new(98, 273, 44, 18);
        d.ShowIr = true; d.Ir = new(868, 276, 26, 16);
        d.TheftLed = new(900, 300);
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Fn[i].Id, KeyKind.Chrome, 145 + i * 112, 326, 102, 34, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, KeyKind.Chrome, 145 + i * 112, 370, 102, 34, Ctl[i].Label));
        }
        Btn[] tr = [Btn.SeekBack, Btn.Prev, Btn.Play, Btn.Next, Btn.SeekFwd];
        for (int i = 0; i < tr.Length; i++)
            d.Keys.Add(K(tr[i], KeyKind.Chrome, 205 + i * 112, 418, 102, 40, tr[i] is Btn.SeekBack or Btn.SeekFwd ? "SEEK" : ""));
        d.Keys.Add(K(Btn.Eject, KeyKind.Chrome, 40, 440, 90, 30, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Chrome, 830, 440, 90, 30, "RELEASE"));
        d.Keys.Add(Knob(Btn.Knob, 80, 164, 50, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, 880, 164, 50, LabelAt.Below));
        d.Beds.Add(new() { X = 134, Y = 316, W = 692, H = 150, Radius = 6 });

        d.Finish = FaceFinish.Brushed; d.FaceMaterial = Mat.Brushed;
        d.Face1 = C(240, 222, 172); d.Face2 = C(198, 170, 112); d.Face3 = C(140, 114, 64);
        d.Print = C(70, 55, 25); d.BrandInk = C(80, 60, 20); d.Trim = C(230, 200, 130);
        d.Bezel1 = C(26, 22, 16); d.Bezel2 = C(4, 3, 2); d.BezelRim = C(230, 200, 130);
        d.KeyMaterial = Mat.Chrome; d.Key1 = C(252, 234, 184); d.Key2 = C(170, 140, 80); d.KeyEdge = C(90, 70, 30); d.KeyText = C(60, 45, 15);
        d.TransportMaterial = Mat.Chrome; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(250, 232, 182); d.Knob2 = C(150, 120, 64); d.Pointer = C(60, 40, 10);
        d.GlowRings = false; d.Led = C(255, 200, 120); d.Palette = 2;
        return d;
    }

    // ───────────────────────────── 10. Xplosion ─────────────────────────────

    static RadioDesign Xplosion()
    {
        var d = New("XPLOSION", "XPLOSION", "XR-2002  MEGA BASS", 980, 500, 30);
        Glass(d, 340, 60, 632, 0.95f, 16, 16);
        d.BrandAt = new(40, 22); d.BrandSize = 22; d.BrandFont = "Arial Black";
        d.ModelAt = new(40, 58); d.ModelAlign = TextAlign.Left;
        d.ShowIr = true; d.Ir = new(820, 20, 22, 14);
        d.ShowAux = true; d.Aux = new(250, 452);
        d.ShowUsb = true; d.Usb = new(268, 443, 44, 18);
        d.TheftLed = new(300, 90);
        // Arc of round keys hugging the giant volume knob.
        for (int i = 0; i < 6; i++)
        {
            double a = (-55 + i * 22) * Math.PI / 180;
            float cx = 150 + 128 * (float)Math.Cos(a), cy = 250 + 128 * (float)Math.Sin(a);
            d.Keys.Add(K(Fn[i].Id, KeyKind.Round, cx - 21, cy - 21, 42, 42, FnShort[i]));
            d.Keys.Add(K(Ctl[i].Id, KeyKind.Pill, 330 + i * 104, 340, 96, 30, Ctl[i].Label));
        }
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Pill, 360, 392, 110, 34, "SEEK"));
        d.Keys.Add(K(Btn.Prev, KeyKind.Pill, 480, 392, 100, 34));
        d.Keys.Add(K(Btn.Play, KeyKind.Dome, 590, 380, 80, 56));
        d.Keys.Add(K(Btn.Next, KeyKind.Pill, 680, 392, 100, 34));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Pill, 790, 392, 110, 34, "SEEK"));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, 872, 17, 70, 20));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 580, 452, 100, 22, "DETACH"));
        d.Keys.Add(Knob(Btn.Knob, 150, 250, 88, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, 100, 432, 28, LabelAt.Right));
        Print(d, "MEGA BASS  ·  4 × 52W", 700, 452, 250, 20, TextAlign.Right);

        d.Finish = FaceFinish.Gloss; d.FaceMaterial = Mat.Gloss;
        d.Face1 = C(84, 88, 96); d.Face2 = C(40, 42, 48); d.Face3 = C(18, 19, 22);
        d.Print = C(255, 140, 40); d.BrandInk = C(255, 140, 40); d.Trim = C(255, 120, 20);
        d.Bezel1 = C(10, 10, 12); d.Bezel2 = C(0, 0, 0); d.BezelRim = C(255, 120, 20);
        d.KeyMaterial = Mat.Gloss; d.Key1 = C(58, 60, 68); d.Key2 = C(18, 19, 23); d.KeyEdge = C(255, 120, 20); d.KeyText = C(255, 165, 70);
        d.TransportMaterial = Mat.Gloss; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.NeonKeys = true; d.DomeColor = C(255, 120, 20);
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(222, 224, 230); d.Knob2 = C(80, 82, 90); d.Pointer = C(255, 120, 20);
        d.Glow = C(255, 120, 20); d.Led = C(255, 130, 30); d.Palette = 2;
        return d;
    }

    // ───────────────────────────── 11. Bel-Aire 57 ─────────────────────────────

    static RadioDesign BelAire()
    {
        var d = New("BEL-AIRE 57", "Bel-Aire", "WONDERBAR  57", 1040, 380, 22);
        Glass(d, 324, 112, 632, 0.62f, 16, 16);
        d.BrandAt = new(110, 290); d.BrandSize = 20; d.BrandAlign = TextAlign.Center; d.BrandFont = "Georgia";
        d.ModelAt = new(930, 292); d.ModelAlign = TextAlign.Center;
        d.ShowAux = true; d.Aux = new(930, 330);
        d.TheftLed = new(516, 296);
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Fn[i].Id, KeyKind.Chrome, 310 + i * 70, 36, 64, 50, Fn[i].Label));
            d.Keys.Add(K(Ctl[i].Id, KeyKind.Chrome, 310 + i * 70, 314, 64, 24, Ctl[i].Label));
        }
        d.Keys.Add(K(Btn.Prev, KeyKind.Chrome, 178, 112, 112, 38));
        d.Keys.Add(K(Btn.Play, KeyKind.Chrome, 178, 160, 112, 38));
        d.Keys.Add(K(Btn.Next, KeyKind.Chrome, 178, 208, 112, 38));
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Chrome, 750, 112, 112, 38, "SEEK"));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Chrome, 750, 160, 112, 38, "SEEK"));
        d.Keys.Add(K(Btn.Eject, KeyKind.Chrome, 750, 208, 112, 38, "EJECT"));
        d.Keys.Add(Knob(Btn.Knob, 100, 170, 58, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, 940, 170, 58, LabelAt.Below));

        d.Finish = FaceFinish.Plain; d.FaceMaterial = Mat.Gloss; d.ChromeTrim = true;
        d.Face1 = C(248, 240, 220); d.Face2 = C(228, 214, 186); d.Face3 = C(192, 176, 146);
        d.Print = C(130, 30, 30); d.BrandInk = C(170, 25, 25); d.Trim = C(200, 30, 30);
        d.BezelMaterial = Mat.Chrome; d.Bezel1 = C(236, 236, 232); d.Bezel2 = C(140, 140, 135); d.BezelRim = C(220, 220, 215);
        d.KeyMaterial = Mat.Chrome; d.Key1 = C(244, 244, 240); d.Key2 = C(128, 128, 124); d.KeyEdge = C(60, 60, 60); d.KeyText = C(120, 20, 20);
        d.TransportMaterial = Mat.Chrome; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(240, 240, 236); d.Knob2 = C(118, 118, 114); d.Pointer = C(200, 30, 30);
        d.GlowRings = false; d.Led = C(255, 170, 40); d.Palette = 2;
        return d;
    }

    // ───────────────────────────── 12. Shadow ─────────────────────────────

    static RadioDesign Shadow()
    {
        var d = New("SHADOW S1", "SHADOW", "S1", 900, 400, 10);
        Glass(d, 40, 36, 700, 0.95f, 12, 12);
        d.Prints.Clear();
        d.BrandAt = new(800, 356); d.BrandSize = 12; d.BrandAlign = TextAlign.Center; d.BrandFont = "Segoe UI Semibold"; d.BrandItalic = false;
        d.ModelAt = new(800, 20); d.ModelAlign = TextAlign.Center;
        d.ShowAux = true; d.Aux = new(760, 380);
        d.ShowUsb = true; d.Usb = new(816, 371, 44, 18);
        d.TheftLed = new(860, 30);
        for (int i = 0; i < 12; i++)
        {
            var (id, label) = i < 6 ? Fn[i] : Ctl[i - 6];
            d.Keys.Add(K(id, KeyKind.Flat, 28 + i * 57.5f, 304, 53, 28, label));
        }
        Btn[] tr = [Btn.SeekBack, Btn.Prev, Btn.Play, Btn.Next, Btn.SeekFwd, Btn.Eject];
        for (int i = 0; i < tr.Length; i++)
            d.Keys.Add(K(tr[i], KeyKind.Flat, 28 + i * 115, 342, 107, 30, tr[i] is Btn.SeekBack or Btn.SeekFwd ? "SEEK" : ""));
        d.Keys.Add(Knob(Btn.Knob, 800, 116, 60, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, 800, 270, 38, LabelAt.None));

        d.Finish = FaceFinish.Matte; d.FaceMaterial = Mat.Rubber;
        d.Face1 = C(36, 36, 38); d.Face2 = C(24, 24, 26); d.Face3 = C(14, 14, 15);
        d.Print = C(110, 110, 115); d.BrandInk = C(135, 135, 140); d.Trim = C(60, 60, 62);
        d.Bezel1 = C(10, 10, 11); d.Bezel2 = C(2, 2, 2); d.BezelRim = C(50, 50, 52);
        d.KeyMaterial = Mat.Rubber; d.Key1 = C(42, 42, 44); d.Key2 = C(22, 22, 24); d.KeyEdge = C(8, 8, 8); d.KeyText = C(150, 150, 156);
        d.TransportMaterial = Mat.Rubber; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Rubber; d.Knob1 = C(46, 46, 48); d.Knob2 = C(14, 14, 15); d.Pointer = C(255, 40, 40);
        d.GlowRings = false; d.Led = C(255, 30, 30); d.Palette = 5;
        return d;
    }

    // ───────────────────────────── 13. Groove 72 ─────────────────────────────

    static RadioDesign Groove()
    {
        var d = New("GROOVE 72", "Groovetron", "GT-72", 700, 620, 40);
        Glass(d, 192, 50, 632, 0.5f, 14, 16);
        d.Prints.Clear();
        d.BrandAt = new(350, 558); d.BrandSize = 20; d.BrandAlign = TextAlign.Center; d.BrandFont = "Cooper Black"; d.BrandItalic = false;
        d.ModelAt = new(350, 586); d.ModelAlign = TextAlign.Center;
        d.ShowAux = true; d.Aux = new(492, 572);
        d.TheftLed = new(350, 212);
        for (int i = 0; i < 12; i++)
        {
            var (id, label) = i < 6 ? Fn[i] : Ctl[i - 6];
            d.Keys.Add(K(id, KeyKind.Rubber, 44 + i % 4 * 154, 230 + i / 4 * 74, 140, 60, label));
        }
        Btn[] tr = [Btn.SeekBack, Btn.Prev, Btn.Play, Btn.Next, Btn.SeekFwd];
        for (int i = 0; i < tr.Length; i++)
            d.Keys.Add(K(tr[i], KeyKind.Rubber, 44 + i * 122, 460, 110, 70, tr[i] is Btn.SeekBack or Btn.SeekFwd ? "SEEK" : ""));
        d.Keys.Add(K(Btn.Eject, KeyKind.Rubber, 44, 552, 120, 36, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Rubber, 536, 552, 120, 36, "DETACH"));
        d.Keys.Add(Knob(Btn.Knob, 104, 110, 62, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, 596, 110, 62, LabelAt.Below));

        d.Finish = FaceFinish.Plain; d.FaceMaterial = Mat.Gloss;
        d.Face1 = C(244, 136, 44); d.Face2 = C(216, 106, 26); d.Face3 = C(170, 76, 16);
        d.Print = C(80, 40, 15); d.BrandInk = C(90, 40, 12); d.Trim = C(240, 200, 150);
        d.Bezel1 = C(64, 38, 22); d.Bezel2 = C(30, 15, 8); d.BezelRim = C(240, 200, 150);
        d.KeyMaterial = Mat.Plastic; d.Key1 = C(248, 240, 220); d.Key2 = C(210, 198, 172); d.KeyEdge = C(120, 80, 40); d.KeyText = C(100, 55, 20);
        d.TransportMaterial = Mat.Plastic; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Plastic; d.Knob1 = C(248, 244, 232); d.Knob2 = C(192, 182, 160); d.Pointer = C(40, 20, 10);
        d.GlowRings = false; d.Led = C(255, 170, 40); d.Palette = 2;
        return d;
    }

    // ───────────────────────────── 14. Hauler ─────────────────────────────

    static RadioDesign Hauler()
    {
        var d = New("HAULER HX", "HAULER", "HX-40  CB READY", 1100, 440, 8);
        Glass(d, 250, 50, 632, 0.85f, 16, 16);
        d.BrandAt = new(130, 22); d.BrandSize = 24; d.BrandAlign = TextAlign.Center; d.BrandFont = "Impact"; d.BrandItalic = false;
        d.ModelAt = new(130, 60); d.ModelAlign = TextAlign.Center;
        d.ShowAux = true; d.Aux = new(430, 392);
        d.ShowUsb = true; d.Usb = new(450, 383, 44, 18);
        d.ShowIr = true; d.Ir = new(560, 384, 26, 16);
        d.TheftLed = new(1052, 300);
        for (int r = 0; r < 6; r++)
        {
            d.Keys.Add(K(Fn[r].Id, KeyKind.Rubber, 824, 40 + r * 40, 118, 30, Fn[r].Label));
            d.Keys.Add(K(Ctl[r].Id, KeyKind.Rubber, 950, 40 + r * 40, 118, 30, Ctl[r].Label));
        }
        Btn[] tr = [Btn.SeekBack, Btn.Prev, Btn.Play, Btn.Next, Btn.SeekFwd];
        for (int i = 0; i < tr.Length; i++)
            d.Keys.Add(K(tr[i], KeyKind.Rubber, 250 + i * 109, 304, 100, 56, tr[i] is Btn.SeekBack or Btn.SeekFwd ? "SEEK" : ""));
        d.Keys.Add(K(Btn.Eject, KeyKind.Rubber, 250, 376, 120, 32, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Rubber, 666, 376, 120, 32, "DETACH"));
        d.Keys.Add(Knob(Btn.Knob, 130, 196, 88, LabelAt.Below));
        d.Keys.Add(Knob(Btn.SpeedKnob, 130, 362, 34, LabelAt.Right));
        d.Beds.Add(new() { X = 812, Y = 30, W = 268, H = 250, Radius = 8 });
        Print(d, "CB READY  ·  4 × 75W  ·  MP3", 824, 300, 244, 30);

        d.Finish = FaceFinish.Brushed; d.FaceMaterial = Mat.Brushed; d.Bolts = true;
        d.Face1 = C(172, 186, 202); d.Face2 = C(122, 136, 152); d.Face3 = C(80, 92, 104);
        d.Print = C(24, 30, 40); d.BrandInk = C(20, 26, 36); d.Trim = C(200, 210, 220);
        d.Bezel1 = C(20, 22, 26); d.Bezel2 = C(4, 4, 6); d.BezelRim = C(170, 180, 190);
        d.KeyMaterial = Mat.Rubber; d.Key1 = C(62, 66, 72); d.Key2 = C(28, 30, 34); d.KeyEdge = C(10, 10, 12); d.KeyText = C(232, 236, 242);
        d.TransportMaterial = Mat.Rubber; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.KnobMaterial = Mat.Rubber; d.Knob1 = C(52, 54, 58); d.Knob2 = C(16, 16, 18); d.Pointer = C(255, 200, 40);
        d.GlowRings = false; d.Led = C(90, 255, 110); d.Palette = 1;
        return d;
    }

    // ───────────────────────────── 15. Bubble ─────────────────────────────

    static RadioDesign Bubble()
    {
        var d = New("BUBBLE Y2K", "bubble", "B-2K  POP", 900, 420, 150);
        Glass(d, 226, 60, 560, 0.8f, 16, 16);
        d.Prints.Clear();
        d.BrandAt = new(450, 18); d.BrandSize = 22; d.BrandAlign = TextAlign.Center; d.BrandFont = "Segoe UI Black"; d.BrandItalic = false;
        d.ModelAt = new(450, 384); d.ModelAlign = TextAlign.Center;
        d.ShowAux = true; d.Aux = new(450, 364);
        d.TheftLed = new(450, 290);
        float[] xs = [150, 128, 116, 116, 128, 150];
        for (int i = 0; i < 6; i++)
        {
            d.Keys.Add(K(Fn[i].Id, KeyKind.Round, xs[i], 58 + i * 52, 44, 44, FnShort[i]));
            d.Keys.Add(K(Ctl[i].Id, KeyKind.Round, 900 - xs[i] - 44, 58 + i * 52, 44, 44, CtlShort[i]));
        }
        d.Keys.Add(K(Btn.Prev, KeyKind.Pill, 300, 300, 100, 36));
        d.Keys.Add(K(Btn.Play, KeyKind.Dome, 410, 292, 80, 52));
        d.Keys.Add(K(Btn.Next, KeyKind.Pill, 500, 300, 100, 36));
        d.Keys.Add(K(Btn.SeekBack, KeyKind.Pill, 214, 348, 90, 28, "SEEK"));
        d.Keys.Add(K(Btn.SeekFwd, KeyKind.Pill, 596, 348, 90, 28, "SEEK"));
        d.Keys.Add(K(Btn.Eject, KeyKind.Pill, 318, 352, 70, 24, "EJECT"));
        d.Keys.Add(K(Btn.Detach, KeyKind.Pill, 512, 352, 70, 24, "POP"));
        d.Keys.Add(Knob(Btn.Knob, 68, 210, 38, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, 832, 210, 38, LabelAt.None));

        d.Finish = FaceFinish.Gloss; d.FaceMaterial = Mat.Gloss;
        d.Face1 = C(255, 186, 220); d.Face2 = C(240, 122, 178); d.Face3 = C(196, 70, 130);
        d.Print = C(120, 20, 70); d.BrandInk = C(255, 250, 252); d.Trim = C(255, 230, 245);
        d.Bezel1 = C(40, 10, 30); d.Bezel2 = C(15, 0, 10); d.BezelRim = C(255, 230, 245);
        d.KeyMaterial = Mat.Backlit; d.Key1 = C(255, 214, 234); d.Key2 = C(236, 152, 198); d.KeyEdge = C(200, 90, 150); d.KeyText = C(120, 20, 70);
        d.TransportMaterial = Mat.Backlit; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.DomeColor = C(255, 60, 160);
        d.KnobMaterial = Mat.Gloss; d.Knob1 = C(255, 255, 255); d.Knob2 = C(230, 190, 210); d.Pointer = C(255, 60, 150);
        d.Glow = C(255, 60, 160); d.Led = C(255, 60, 160); d.Palette = 6;
        return d;
    }

    // ───────────────────────────── modern radios (high-resolution screens) ─────────────────────────────

    /// <summary>A key showing a modern icon instead of a printed label.</summary>
    static KeyDesign T(Btn id, KeyKind kind, float x, float y, float w, float h)
    {
        var k = K(id, kind, x, y, w, h);
        k.Icon = FaceplateRenderer.IconFor(id);
        return k;
    }

    /// <summary>Shared look of the modern units: glossy black glass, touch keys with glowing icons.</summary>
    static void ModernGlass(RadioDesign d, Color glow)
    {
        d.Screen = ScreenKind.Modern;
        d.Prints.Clear();
        d.Leds = [];
        d.Finish = FaceFinish.Gloss; d.FaceMaterial = Mat.Gloss;
        d.Face1 = C(40, 42, 47); d.Face2 = C(16, 17, 20); d.Face3 = C(6, 6, 8);
        d.Print = C(150, 155, 165); d.BrandInk = C(210, 214, 222); d.Trim = C(120, 124, 132);
        d.Bezel1 = C(14, 14, 16); d.Bezel2 = C(2, 2, 3); d.BezelRim = C(60, 62, 68);
        d.KeyMaterial = Mat.Gloss; d.Key1 = C(34, 35, 40); d.Key2 = C(14, 14, 17); d.KeyEdge = C(70, 74, 82); d.KeyText = C(225, 230, 240);
        d.TransportMaterial = Mat.Gloss; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.BacklitLabels = true;
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(92, 95, 104); d.Knob2 = C(22, 23, 27); d.Pointer = C(235, 240, 250);
        d.Glow = glow; d.NightLight = glow; d.Led = glow; d.GlowRings = true;
        d.BrandFont = "Segoe UI Semibold"; d.BrandItalic = false;
    }

    // ───────────────────────────── 16. Prism X9 ─────────────────────────────

    static RadioDesign Prism()
    {
        var d = New("PRISM X9", "PRISM", "X9  ·  9-INCH HD", 1000, 580, 18);
        Glass(d, 85, 34, 466, 1.78f, 10, 12);
        ModernGlass(d, C(90, 180, 255));
        d.BrandAt = new(200, 526); d.BrandSize = 13;
        d.ModelAt = new(800, 530);
        d.TheftLed = new(972, 552);
        Btn[] left = [Btn.Power, Btn.Eject, Btn.Open, Btn.Folder, Btn.List, Btn.Settings, Btn.Vis];
        Btn[] right = [Btn.VolUp, Btn.VolDown, Btn.Mute, Btn.Shuffle, Btn.Repeat, Btn.Stop, Btn.SeekBack, Btn.SeekFwd];
        for (int i = 0; i < left.Length; i++) d.Keys.Add(T(left[i], KeyKind.Touch, 20, 40 + i * 64, 46, 46));
        for (int i = 0; i < right.Length; i++) d.Keys.Add(T(right[i], KeyKind.Touch, 934, 34 + i * 60, 46, 46));
        d.Keys.Add(T(Btn.Prev, KeyKind.Touch, 400, 520, 60, 36));
        d.Keys.Add(T(Btn.Play, KeyKind.Touch, 470, 518, 60, 40));
        d.Keys.Add(T(Btn.Next, KeyKind.Touch, 540, 520, 60, 36));
        d.Keys.Add(Knob(Btn.Knob, 130, 540, 20, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, 870, 540, 20, LabelAt.None));
        d.Palette = 0;
        return d;
    }

    // ───────────────────────────── 17. Nova Tab ─────────────────────────────

    static RadioDesign NovaTab()
    {
        var d = New("NOVA TAB", "NOVA", "TAB 10  ·  CARPLAY READY", 1000, 690, 14);
        Glass(d, 46, 30, 466, 1.95f, 12, 14);
        ModernGlass(d, C(120, 170, 255));
        // Aluminium body under a big glass tablet.
        d.Finish = FaceFinish.Brushed; d.FaceMaterial = Mat.Brushed;
        d.Face1 = C(214, 216, 220); d.Face2 = C(172, 175, 180); d.Face3 = C(128, 131, 136);
        d.Print = C(60, 64, 70); d.BrandInk = C(50, 54, 60);
        d.KeyMaterial = Mat.Brushed; d.Key1 = C(212, 215, 220); d.Key2 = C(160, 163, 168); d.KeyEdge = C(90, 94, 100); d.KeyText = C(40, 42, 46);
        d.TransportMaterial = Mat.Brushed; d.Transport1 = d.Key1; d.Transport2 = d.Key2; d.TransportText = d.KeyText;
        d.BacklitLabels = false;
        d.KnobMaterial = Mat.Chrome; d.Knob1 = C(236, 238, 242); d.Knob2 = C(120, 124, 130); d.Pointer = C(40, 110, 255);
        d.BrandAt = new(30, 659); d.BrandSize = 11;
        d.ModelAt = new(970, 661);
        d.TheftLed = new(972, 590);
        Btn[] strip = [Btn.Open, Btn.Folder, Btn.List, Btn.Settings, Btn.Vis, Btn.VolDown, Btn.VolUp, Btn.Stop, Btn.SeekBack, Btn.SeekFwd, Btn.Eject];
        for (int i = 0; i < strip.Length; i++) d.Keys.Add(T(strip[i], KeyKind.Flat, 60 + i * 80, 562, 70, 26));
        d.Keys.Add(T(Btn.Prev, KeyKind.Flat, 90, 606, 90, 48));
        d.Keys.Add(T(Btn.Play, KeyKind.Flat, 190, 606, 90, 48));
        d.Keys.Add(T(Btn.Next, KeyKind.Flat, 290, 606, 90, 48));
        d.Keys.Add(T(Btn.Mute, KeyKind.Flat, 400, 606, 56, 48));
        d.Keys.Add(T(Btn.Shuffle, KeyKind.Flat, 620, 606, 90, 48));
        d.Keys.Add(T(Btn.Repeat, KeyKind.Flat, 720, 606, 90, 48));
        d.Keys.Add(T(Btn.Power, KeyKind.Flat, 820, 606, 90, 48));
        d.Keys.Add(Knob(Btn.Knob, 510, 632, 34, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, 950, 632, 22, LabelAt.None));
        d.Beds.Add(new() { X = 26, Y = 596, W = 948, H = 62, Radius = 12 });
        d.Palette = 4;
        return d;
    }

    // ───────────────────────────── 18. Aurora Strip ─────────────────────────────

    static RadioDesign AuroraStrip()
    {
        var d = New("AURORA STRIP", "AURORA", "OLED ONE  ·  RGB", 1180, 330, 20);
        Glass(d, 311, 40, 900, 0.62f, 10, 10);
        ModernGlass(d, C(255, 70, 200));
        d.Finish = FaceFinish.Matte; d.FaceMaterial = Mat.Rubber;
        d.Face1 = C(38, 38, 42); d.Face2 = C(22, 22, 25); d.Face3 = C(10, 10, 12);
        d.NeonBezel = true; d.BezelRim = C(255, 70, 200);
        d.BrandAt = new(40, 20); d.BrandSize = 14;
        d.ModelAt = new(1140, 22);
        d.TheftLed = new(1150, 300);
        Btn[] row = [Btn.Open, Btn.Folder, Btn.List, Btn.Settings, Btn.Vis, Btn.Mute, Btn.Prev, Btn.Play, Btn.Next, Btn.Shuffle, Btn.Repeat, Btn.Stop];
        for (int i = 0; i < row.Length; i++) d.Keys.Add(T(row[i], KeyKind.Touch, 305 + i * 48, 228, 42, 34));
        d.Keys.Add(T(Btn.Power, KeyKind.Touch, 40, 262, 56, 30));
        d.Keys.Add(T(Btn.Eject, KeyKind.Touch, 104, 262, 56, 30));
        d.Keys.Add(T(Btn.SeekBack, KeyKind.Touch, 168, 262, 56, 30));
        d.Keys.Add(T(Btn.VolUp, KeyKind.Touch, 900, 44, 60, 30));
        d.Keys.Add(T(Btn.VolDown, KeyKind.Touch, 900, 236, 60, 30));
        d.Keys.Add(T(Btn.SeekFwd, KeyKind.Touch, 990, 262, 56, 30));
        d.Keys.Add(Knob(Btn.SpeedKnob, 150, 140, 50, LabelAt.None));
        d.Keys.Add(Knob(Btn.Knob, 1040, 140, 64, LabelAt.None));
        d.Palette = 6;
        return d;
    }

    // ───────────────────────────── 19. Vector Portrait ─────────────────────────────

    static RadioDesign VectorPortrait()
    {
        var d = New("VECTOR PORTRAIT", "VECTOR", "V12  ·  PORTRAIT HD", 700, 880, 22);
        Glass(d, 110, 40, 200, 2.4f, 12, 14);
        ModernGlass(d, C(110, 210, 255));
        d.Finish = FaceFinish.Brushed; d.FaceMaterial = Mat.Brushed;
        d.Face1 = C(74, 76, 80); d.Face2 = C(52, 54, 58); d.Face3 = C(34, 35, 38);
        d.BrandAt = new(350, 840); d.BrandSize = 13; d.BrandAlign = TextAlign.Center;
        d.ModelAt = new(350, 690); d.ModelAlign = TextAlign.Center;
        d.TheftLed = new(660, 850);
        Btn[] left = [Btn.Power, Btn.Eject, Btn.Open, Btn.Folder, Btn.List, Btn.Settings, Btn.Vis, Btn.Mute];
        Btn[] right = [Btn.VolUp, Btn.VolDown, Btn.Shuffle, Btn.Repeat, Btn.Stop, Btn.SeekBack, Btn.SeekFwd];
        for (int i = 0; i < left.Length; i++) d.Keys.Add(T(left[i], KeyKind.Touch, 28, 50 + i * 74, 56, 50));
        for (int i = 0; i < right.Length; i++) d.Keys.Add(T(right[i], KeyKind.Touch, 616, 50 + i * 74, 56, 50));
        d.Keys.Add(T(Btn.Prev, KeyKind.Touch, 60, 756, 80, 48));
        d.Keys.Add(T(Btn.Play, KeyKind.Touch, 150, 752, 100, 56));
        d.Keys.Add(T(Btn.Next, KeyKind.Touch, 420, 756, 80, 48));
        d.Keys.Add(Knob(Btn.Knob, 350, 780, 56, LabelAt.None));
        d.Keys.Add(Knob(Btn.SpeedKnob, 590, 780, 36, LabelAt.None));
        d.Palette = 1;
        return d;
    }
}
