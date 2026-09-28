using System.Reflection;

namespace RetroLauncher;

static class Program
{
    public static readonly Version Version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);

    [STAThread]
    static void Main(string[] args)
    {
        // --install <radio|converter|designer>: installs or updates a program without opening the window.
        if (args.Length == 2 && args[0] == "--install")
        {
            Environment.Exit(InstallFromCommandLine(args[1]).GetAwaiter().GetResult());
            return;
        }

        ApplicationConfiguration.Initialize();
        var form = new LauncherForm();
        // --snapshot out.png: saves a screenshot after checking GitHub, then exits (used for testing).
        if (args.Length == 2 && args[0] == "--snapshot")
        {
            form.Shown += async (_, _) =>
            {
                await Task.Delay(5000);
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(args[1]);
                form.Close();
            };
        }
        Application.Run(form);
    }

    static async Task<int> InstallFromCommandLine(string id)
    {
        var app = LauncherForm.Apps.FirstOrDefault(a => a.Id == id);
        if (app == null) return 2;
        try
        {
            var release = await GitHub.LatestAsync(CancellationToken.None);
            if (release == null) return 3;
            await Installer.InstallAsync(app, release, new Progress<(long, long)>(), CancellationToken.None);
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
