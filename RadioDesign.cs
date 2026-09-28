using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroRadio;

enum Btn { None, Open, Folder, List, Settings, Vis, Mute, VolUp, VolDown, Shuffle, Repeat, Stop, Power, Prev, Play, Next, SeekBack, SeekFwd, Knob, SpeedKnob, Eject, Detach }
enum KeyKind { OvalTilt, Rubber, Pill, Chrome, Angled, Round, Flat, Touch, ChevronL, ChevronR, Dome, Knob }
enum LabelAt { On, Left, Right, Below, Above, None }
enum FaceFinish { Brushed, Matte, Gloss, Wood, Carbon, Plain }
enum TextAlign { Left, Center, Right }
enum ScreenKind { Vfd, Modern }

[TypeConverter(typeof(ExpandableObjectConverter))]
sealed class Box
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }

    public Box() { }
    public Box(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }

    [JsonIgnore, Browsable(false)] public RectangleF R => new(X, Y, W, H);
    public override string ToString() => $"{X:0}, {Y:0}, {W:0} × {H:0}";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
sealed class Pt
{
    public float X { get; set; }
    public float Y { get; set; }

    public Pt() { }
    public Pt(float x, float y) { X = x; Y = y; }

    [JsonIgnore, Browsable(false)] public PointF P => new(X, Y);
    public override string ToString() => $"{X:0}, {Y:0}";
}

/// <summary>One physical control on the faceplate.</summary>
sealed class KeyDesign
{
    public Btn Id { get; set; }
    public KeyKind Kind { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; } = 60;
    public float H { get; set; } = 26;
    public string Label { get; set; } = "";
    public LabelAt LabelAt { get; set; }
    [Description("Rotation in degrees (tilted oval keys).")]
    public float Tilt { get; set; }
    [Description("Mirror angled keys.")]
    public bool Mirror { get; set; }
    [Description("Optional icon (a character from the Segoe Fluent Icons font) shown instead of the label.")]
    public string Icon { get; set; } = "";

    [JsonIgnore, Browsable(false)] public RectangleF Rect => new(X, Y, W, H);
    [JsonIgnore, Browsable(false)] public PointF Center => new(X + W / 2, Y + H / 2);
    [JsonIgnore, Browsable(false)] public float Radius => W / 2;
    [JsonIgnore, Browsable(false)] public bool Repeats => Id is Btn.VolUp or Btn.VolDown or Btn.SeekBack or Btn.SeekFwd;

    public KeyDesign Clone() => (KeyDesign)MemberwiseClone();
    public override string ToString() => $"{Id} ({Kind})";

    /// <summary>The key's outline in logical pixels, optionally grown by <paramref name="grow"/>.</summary>
    public GraphicsPath Path(float grow = 0)
    {
        var r = RectangleF.Inflate(Rect, grow, grow);
        var p = new GraphicsPath();
        switch (Kind)
        {
            case KeyKind.Knob or KeyKind.Round or KeyKind.Dome:
                p.AddEllipse(r);
                break;
            case KeyKind.Angled:
            {
                float k = Math.Min(9, r.Width / 5);
                PointF[] pts = Mirror
                    ? [new(r.X + k, r.Y), new(r.Right, r.Y), new(r.Right - k, r.Bottom), new(r.X, r.Bottom)]
                    : [new(r.X, r.Y), new(r.Right - k, r.Y), new(r.Right, r.Bottom), new(r.X + k, r.Bottom)];
                p.AddPolygon(pts);
                break;
            }
            case KeyKind.ChevronL or KeyKind.ChevronR:
            {
                float tip = Math.Min(26, r.Width / 4), mid = r.Y + r.Height / 2;
                PointF[] pts = Kind == KeyKind.ChevronL
                    ? [new(r.X, mid), new(r.X + tip, r.Y), new(r.Right, r.Y), new(r.Right, r.Bottom), new(r.X + tip, r.Bottom)]
                    : [new(r.Right, mid), new(r.Right - tip, r.Y), new(r.X, r.Y), new(r.X, r.Bottom), new(r.Right - tip, r.Bottom)];
                p.AddPolygon(pts);
                break;
            }
            default:
            {
                float rad = Kind switch
                {
                    KeyKind.OvalTilt or KeyKind.Pill => r.Height / 2,
                    KeyKind.Chrome => 2.5f,
                    KeyKind.Rubber => Math.Min(7, r.Height / 3),
                    KeyKind.Touch => Math.Min(8, r.Height / 2),
                    _ => Math.Min(7, r.Height / 3),
                };
                AddRoundRect(p, r, rad);
                break;
            }
        }
        if (Tilt != 0)
        {
            using var m = new Matrix();
            m.RotateAt(Tilt, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2));
            p.Transform(m);
        }
        return p;
    }

    public static void AddRoundRect(GraphicsPath p, RectangleF r, float rad)
    {
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.1f)
        {
            p.AddRectangle(r);
            return;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
    }
}

