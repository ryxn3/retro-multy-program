namespace JdmGarage;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // --snapshot out.png [car index] [day|night] [scale]
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            int car = args.Length > 2 && int.TryParse(args[2], out var c) ? c : 0;
            bool night = args.Length > 3 && args[3] == "night";
            float scale = args.Length > 4 && float.TryParse(args[4], System.Globalization.CultureInfo.InvariantCulture, out var k) ? k : 1f;
            GarageForm.Snapshot(args[1], car, night, scale);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new GarageForm());
        return 0;
    }
}
