using NAudio.Wave;

namespace RetroRadio;

enum ReverbMode { Off, Room, Hall, Garage, Echo }

/// <summary>
/// The radio's sound processing on 44.1 kHz stereo: a 3-band tone control with loudness, the
/// "outside the car" muffle, and reverb/echo. Settings can change at any time from the UI thread;
/// filter coefficients are updated on the audio thread the next time it reads.
/// </summary>
sealed class SoundProcessor(ISampleProvider source) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;
    int Rate => source.WaveFormat.SampleRate;

    // Tone controls in dB (-12..+12), loudness, muffle, reverb.
    volatile float bass, mid, treble;
    volatile bool loudness, muffle;
    volatile ReverbMode reverb;
    volatile float volumeForLoudness = 1;
    volatile bool dirty = true;

    public float Bass { get => bass; set { bass = value; dirty = true; } }
    public float Mid { get => mid; set { mid = value; dirty = true; } }
    public float Treble { get => treble; set { treble = value; dirty = true; } }
    public bool Loudness { get => loudness; set { loudness = value; dirty = true; } }
    public bool Muffle { get => muffle; set { muffle = value; dirty = true; } }
    public ReverbMode Reverb { get => reverb; set { reverb = value; dirty = true; } }

    /// <summary>The volume knob: loudness boosts bass and treble more the quieter it is, like real head units.</summary>
    public float KnobVolume
    {
        get => volumeForLoudness;
        set
        {
            if (Math.Abs(value - volumeForLoudness) < 0.02f) return;
            volumeForLoudness = value;
            if (loudness) dirty = true;
        }
    }

    readonly Biquad[][] bands = [[new(), new(), new()], [new(), new(), new()]];      // bass, mid, treble
    readonly Biquad[][] muffleFilters = [[new(), new(), new()], [new(), new(), new()]];
    bool tone, muffled;
    float headroom = 1, headroomNow = 1;
    Freeverb? verb;
    Echo? echo;
    ReverbMode builtReverb = (ReverbMode)(-1);

    void Rebuild()
    {
        dirty = false;
        float lb = 0, lt = 0;
        if (loudness)
        {
            float quiet = 1 - Math.Clamp(volumeForLoudness, 0, 1);
            lb = 3 + 7 * quiet;
            lt = 1.5f + 3.5f * quiet;
        }
        float b = bass + lb, t = treble + lt, m = mid;
        for (int ch = 0; ch < 2; ch++)
        {
            bands[ch][0].LowShelf(Rate, 110, b);
            bands[ch][1].Peaking(Rate, 1000, 0.8f, m);
            bands[ch][2].HighShelf(Rate, 7000, t);
            // Heard from outside a closed car: the body and glass swallow the highs, the bass comes through.
            muffleFilters[ch][0].LowPass(Rate, 380, 0.7f);
            muffleFilters[ch][1].LowPass(Rate, 520, 0.7f);
            muffleFilters[ch][2].LowShelf(Rate, 90, 5);
        }
        tone = Math.Abs(b) > 0.05f || Math.Abs(m) > 0.05f || Math.Abs(t) > 0.05f;
        muffled = muffle;
        // Headroom like a real head unit: boosting a band turns the whole signal down by about as much,
        // so a +12 dB bass boost makes the bass fuller instead of overdriving everything into distortion.
        float boost = Math.Max(0, Math.Max(b, Math.Max(m, t))) + (muffle ? 5 : 0);
        headroom = (float)Math.Pow(10, -boost * 0.9f / 20);

        if (reverb != builtReverb)
        {
            builtReverb = reverb;
            verb = reverb switch
            {
                ReverbMode.Room => new Freeverb(Rate, room: 0.55f, damp: 0.55f, wet: 0.18f, predelayMs: 8),
                ReverbMode.Hall => new Freeverb(Rate, room: 0.86f, damp: 0.4f, wet: 0.3f, predelayMs: 25),
                ReverbMode.Garage => new Freeverb(Rate, room: 0.93f, damp: 0.12f, wet: 0.36f, predelayMs: 45),
                _ => null,
            };
            echo = reverb == ReverbMode.Echo ? new Echo(Rate, 340, 0.38f, 0.32f) : null;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int n = source.Read(buffer, offset, count);
        if (dirty) Rebuild();
        if (tone || muffled || headroomNow != headroom)
        {
            // The headroom gain glides to its new value so changing the EQ never clicks.
            float h = headroomNow, hStep = (headroom - h) / Math.Max(1, n / 2);
            var bl = bands[0];
            var br = bands[1];
            var ml = muffleFilters[0];
            var mr = muffleFilters[1];
            for (int i = offset; i + 1 < offset + n; i += 2)
            {
                h += hStep;
                float l = buffer[i] * h, r = buffer[i + 1] * h;
                if (tone)
                    for (int k = 0; k < 3; k++)
                    {
                        l = bl[k].Process(l);
                        r = br[k].Process(r);
                    }
                if (muffled)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        l = ml[k].Process(l);
                        r = mr[k].Process(r);
                    }
                    // Mostly mono, as sound through a car body is.
                    float mono = (l + r) * 0.5f;
                    l = mono * 0.85f + l * 0.15f;
                    r = mono * 0.85f + r * 0.15f;
                }
                buffer[i] = l;
                buffer[i + 1] = r;
            }
            headroomNow = headroom;
        }
        verb?.Process(buffer, offset, n);
        echo?.Process(buffer, offset, n);
        return n;
    }
}

