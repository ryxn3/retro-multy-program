using System.IO.Compression;
using System.Text;

namespace RetroRadio;

/// <summary>
/// "Retro Display Video" (.rdv): a low-resolution dot-matrix animation for the radio display.
///
/// Layout: "RDV1", u16 width, u16 height, u8 bits per dot (1 or 2), u8 reserved,
/// u16 fps x 100, u32 frame count, then a GZip stream of frames. Each frame is width x height
/// dots, row by row, packed MSB-first at the given bit depth.
/// </summary>
sealed class RdvVideo
{
    public const int DefaultWidth = 192;
    public const int DefaultHeight = 48;
    static readonly byte[] Magic = "RDV1"u8.ToArray();

    public int Width { get; }
    public int Height { get; }
    public int Bits { get; }
    public float Fps { get; }
    public List<byte[]> Frames { get; }

    public int MaxLevel => (1 << Bits) - 1;
    public int FrameBytes => FrameSize(Width, Height, Bits);
    public TimeSpan Duration => TimeSpan.FromSeconds(Frames.Count / Fps);

    RdvVideo(int w, int h, int bits, float fps, List<byte[]> frames)
    {
        Width = w; Height = h; Bits = bits; Fps = fps; Frames = frames;
    }

    public static int FrameSize(int w, int h, int bits) => (w * h * bits + 7) / 8;

    /// <summary>Brightness level (0..MaxLevel) of one dot.</summary>
    public int Level(byte[] frame, int x, int y)
    {
        int bit = (y * Width + x) * Bits;
        int shift = 8 - Bits - (bit & 7);
        return (frame[bit >> 3] >> shift) & MaxLevel;
    }

    public static RdvVideo Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        if (!br.ReadBytes(4).AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a radio video (.rdv) file.");
        int w = br.ReadUInt16(), h = br.ReadUInt16(), bits = br.ReadByte();
        br.ReadByte();
        float fps = br.ReadUInt16() / 100f;
        int count = (int)br.ReadUInt32();
        if (w == 0 || h == 0 || bits is not (1 or 2) || fps <= 0) throw new InvalidDataException("Corrupt .rdv header.");

        int size = FrameSize(w, h, bits);
        var frames = new List<byte[]>(count);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        for (int i = 0; i < count; i++)
        {
            var f = new byte[size];
            gz.ReadExactly(f);
            frames.Add(f);
        }
        return new RdvVideo(w, h, bits, fps, frames);
    }

    /// <summary>
    /// Plays an .rdv straight from the file: frames are unpacked as they're shown, so a long video costs
    /// almost no memory (loading one whole could take hundreds of MB). Going back (the loop) reopens the file.
    /// </summary>
    public sealed class Player : IDisposable
    {
        readonly string path;
        FileStream? fs;
        GZipStream? gz;
        int next;          // index of the frame the stream will produce next
        readonly byte[] frame;

        public int Width { get; }
        public int Height { get; }
        public int Bits { get; }
        public float Fps { get; }
        public int Count { get; }
        public int MaxLevel => (1 << Bits) - 1;

        public Player(string path)
        {
            this.path = path;
            using (var f = File.OpenRead(path))
            {
                var h = ReadHeader(f);
                (Width, Height, Bits, Fps, Count) = h;
            }
            frame = new byte[FrameSize(Width, Height, Bits)];
        }

        /// <summary>The frame at <paramref name="index"/> (the returned buffer is reused).</summary>
        public byte[] Frame(int index)
        {
            if (index == next - 1 && gz != null) return frame;
            if (gz == null || index < next) Reopen();
            try
            {
                while (next <= index)
                {
                    gz!.ReadExactly(frame);
                    next++;
                }
            }
            catch (EndOfStreamException)
            {
                Reopen();
            }
            return frame;
        }

        void Reopen()
        {
            gz?.Dispose();
            fs?.Dispose();
            fs = File.OpenRead(path);
            ReadHeader(fs);
            gz = new GZipStream(fs, CompressionMode.Decompress);
            next = 0;
        }

        /// <summary>Brightness level (0..MaxLevel) of one dot.</summary>
        public int Level(byte[] f, int x, int y)
        {
            int bit = (y * Width + x) * Bits;
            int shift = 8 - Bits - (bit & 7);
            return (f[bit >> 3] >> shift) & MaxLevel;
        }

        public void Dispose()
        {
            gz?.Dispose();
            fs?.Dispose();
            gz = null;
            fs = null;
        }
    }

    static (int W, int H, int Bits, float Fps, int Count) ReadHeader(Stream s)
    {
        var br = new BinaryReader(s);
        if (!br.ReadBytes(4).AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a radio video (.rdv) file.");
        int w = br.ReadUInt16(), h = br.ReadUInt16(), bits = br.ReadByte();
        br.ReadByte();
        float fps = br.ReadUInt16() / 100f;
        int count = (int)br.ReadUInt32();
        if (w == 0 || h == 0 || bits is not (1 or 2) || fps <= 0) throw new InvalidDataException("Corrupt .rdv header.");
        return (w, h, bits, fps, count);
    }

    /// <summary>Streams frames into a new .rdv file; the frame count is patched in on Dispose.</summary>
    public sealed class Writer : IDisposable
    {
        readonly FileStream fs;
        readonly GZipStream gz;
        readonly int w, h, bits;
        int count;

        public Writer(string path, int width, int height, int bits, float fps)
        {
            w = width; h = height; this.bits = bits;
            fs = File.Create(path);
            var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);
            bw.Write(Magic);
            bw.Write((ushort)width);
            bw.Write((ushort)height);
            bw.Write((byte)bits);
            bw.Write((byte)0);
            bw.Write((ushort)Math.Round(fps * 100));
            bw.Write(0u); // frame count, filled in later
            bw.Flush();
            gz = new GZipStream(fs, CompressionLevel.Optimal, leaveOpen: true);
        }

        public int Count => count;

        /// <summary>Adds a frame given one level (0..2^bits-1) per dot, row by row.</summary>
        public void Add(byte[] levels)
        {
            var packed = new byte[FrameSize(w, h, bits)];
            for (int i = 0; i < w * h; i++)
            {
                int bit = i * bits;
                packed[bit >> 3] |= (byte)(levels[i] << (8 - bits - (bit & 7)));
            }
            gz.Write(packed);
            count++;
        }

        public void Dispose()
        {
            gz.Dispose();
            fs.Position = 12;
            fs.Write(BitConverter.GetBytes((uint)count));
            fs.Dispose();
        }
    }
}
