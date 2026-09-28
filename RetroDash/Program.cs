namespace RetroDash;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // --snapshot out.png [digital|analog|modern] [day|night] [seconds of demo driving]
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            var style = args.Length > 2 && Enum.TryParse<ClusterStyle>(args[2], true, out var st) ? st : ClusterStyle.Analog;
            bool night = args.Length > 3 && args[3] == "night";
            double secs = args.Length > 4 && double.TryParse(args[4], System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 14;
            DashForm.Snapshot(args[1], style, night, secs, 1.5f);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new DashForm());
        return 0;
    }
}