/// <summary>
/// An RBJ-cookbook biquad whose coefficients can change while it runs without losing its state
/// (NAudio's filters have to be rebuilt, which clicks). A 0 dB shelf or peak passes the sound untouched.
/// </summary>
sealed class Biquad
{
    double b0 = 1, b1, b2, a1, a2;
    double x1, x2, y1, y2;

    public float Process(float x)
    {
        double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
        x2 = x1; x1 = x;
        y2 = y1; y1 = Math.Abs(y) < 1e-20 ? 0 : y; // no denormals
        return (float)y;
    }

    void Set(double nb0, double nb1, double nb2, double na0, double na1, double na2)
    {
        b0 = nb0 / na0; b1 = nb1 / na0; b2 = nb2 / na0; a1 = na1 / na0; a2 = na2 / na0;
    }

    public void LowShelf(int rate, double f, double db)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / rate, cs = Math.Cos(w);
        double sq = 2 * Math.Sqrt(A) * Math.Sin(w) / 2 * Math.Sqrt(2); // shelf slope 1
        Set(A * ((A + 1) - (A - 1) * cs + sq), 2 * A * ((A - 1) - (A + 1) * cs), A * ((A + 1) - (A - 1) * cs - sq),
            (A + 1) + (A - 1) * cs + sq, -2 * ((A - 1) + (A + 1) * cs), (A + 1) + (A - 1) * cs - sq);
    }

    public void HighShelf(int rate, double f, double db)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / rate, cs = Math.Cos(w);
        double sq = 2 * Math.Sqrt(A) * Math.Sin(w) / 2 * Math.Sqrt(2);
        Set(A * ((A + 1) + (A - 1) * cs + sq), -2 * A * ((A - 1) + (A + 1) * cs), A * ((A + 1) + (A - 1) * cs - sq),
            (A + 1) - (A - 1) * cs + sq, 2 * ((A - 1) - (A + 1) * cs), (A + 1) - (A - 1) * cs - sq);
    }

    public void Peaking(int rate, double f, double q, double db)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / rate, alpha = Math.Sin(w) / (2 * q), cs = Math.Cos(w);
        Set(1 + alpha * A, -2 * cs, 1 - alpha * A, 1 + alpha / A, -2 * cs, 1 - alpha / A);
    }

    public void LowPass(int rate, double f, double q)
    {
        double w = 2 * Math.PI * f / rate, alpha = Math.Sin(w) / (2 * q), cs = Math.Cos(w);
        Set((1 - cs) / 2, 1 - cs, (1 - cs) / 2, 1 + alpha, -2 * cs, 1 - alpha);
    }
}

/// <summary>Freeverb (Jezar's public-domain reverb): 8 comb and 4 all-pass filters per channel.</summary>
sealed class Freeverb
{
    static readonly int[] CombTuning = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    static readonly int[] AllPassTuning = [556, 441, 341, 225];
    const int Spread = 23;

    readonly Comb[][] combs = new Comb[2][];
    readonly AllPass[][] allpasses = new AllPass[2][];
    readonly float wet, dry;
    readonly float[][] pre;
    int prePos;

    public Freeverb(int rate, float room, float damp, float wet, float predelayMs)
    {
        float scale = rate / 44100f;
        for (int ch = 0; ch < 2; ch++)
        {
            int sp = ch == 0 ? 0 : Spread;
            combs[ch] = CombTuning.Select(t => new Comb((int)((t + sp) * scale), 0.7f + 0.28f * room, damp * 0.4f)).ToArray();
            allpasses[ch] = AllPassTuning.Select(t => new AllPass((int)((t + sp) * scale))).ToArray();
        }
        this.wet = wet;
        dry = 1 - wet * 0.5f;
        pre = [new float[Math.Max(1, (int)(rate * predelayMs / 1000))], new float[Math.Max(1, (int)(rate * predelayMs / 1000))]];
    }

    public void Process(float[] buf, int offset, int count)
    {
        for (int i = offset; i + 1 < offset + count; i += 2)
        {
            float inL = buf[i], inR = buf[i + 1];
            float input = (pre[0][prePos] + pre[1][prePos]) * 0.015f;
            pre[0][prePos] = inL;
            pre[1][prePos] = inR;
            prePos = (prePos + 1) % pre[0].Length;
            float outL = 0, outR = 0;
            foreach (var c in combs[0]) outL += c.Process(input);
            foreach (var c in combs[1]) outR += c.Process(input);
            foreach (var a in allpasses[0]) outL = a.Process(outL);
            foreach (var a in allpasses[1]) outR = a.Process(outR);
            buf[i] = inL * dry + outL * wet * 3f; // Freeverb's wet scale
            buf[i + 1] = inR * dry + outR * wet * 3f;
        }
    }

