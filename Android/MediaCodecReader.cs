using System.Runtime.InteropServices;
using Android.Media;
using NAudio.Wave;

namespace RetroRadio;

/// <summary>
/// Reads any audio Android can decode (FLAC, M4A/AAC, ALAC, OGG, Opus, AMR…) from a file or an http(s) address,
/// as 16-bit PCM, decoding a little at a time as the radio plays it. Seeking jumps the extractor and skips ahead.
/// </summary>
sealed class MediaCodecReader : WaveStream
{
    const long TimeoutUs = 10_000;

    readonly MediaExtractor extractor = new();
    readonly MediaCodec codec;
    readonly MediaCodec.BufferInfo info = new();
    readonly long durationUs;
    WaveFormat format;
    bool floatOut;

    byte[] pending = new byte[65536];
    int pendingPos, pendingLen;
    long position;           // bytes handed out so far (in the output format)
    long skipUntilUs = -1;   // after a seek: drop decoded audio before this time
    bool inputDone, outputDone;

    public MediaCodecReader(string source)
    {
        try
        {
            if (source.Contains("://")) extractor.SetDataSource(source, new Dictionary<string, string> { ["User-Agent"] = "RetroRadio/1.0" });
            else extractor.SetDataSource(source);

            int track = -1;
            MediaFormat? f = null;
            for (int i = 0; i < extractor.TrackCount; i++)
            {
                var tf = extractor.GetTrackFormat(i);
                if (tf.GetString(MediaFormat.KeyMime)?.StartsWith("audio/", StringComparison.Ordinal) == true)
                {
                    track = i;
                    f = tf;
                    break;
                }
            }
            if (track < 0 || f == null) throw new InvalidDataException("No audio in this file.");
            extractor.SelectTrack(track);
            durationUs = f.ContainsKey(MediaFormat.KeyDuration) ? f.GetLong(MediaFormat.KeyDuration) : 0;
            format = new WaveFormat(f.GetInteger(MediaFormat.KeySampleRate), 16, f.GetInteger(MediaFormat.KeyChannelCount));

            codec = MediaCodec.CreateDecoderByType(f.GetString(MediaFormat.KeyMime)!);
            codec.Configure(f, null, null, MediaCodecConfigFlags.None);
            codec.Start();
        }
        catch
        {
            extractor.Release();
            throw;
        }

        // Decode the first bit now: the decoder says what it really puts out (HE-AAC doubles the sample rate).
        while (pendingLen == 0 && !outputDone && DecodeStep(primed: false)) { }
    }

    public override WaveFormat WaveFormat => format;

    public override long Length => durationUs <= 0 ? 0 : durationUs * format.AverageBytesPerSecond / 1_000_000 / format.BlockAlign * format.BlockAlign;

    public override long Position
    {
        get => position;
        set => Seek(value);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            if (pendingLen == 0)
            {
                if (outputDone) break;
                DecodeStep(primed: true);
                continue;
            }
            int n = Math.Min(count - total, pendingLen);
            Buffer.BlockCopy(pending, pendingPos, buffer, offset + total, n);
            pendingPos += n;
            pendingLen -= n;
            total += n;
        }
        position += total;
        return total;
    }

    void Seek(long bytes)
    {
        bytes = Math.Max(0, bytes / format.BlockAlign * format.BlockAlign);
        long us = bytes * 1_000_000 / format.AverageBytesPerSecond;
        extractor.SeekTo(us, MediaExtractorSeekTo.PreviousSync);
        codec.Flush();
        inputDone = outputDone = false;
        pendingPos = pendingLen = 0;
        skipUntilUs = us;
        position = bytes;
    }

    /// <summary>Feeds the decoder one packet and takes one block of sound out. Returns false at the very end.</summary>
    bool DecodeStep(bool primed)
    {
        if (!inputDone)
        {
            int inIdx = codec.DequeueInputBuffer(TimeoutUs);
            if (inIdx >= 0)
            {
                var inBuf = codec.GetInputBuffer(inIdx)!;
                int size = extractor.ReadSampleData(inBuf, 0);
                if (size < 0)
                {
                    codec.QueueInputBuffer(inIdx, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                    inputDone = true;
                }
                else
                {
                    codec.QueueInputBuffer(inIdx, 0, size, extractor.SampleTime, MediaCodecBufferFlags.None);
                    extractor.Advance();
                }
            }
        }

        int outIdx = codec.DequeueOutputBuffer(info, TimeoutUs);
        if (outIdx == (int)MediaCodecInfoState.OutputFormatChanged)
        {
            var of = codec.OutputFormat;
            floatOut = of.ContainsKey(MediaFormat.KeyPcmEncoding) && of.GetInteger(MediaFormat.KeyPcmEncoding) == (int)Encoding.PcmFloat;
            // Once playing, the format can't change under the radio; before that it takes the decoder's word.
            if (!primed) format = new WaveFormat(of.GetInteger(MediaFormat.KeySampleRate), 16, of.GetInteger(MediaFormat.KeyChannelCount));
            return true;
        }
        if (outIdx < 0) return !outputDone;

        if (info.Size > 0) TakeOutput(outIdx);
        codec.ReleaseOutputBuffer(outIdx, false);
        if ((info.Flags & MediaCodecBufferFlags.EndOfStream) != 0) outputDone = true;
        return !outputDone;
    }

    void TakeOutput(int outIdx)
    {
        var outBuf = codec.GetOutputBuffer(outIdx)!;
        IntPtr src = outBuf.GetDirectBufferAddress() + info.Offset;
        int size = info.Size;
        int bytesPerSample = floatOut ? 4 : 2;
        int frameBytes = bytesPerSample * format.Channels;
        int skip = 0;
        if (skipUntilUs >= 0)
        {
            // After a seek the decoder starts at the key frame before the target: drop audio up to it.
            long late = skipUntilUs - info.PresentationTimeUs;
            if (late > 0) skip = (int)Math.Min(size, late * format.SampleRate / 1_000_000 * frameBytes);
            skip -= skip % frameBytes;
            if (skip < size) skipUntilUs = -1;
        }
        int keep = size - skip;
        if (keep <= 0) return;
        int outLen = floatOut ? keep / 2 : keep;
        if (pending.Length < outLen) pending = new byte[outLen];
        if (floatOut)
        {
            // Some decoders put out float; the radio reads 16-bit.
            var f = new float[keep / 4];
            Marshal.Copy(src + skip, f, 0, f.Length);
            for (int i = 0; i < f.Length; i++)
            {
                short s = (short)Math.Clamp((int)Math.Round(f[i] * 32767f), short.MinValue, short.MaxValue);
                pending[i * 2] = (byte)s;
                pending[i * 2 + 1] = (byte)(s >> 8);
            }
        }
        else
        {
            Marshal.Copy(src + skip, pending, 0, keep);
        }
        pendingPos = 0;
        pendingLen = outLen;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { codec.Stop(); } catch (Exception) { }
            codec.Release();
            extractor.Release();
        }
        base.Dispose(disposing);
    }
}
