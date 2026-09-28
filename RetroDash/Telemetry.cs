using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RetroDash;

/// <summary>Everything the cluster shows, already in car terms.</summary>
sealed class DashState
{
    public float Rpm;          // 0..8000
    public float Speed;        // km/h
    public float Fuel;         // 0..1
    public float Temp;         // °C, 40..130
    public float Volts;        // 11.5..14.8
    public string Gear = "P";
    public double OdoKm, TripKm;
    public bool CheckEngine, Battery, LowFuel, Hot, HighBeam, Left, Right, Charging;
    public float CpuPercent, RamPercent, BatteryPercent = -1;
    public double DownBps, UpBps;
    public TimeSpan Uptime;
    public string NowPlaying = "";
}

/// <summary>Reads the PC (CPU, memory, network, battery) and turns it into a drive; or simulates one.</summary>
sealed class Telemetry
{
    public bool Demo;
    public readonly DashState State = new();

    long lastIdle, lastKernel, lastUser;
    long lastRx, lastTx;
    DateTime lastSample = DateTime.MinValue, lastNowPlaying = DateTime.MinValue, lastSave = DateTime.UtcNow;
    double cpuAvg;
    float hotSeconds;
    double totalKm, realTrip;
    double demoOdo = 48213.4, demoTrip;

    // Demo drive.
    double demoTime, demoSpeed, demoRpm = 900;
    int demoGear;

