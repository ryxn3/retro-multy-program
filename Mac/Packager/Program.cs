// Builds "Retro Radio.app" from a macOS publish folder and zips it with Unix permissions,
// so the app is executable after unzipping on a Mac (zips made on Windows normally lose that).
//
//   dotnet run --project Mac/Packager -- <publish dir> <icns> <out.zip> <version> [extra files...]
using System.IO.Compression;
using System.Text;

if (args.Length < 4)
{
    Console.Error.WriteLine("usage: Packager <publish dir> <icns> <out.zip> <version> [extra files...]");
    return 2;
}
string pub = args[0], icns = args[1], outZip = args[2], version = args[3];
const string App = "Retro Radio.app/Contents/";

string plist = $"""
    <?xml version="1.0" encoding="UTF-8"?>
    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
    <plist version="1.0">
    <dict>
        <key>CFBundleName</key><string>Retro Radio</string>
        <key>CFBundleDisplayName</key><string>Retro Radio</string>
        <key>CFBundleIdentifier</key><string>io.github.ryxn3.retroradio</string>
        <key>CFBundleVersion</key><string>{version}</string>
        <key>CFBundleShortVersionString</key><string>{version}</string>
        <key>CFBundleExecutable</key><string>RetroRadio</string>
        <key>CFBundleIconFile</key><string>RetroRadio</string>
        <key>CFBundlePackageType</key><string>APPL</string>
        <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
        <key>LSMinimumSystemVersion</key><string>11.0</string>
        <key>LSApplicationCategoryType</key><string>public.app-category.music</string>
        <key>NSHighResolutionCapable</key><true/>
        <key>NSHumanReadableCopyright</key><string>Retro Multy Program</string>
    </dict>
    </plist>
    """;

string howTo = """
    RETRO RADIO FOR MAC (Apple Silicon)

    1. Drag "Retro Radio" into your Applications folder.

    2. The first time only: Retro Radio isn't signed with a paid Apple developer account,
       so macOS blocks it when it comes from the internet. Open Terminal and paste:

           xattr -dr com.apple.quarantine "/Applications/Retro Radio.app"

       Then open Retro Radio normally. (If macOS says it "can't be checked", you can also
       go to System Settings > Privacy & Security and click "Open Anyway".)

    3. Optional: for OGG/Opus/WMA files and some SoundCloud tracks, install ffmpeg
       with Homebrew:  brew install ffmpeg

    Music files: MP3, WAV, AIFF, M4A/AAC, ALAC and FLAC play out of the box.
    Drag files or folders onto the radio, or use OPEN / FOLDER.
    """;

if (File.Exists(outZip)) File.Delete(outZip);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outZip))!);
var executables = new List<string>();
using (var zip = ZipFile.Open(outZip, ZipArchiveMode.Create))
{
    void Dir(string name) => zip.CreateEntry(name).ExternalAttributes = Mode(0x41ED); // drwxr-xr-x
    void Text(string name, string content) => Add(name, Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n")), false);
    void Add(string name, byte[] data, bool exec)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        e.ExternalAttributes = Mode(exec ? 0x81ED : 0x81A4); // -rwxr-xr-x / -rw-r--r--
        using var s = e.Open();
        s.Write(data);
        if (exec) executables.Add(name);
    }

    Dir("Retro Radio.app/");
    Dir(App);
    Dir(App + "MacOS/");
    Dir(App + "Resources/");
    Text(App + "Info.plist", plist);
    Text(App + "PkgInfo", "APPL????");
    Add(App + "Resources/RetroRadio.icns", File.ReadAllBytes(icns), false);
    foreach (var f in Directory.EnumerateFiles(pub, "*", SearchOption.AllDirectories).OrderBy(f => f))
    {
        string rel = Path.GetRelativePath(pub, f).Replace('\\', '/');
        bool exec = rel == "RetroRadio" || rel.EndsWith(".dylib", StringComparison.Ordinal);
        Add(App + "MacOS/" + rel, File.ReadAllBytes(f), exec);
    }
    Text("HOW TO OPEN.txt", howTo);
    foreach (var extra in args.Skip(4).Where(File.Exists))
        Add(Path.GetFileName(extra), File.ReadAllBytes(extra), false);
}

MarkUnix(outZip);
Console.WriteLine($"{outZip}: {new FileInfo(outZip).Length / 1048576.0:0.0} MB, {executables.Count} executables");
return 0;

static int Mode(int unixMode) => unixMode << 16;

// Unzip tools only honour Unix permissions when an entry says it was made on Unix
// ("version made by" host byte = 3), which .NET doesn't set on Windows. Patch the central directory.
static void MarkUnix(string path)
{
    var data = File.ReadAllBytes(path);
    int eocd = -1;
    for (int i = data.Length - 22; i >= 0; i--)
        if (BitConverter.ToUInt32(data, i) == 0x06054b50) { eocd = i; break; }
    if (eocd < 0) throw new InvalidDataException("No end of central directory.");
    int count = BitConverter.ToUInt16(data, eocd + 10);
    int pos = (int)BitConverter.ToUInt32(data, eocd + 16);
    for (int n = 0; n < count; n++)
    {
        if (BitConverter.ToUInt32(data, pos) != 0x02014b50) throw new InvalidDataException("Bad central directory.");
        data[pos + 4] = 20; // spec version 2.0
        data[pos + 5] = 3;  // made on Unix
        int nameLen = BitConverter.ToUInt16(data, pos + 28), extraLen = BitConverter.ToUInt16(data, pos + 30), commentLen = BitConverter.ToUInt16(data, pos + 32);
        pos += 46 + nameLen + extraLen + commentLen;
    }
    File.WriteAllBytes(path, data);
}