sealed class BedDesign
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; } = 100;
    public float H { get; set; } = 40;
    public float Radius { get; set; } = 8;
    [JsonIgnore, Browsable(false)] public RectangleF Rect => new(X, Y, W, H);
    public override string ToString() => $"Tray {X:0},{Y:0} {W:0}×{H:0}";
}

sealed class PrintDesign
{
    public string Text { get; set; } = "TEXT";
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; } = 200;
    public float H { get; set; } = 14;
    public TextAlign Align { get; set; } = TextAlign.Center;
    public float Size { get; set; } = 7.5f;
    [JsonIgnore, Browsable(false)] public RectangleF Rect => new(X, Y, W, H);
    public override string ToString() => Text;
}

/// <summary>
/// A complete radio: size, layout, colors and materials. Built-in models are made in code;
/// custom ones are .radio.json files made with Radio Designer.
/// </summary>
sealed class RadioDesign
{
    const string C1 = "1 Identity", C2 = "2 Size & shape", C3 = "3 Faceplate", C4 = "4 Display", C5 = "5 Keys", C6 = "6 Knobs", C7 = "7 Extras";

    [Category(C1)] public string Name { get; set; } = "MY RADIO";
    [Category(C1)] public string Brand { get; set; } = "BRAND";
    [Category(C1)] public string Model { get; set; } = "MODEL-1";
    [Category(C1)] public string BrandFont { get; set; } = "Segoe UI";
    [Category(C1)] public float BrandSize { get; set; } = 18;
    [Category(C1)] public bool BrandItalic { get; set; } = true;
    [Category(C1)] public Pt BrandAt { get; set; } = new(40, 22);
    [Category(C1)] public TextAlign BrandAlign { get; set; } = TextAlign.Left;
    [Category(C1)] public Pt ModelAt { get; set; } = new(900, 24);
    [Category(C1)] public TextAlign ModelAlign { get; set; } = TextAlign.Right;

    [Category(C2), Description("Window width in pixels.")] public float Width { get; set; } = 960;
    [Category(C2), Description("Window height in pixels.")] public float Height { get; set; } = 540;
    [Category(C2)] public Box Face { get; set; } = new(14, 14, 932, 512);
    [Category(C2)] public float Corner { get; set; } = 14;

    [Category(C3)] public FaceFinish Finish { get; set; } = FaceFinish.Brushed;
    [Category(C3)] public Mat FaceMaterial { get; set; } = Mat.Brushed;
    [Category(C3)] public Color Face1 { get; set; } = Color.FromArgb(236, 238, 241);
    [Category(C3)] public Color Face2 { get; set; } = Color.FromArgb(166, 171, 178);
    [Category(C3)] public Color Face3 { get; set; } = Color.FromArgb(118, 123, 130);
    [Category(C3), Description("Printed text on the faceplate.")] public Color Print { get; set; } = Color.FromArgb(52, 56, 62);
    [Category(C3)] public Color BrandInk { get; set; } = Color.FromArgb(40, 44, 52);
    [Category(C3)] public Color Trim { get; set; } = Color.Silver;
    [Category(C3), Description("Chrome strip around the edge.")] public bool ChromeTrim { get; set; }
    [Category(C3), Description("Thin painted line in the Trim color.")] public bool Pinstripe { get; set; }
    [Category(C3), Description("Hex bolts in the corners.")] public bool Bolts { get; set; }