    sealed class Comb(int size, float feedback, float damp)
    {
        readonly float[] buf = new float[Math.Max(1, size)];
        int pos;
        float store;

        public float Process(float input)
        {
            float o = buf[pos];
            store = o * (1 - damp) + store * damp;
            buf[pos] = input + store * feedback;
            if (++pos >= buf.Length) pos = 0;
            return o;
        }
    }

    sealed class AllPass(int size)
    {
        readonly float[] buf = new float[Math.Max(1, size)];
        int pos;

        public float Process(float input)
        {
            float b = buf[pos];
            float o = -input + b;
            buf[pos] = input + b * 0.5f;
            if (++pos >= buf.Length) pos = 0;
            return o;
        }
    }
}

/// <summary>A tape-style echo: repeats that get darker as they fade.</summary>
sealed class Echo(int rate, float delayMs, float feedback, float wet)
{
    readonly float[] l = new float[(int)(rate * delayMs / 1000)], r = new float[(int)(rate * delayMs / 1000)];
    int pos;
    float lpL, lpR;

    public void Process(float[] buf, int offset, int count)
    {
        for (int i = offset; i + 1 < offset + count; i += 2)
        {
            float dl = l[pos], dr = r[pos];
            lpL += (dl - lpL) * 0.45f;
            lpR += (dr - lpR) * 0.45f;
            l[pos] = buf[i] + lpR * feedback; // ping-pong
            r[pos] = buf[i + 1] + lpL * feedback;
            buf[i] += lpL * wet;
            buf[i + 1] += lpR * wet;
            if (++pos >= l.Length) pos = 0;
        }
    }
}

/// <summary>
/// Estimates the tempo from the beat of the music: an onset envelope of the low end,
/// autocorrelated over the last few seconds for 70–180 BPM.
/// </summary>
sealed class BpmDetector
{
    const int Hop = 512;
    readonly float[] onsets = new float[700]; // ~8 s at 44.1 kHz / 512
    int count, pos;
    float energy, lastEnergy, avg, lp;
    int inHop;
    public float Bpm { get; private set; }
    readonly object gate = new();

    public void Reset()
    {
        lock (gate)
        {
            count = pos = inHop = 0;
            energy = lastEnergy = avg = lp = 0;
            Bpm = 0;
            Array.Clear(onsets);
        }
    }

    /// <summary>Stereo 44.1 kHz samples from the audio thread.</summary>
    public void Feed(float[] buf, int offset, int n)
    {
        lock (gate)
        {
            for (int i = offset; i + 1 < offset + n; i += 2)
            {
                float m = (buf[i] + buf[i + 1]) * 0.5f;
                lp += (m - lp) * 0.02f; // low end, where the kick drum is
                energy += lp * lp;
                if (++inHop < Hop) continue;
                inHop = 0;
                float onset = Math.Max(0, energy - lastEnergy);
                lastEnergy = energy;
                energy = 0;
                avg = avg * 0.99f + onset * 0.01f;
                onsets[pos] = onset;
                pos = (pos + 1) % onsets.Length;
                if (count < onsets.Length) count++;
            }
        }
    }

    /// <summary>Recomputes the estimate (call now and then from the UI thread).</summary>
    public float Estimate()
    {
        float[] o;
        int n, p;
        lock (gate)
        {
            if (count < 300) return Bpm = 0;
            o = (float[])onsets.Clone();
            n = count;
            p = pos;
        }
        double hopsPerSec = 44100.0 / Hop;
        int minLag = (int)(hopsPerSec * 60 / 180), maxLag = (int)(hopsPerSec * 60 / 70);
        double best = 0;
        int bestLag = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double s = 0;
            for (int k = 0; k + lag < n; k++)
            {
                int a = (p - n + k + o.Length * 2) % o.Length, b = (a + lag) % o.Length;
                s += o[a] * o[b];
            }
            s /= n - lag;
            // A gentle preference for the 90–140 range, where most music sits.
            double bpmAt = hopsPerSec * 60 / lag;
            s *= 1 + 0.15 * Math.Exp(-Math.Pow((bpmAt - 118) / 30, 2));
            if (s > best) { best = s; bestLag = lag; }
        }
        if (bestLag == 0 || best <= 0) return Bpm;
        float est = (float)(hopsPerSec * 60 / bestLag);
        Bpm = Bpm <= 0 ? est : Math.Abs(est - Bpm) > 6 ? est : Bpm * 0.7f + est * 0.3f;
        return Bpm;
    }
}
