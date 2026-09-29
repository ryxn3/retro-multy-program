// What the Android app gives the shared radio code: its UI thread, the screen, the file pickers,
// sound output and the system's audio decoders. MainActivity fills this in at start.
namespace RetroRadio.Droid
{
    /// <summary>A file (or folder) picker request; the answer comes later, on the UI thread.</summary>
    public sealed record PickRequest(string Title, string[] Extensions, bool Multiple, bool Folder, Action<string[]> Done);

    /// <summary>A sound output stream: interleaved stereo float samples in, the speaker out.</summary>
    public interface IAudioSink : IDisposable
    {
        /// <summary>Writes as many samples as fit right now (never blocks). Returns how many were taken.</summary>
        int Write(float[] samples, int offset, int count);
        void Play();
        void Pause();
        /// <summary>Drops whatever is still waiting to be played.</summary>
        void Flush();
        /// <summary>Frames the speaker has actually played since the stream started (or was flushed).</summary>
        long FramesPlayed { get; }
        float Volume { set; }
    }

    public interface IAndroidHost
    {
        bool IsUiThread { get; }
        void Post(Action action);
        void PostDelayed(Action action, int milliseconds);

        /// <summary>The main form wants to be drawn again.</summary>
        void RequestPaint();

        /// <summary>The main form changed its size (so the view fits it to the screen again).</summary>
        void FormResized();

        void OpenUrl(string url);
        void Pick(PickRequest request);

        /// <summary>Asks for a few lines of text (like app keys). <paramref name="done"/> gets the answers if the user taps OK.</summary>
        void AskText(string title, string message, string[] fields, string[] values, Action<string[]> done);
        void Minimize();
        void Quit();

        IAudioSink OpenAudio(int sampleRate);

        /// <summary>Opens any file or http(s) stream Android can decode (FLAC, M4A/AAC, OGG, Opus…), decoding as it's read.</summary>
        NAudio.Wave.WaveStream OpenDecoder(string pathOrUrl);
    }

    public static class Host
    {
        public static IAndroidHost Current { get; set; } = null!;
    }
}