    [Category(C4), Description("Vfd = glowing dot-matrix display. Modern = sharp high-resolution color screen with album covers.")]
    public ScreenKind Screen { get; set; } = ScreenKind.Vfd;
    [Category(C4)] public Box Bezel { get; set; } = new(150, 72, 660, 304);
    [Category(C4)] public Mat BezelMaterial { get; set; } = Mat.Gloss;
    [Category(C4)] public Color Bezel1 { get; set; } = Color.FromArgb(44, 46, 50);
    [Category(C4)] public Color Bezel2 { get; set; } = Color.FromArgb(10, 10, 12);
    [Category(C4)] public Color BezelRim { get; set; } = Color.FromArgb(225, 228, 232);
    [Category(C4), Description("Glowing neon line around the display.")] public bool NeonBezel { get; set; }
    [Category(C4)] public float GlassX { get; set; } = 164;
    [Category(C4)] public float GlassY { get; set; } = 90;
    [Category(C4), Description("Width of the display content (200–900). Wider = more room for text.")] public float DisplayWidth { get; set; } = 632;
    [Category(C4), Description("Display zoom (0.4–1.5). The display is always 262 × this tall.")] public float DisplayScale { get; set; } = 1;
    [Category(C4), Description("Default display color: 0 ice, 1 aqua, 2 amber, 3 green, 4 blue, 5 red, 6 vapor.")] public int Palette { get; set; }

    [Category(C5)] public Mat KeyMaterial { get; set; } = Mat.Plastic;
    [Category(C5)] public Color Key1 { get; set; } = Color.FromArgb(104, 110, 118);
    [Category(C5)] public Color Key2 { get; set; } = Color.FromArgb(28, 30, 34);
    [Category(C5)] public Color KeyEdge { get; set; } = Color.FromArgb(18, 18, 20);
    [Category(C5)] public Color KeyText { get; set; } = Color.FromArgb(52, 56, 62);
    [Category(C5), Description("Material of the play / skip / seek keys.")] public Mat TransportMaterial { get; set; } = Mat.Brushed;
    [Category(C5)] public Color Transport1 { get; set; } = Color.FromArgb(236, 238, 241);
    [Category(C5)] public Color Transport2 { get; set; } = Color.FromArgb(150, 155, 162);
    [Category(C5)] public Color TransportText { get; set; } = Color.FromArgb(55, 60, 66);
    [Category(C5), Description("Color of dome (round glossy) keys.")] public Color DomeColor { get; set; } = Color.FromArgb(40, 120, 255);
    [Category(C5), Description("Key labels glow like backlit rubber.")] public bool BacklitLabels { get; set; }
    [Category(C5), Description("Color of the key illumination in NIGHT lighting.")] public Color NightLight { get; set; } = Color.FromArgb(255, 160, 70);
    [Category(C5), Description("Keys get glowing edges in KeyEdge color.")] public bool NeonKeys { get; set; }

    [Category(C6)] public Mat KnobMaterial { get; set; } = Mat.Plastic;
    [Category(C6)] public Color Knob1 { get; set; } = Color.FromArgb(80, 84, 92);
    [Category(C6)] public Color Knob2 { get; set; } = Color.FromArgb(16, 17, 20);
    [Category(C6)] public Color Pointer { get; set; } = Color.FromArgb(140, 200, 255);
    [Category(C6), Description("Illuminated rings around knobs and glowing play keys.")] public bool GlowRings { get; set; } = true;
    [Category(C6)] public Color Glow { get; set; } = Color.FromArgb(40, 120, 255);

