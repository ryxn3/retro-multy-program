using Android.Media;
using RetroRadio.Droid;

namespace RetroRadio;

/// <summary>The phone's sound output: a streaming 32-bit float stereo AudioTrack.</summary>
sealed class AudioTrackSink : IAudioSink
{
    readonly AudioTrack track;
    readonly object gate = new();
    bool disposed;

    public AudioTrackSink(int sampleRate)
    {
        int min = AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Stereo, Encoding.PcmFloat);
        // About a quarter of a second: the radio now and then renders big images, and the music must not skip.
        int size = Math.Max(min * 2, sampleRate * 2 * sizeof(float) / 4);
        track = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!)!
            .SetAudioFormat(new AudioFormat.Builder()
                .SetEncoding(Encoding.PcmFloat)!
                .SetSampleRate(sampleRate)!
                .SetChannelMask(ChannelOut.Stereo)!
                .Build()!)!
            .SetBufferSizeInBytes(size)!
            .SetTransferMode(AudioTrackMode.Stream)!
            .Build()!;
    }

    public int Write(float[] samples, int offset, int count)
    {
        lock (gate)
        {
            if (disposed) return count;
            int n = track.Write(samples, offset, count, WriteMode.NonBlocking);
            return Math.Max(0, n);
        }
    }

    public void Play()
    {
        lock (gate) if (!disposed) track.Play();
    }

    public void Pause()
    {
        lock (gate) if (!disposed) track.Pause();
    }

    public void Flush()
    {
        lock (gate) if (!disposed) track.Flush();
    }

    public long FramesPlayed
    {
        get
        {
            lock (gate) return disposed ? long.MaxValue : track.PlaybackHeadPosition & 0xFFFFFFFFL;
        }
    }

    public float Volume
    {
        set { lock (gate) if (!disposed) track.SetVolume(Math.Clamp(value, 0f, 1f)); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            try { track.Stop(); } catch (Exception) { }
            track.Release();
            track.Dispose();
        }
    }
}