    static string StateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroDash", "odometer.json");
    static string NowPlayingFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "nowplaying.txt");

    public Telemetry()
    {
        try
        {
            if (File.Exists(StateFile)) totalKm = JsonSerializer.Deserialize<double>(File.ReadAllText(StateFile));
        }
        catch (Exception) { }
        State.OdoKm = totalKm;
        ReadCpu(out lastIdle, out lastKernel, out lastUser);
        (lastRx, lastTx) = NetworkTotals();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(totalKm));
        }
        catch (Exception) { }
    }

    public void ResetTrip()
    {
        realTrip = demoTrip = 0;
        State.TripKm = 0;
    }

    /// <summary>Call every frame; real readings are taken about four times a second.</summary>
    public void Update(double dt)
    {
        var s = State;
        if (Demo)
        {
            DemoDrive(dt);
        }
        else if ((DateTime.UtcNow - lastSample).TotalSeconds >= 0.25)
        {
            double secs = lastSample == DateTime.MinValue ? 0.25 : (DateTime.UtcNow - lastSample).TotalSeconds;
            lastSample = DateTime.UtcNow;
            Sample(secs);
        }

        if ((DateTime.UtcNow - lastNowPlaying).TotalSeconds > 2)
        {
            lastNowPlaying = DateTime.UtcNow;
            try { s.NowPlaying = File.Exists(NowPlayingFile) && (DateTime.Now - File.GetLastWriteTime(NowPlayingFile)).TotalHours < 12 ? File.ReadAllText(NowPlayingFile).Trim() : ""; }
            catch (IOException) { }
        }
        if ((DateTime.UtcNow - lastSave).TotalSeconds > 30)
        {
            lastSave = DateTime.UtcNow;
            Save();
        }
        s.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    void Sample(double secs)
    {
        var s = State;

        // CPU → engine speed.
        ReadCpu(out long idle, out long kernel, out long user);
        long dIdle = idle - lastIdle, dTotal = (kernel - lastKernel) + (user - lastUser);
        (lastIdle, lastKernel, lastUser) = (idle, kernel, user);
        float cpu = dTotal > 0 ? Math.Clamp(1f - dIdle / (float)dTotal, 0, 1) : 0;
        s.CpuPercent = cpu * 100;
        s.Rpm = 800 + cpu * 7200;

        // A slow average warms the engine up under sustained load.
        cpuAvg += (cpu - cpuAvg) * Math.Min(1, secs / 40);
        s.Temp = 70 + (float)cpuAvg * 55;
        hotSeconds = cpu > 0.92f ? hotSeconds + (float)secs : 0;
        s.CheckEngine = hotSeconds > 5;
        s.Hot = s.Temp > 115;

        // Memory → fuel.
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            s.RamPercent = mem.dwMemoryLoad;
            s.Fuel = 1 - mem.dwMemoryLoad / 100f;
        }
        s.LowFuel = s.Fuel < 0.15f;

        // Network → road speed (log scale: 20 KB/s ≈ 60, 2 MB/s ≈ 180, 100 MB/s ≈ 280 km/h).
        var (rx, tx) = NetworkTotals();
        double down = Math.Max(0, rx - lastRx) / secs, up = Math.Max(0, tx - lastTx) / secs;
        (lastRx, lastTx) = (rx, tx);
        s.DownBps = down;
        s.UpBps = up;
        s.Speed = (float)Math.Min(300, 60 * Math.Log10(1 + Math.Max(0, down + up - 1500) / 2000)); // ignore background chatter
        double km = (down + up) * secs / 1_000_000; // 1 km per MB
        totalKm += km;
        realTrip += km;
        s.OdoKm = totalKm;
        s.TripKm = realTrip;
        s.HighBeam = down + up > 5_000_000;
        s.Left = down > 50_000;
        s.Right = up > 50_000;
        s.Gear = s.Speed < 1 ? (s.Rpm < 1500 ? "P" : "N") : GearFor(s.Speed).ToString();

        // Battery → charging system.
        var ps = SystemInformation.PowerStatus;
        s.Charging = ps.PowerLineStatus == PowerLineStatus.Online;
        s.BatteryPercent = ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery) ? -1 : ps.BatteryLifePercent * 100;
        s.Volts = s.Charging ? 14.2f : s.BatteryPercent < 0 ? 13.8f : 11.6f + ps.BatteryLifePercent * 1.2f;
        s.Battery = !s.Charging && s.BatteryPercent is >= 0 and < 20;

    }

    static int GearFor(float speed) => speed switch { < 20 => 1, < 45 => 2, < 75 => 3, < 110 => 4, < 150 => 5, _ => 6 };

    /// <summary>A simple car: throttle patterns, six gears, shifts at high revs, and engine braking.</summary>
    void DemoDrive(double dt)
    {
        var s = State;
        demoTime += dt;
        double cycle = demoTime % 60;
        double throttle = cycle switch
        {
            < 3 => 0,                     // idle at the lights
            < 16 => 0.85,                 // pull away hard
            < 26 => 0.35,                 // cruise
            < 32 => 1.0,                  // overtake
            < 42 => 0.3,
            < 50 => 0,                    // coast
            _ => -1,                      // brake to a stop
        };
        double[] ratios = [3.6, 2.1, 1.4, 1.0, 0.8, 0.65];
        if (demoGear == 0 && throttle > 0) demoGear = 1;
        double ratio = demoGear > 0 ? ratios[demoGear - 1] : 0;

        double accel = throttle > 0 ? throttle * 9 / Math.Max(0.6, ratio * 0.5 + demoGear * 0.35) : throttle < 0 ? -9 : -1.2;
        accel -= demoSpeed * demoSpeed * 0.00006; // drag
        demoSpeed = Math.Clamp(demoSpeed + accel * dt * 3.6, 0, 250);

        double targetRpm = demoGear == 0 || demoSpeed < 2 ? 850 + throttle * 2000 : Math.Max(900, demoSpeed * ratio * 36);
        demoRpm += (targetRpm - demoRpm) * Math.Min(1, dt * 6);
        if (demoGear > 0 && demoGear < 6 && demoRpm > 6200 && throttle > 0.5) demoGear++;
        else if (demoGear < 6 && demoRpm > 4500 && throttle > 0 && throttle <= 0.5) demoGear++;
        else if (demoGear > 1 && demoRpm < 1500) demoGear--;
        if (demoSpeed < 1 && throttle <= 0) demoGear = 0;

        s.Speed = (float)demoSpeed;
        s.Rpm = (float)Math.Clamp(demoRpm, 700, 8000);
        s.Gear = demoGear == 0 ? "N" : demoGear.ToString();
        double km = demoSpeed * dt / 3600;
        demoOdo += km; // the demo car has its own odometer
        demoTrip += km;
        s.OdoKm = demoOdo;
        s.TripKm = demoTrip;
        s.DownBps = s.UpBps = 0;
        s.Temp = (float)Math.Min(95, 40 + demoTime * 2);
        s.Fuel = (float)Math.Max(0.05, 0.8 - demoTime / 3000);
        s.Volts = 14.1f;
        s.Charging = true;
        s.Left = cycle is > 1 and < 3;
        s.Right = cycle is > 26 and < 28;
        s.HighBeam = cycle is > 28 and < 32;
        s.CheckEngine = s.Battery = s.Hot = false;
        s.LowFuel = s.Fuel < 0.15f;
    }

    // ───────────────────────────── Windows ─────────────────────────────

    static (long Rx, long Tx) NetworkTotals()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var st = ni.GetIPStatistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
            }
        }
        catch (NetworkInformationException) { }
        return (rx, tx);
    }

    static void ReadCpu(out long idle, out long kernel, out long user)
    {
        GetSystemTimes(out var i, out var k, out var u);
        idle = i; kernel = k; user = u; // kernel time includes idle time
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
