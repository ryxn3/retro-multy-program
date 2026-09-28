namespace RadioVideoConverter;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Command-line mode: RadioVideoConverter.exe input.mp4 output.rdv [--fit fill|fit|stretch] [--fps N] [--mode fs|ordered|threshold|gray4] [--color on|off]
        if (args.Length >= 2 && !args[0].StartsWith("--"))
            return Converter.RunCommandLine(args);

        ApplicationConfiguration.Initialize();

        // --snapshot out.png [video] [color]: saves a screenshot of the window and exits (used for the launcher's pictures).
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            var form = new ConverterForm(args.Length > 2 ? args[2] : null, args.Length > 3 && args[3] == "color");
            form.Shown += async (_, _) =>
            {
                await Task.Delay(4000);
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(args[1]);
                form.Close();
            };
            Application.Run(form);
            return 0;
        }

        Application.Run(new ConverterForm(args.Length == 1 ? args[0] : null));
        return 0;
    }
}
