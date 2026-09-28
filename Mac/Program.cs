using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace RetroRadio;

sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        Name = "Retro Radio";
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Finder passes a "-psn_…" argument to apps opened from it; it isn't a file.
        args = args.Where(a => !a.StartsWith("-psn_", StringComparison.Ordinal)).ToArray();
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .SetupWithoutStarting();
        System.Windows.Forms.Form.PrimaryScaling = 1;
        System.Windows.Forms.Application.Run(new RadioForm(args));
    }
}