    [Category(C7)] public Color Led { get; set; } = Color.FromArgb(90, 255, 110);
    [Category(C7)] public List<Pt> Leds { get; set; } = [];
    [Category(C7)] public Pt TheftLed { get; set; } = new(150, 398);
    [Category(C7)] public bool ShowSlot { get; set; }
    [Category(C7), Description("Disc / cassette slot opening.")] public Box Slot { get; set; } = new(330, 42, 300, 6);
    [Category(C7)] public bool ShowIr { get; set; }
    [Category(C7)] public Box Ir { get; set; } = new(742, 37, 26, 16);
    [Category(C7)] public bool ShowUsb { get; set; }
    [Category(C7)] public Box Usb { get; set; } = new(222, 489, 40, 18);
    [Category(C7)] public bool ShowAux { get; set; }
    [Category(C7)] public Pt Aux { get; set; } = new(196, 498);
    [Category(C7), Description("A groove across the face (flip-down faceplate).")] public bool ShowSeam { get; set; }
    [Category(C7)] public Box Seam { get; set; } = new(30, 37, 900, 2);
    [Category(C7)] public List<BedDesign> Beds { get; set; } = [];
    [Category(C7)] public List<PrintDesign> Prints { get; set; } = [];

    [Browsable(false)] public List<KeyDesign> Keys { get; set; } = [];

    [JsonIgnore, Browsable(false)] public RectangleF Glass => new(GlassX, GlassY, DisplayWidth * DisplayScale, 262 * DisplayScale);
    [JsonIgnore, Browsable(false)] public string? SourcePath { get; set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new ColorJson() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static RadioDesign FromJson(string json) => JsonSerializer.Deserialize<RadioDesign>(json, Json) ?? throw new InvalidDataException("Empty radio design.");
    public static RadioDesign Load(string path) => Normalize(FromJson(File.ReadAllText(path)), path);
    public void Save(string path) => File.WriteAllText(path, ToJson());
    public RadioDesign Clone() => FromJson(ToJson());

    /// <summary>Clamps values so a hand-edited file can't break the renderer.</summary>
    static RadioDesign Normalize(RadioDesign d, string path)
    {
        d.SourcePath = path;
        d.Width = Math.Clamp(d.Width, 300, 2400);
        d.Height = Math.Clamp(d.Height, 200, 1600);
        d.DisplayWidth = Math.Clamp(d.DisplayWidth, 200, 900);
        d.DisplayScale = Math.Clamp(d.DisplayScale, 0.3f, 2f);
        d.Palette = Math.Clamp(d.Palette, 0, 6);
        d.Keys = d.Keys.Where(k => k.W > 2 && k.H > 2).ToList();
        return d;
    }

    public static string CustomFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "radios");

    /// <summary>Copies a design file into the radio's custom folder and returns the installed path.</summary>
    public static string Install(string file)
    {
        var d = Load(file); // validates
        Directory.CreateDirectory(CustomFolder);
        string safe = string.Concat(d.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        string dest = Path.Combine(CustomFolder, (safe.Length == 0 ? "radio" : safe) + ".radio.json");
        if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            File.Copy(file, dest, overwrite: true);
        return dest;
    }

    public static List<RadioDesign> LoadCustom()
    {
        var list = new List<RadioDesign>();
        if (!Directory.Exists(CustomFolder)) return list;
        foreach (var f in Directory.EnumerateFiles(CustomFolder, "*.radio.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try { list.Add(Load(f)); }
            catch (Exception) { /* skip broken files */ }
        }
        return list;
    }

    sealed class ColorJson : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        {
            var s = (reader.GetString() ?? "#000000").TrimStart('#');
            uint v = Convert.ToUInt32(s, 16);
            return s.Length <= 6 ? Color.FromArgb(255, Color.FromArgb((int)v)) : Color.FromArgb((int)v);
        }

        public override void Write(Utf8JsonWriter writer, Color c, JsonSerializerOptions o) =>
            writer.WriteStringValue(c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
    }
}
