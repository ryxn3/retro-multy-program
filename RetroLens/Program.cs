namespace RetroLens;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // --render <input image> <output image> "<preset name>": applies a preset without opening the window.
        if (args.Length == 4 && args[0] == "--render")
        {
            var preset = Presets.All().Concat(Presets.Mine()).FirstOrDefault(p => p.Name.Equals(args[3], StringComparison.OrdinalIgnoreCase));
            if (preset == null) return 2;
            if (VideoIO.IsVideo(args[1]))
            {
                VideoIO.ExportAsync(args[1], args[2], preset.Look, new Progress<double>(), CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            using var src = new Bitmap(args[1]);
            using var img = Processor.Apply(src, preset.Look);
            img.Save(args[2]);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        var form = new EditorForm(args.Length >= 1 && !args[0].StartsWith("--") ? args[0] : null);
        // --snapshot out.png [file]: saves a screenshot of the window and exits (used for the launcher's picture).
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            form = new EditorForm(args.Length > 2 ? args[2] : null);
            form.Shown += async (_, _) =>
            {
                await Task.Delay(4500);
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(args[1]);
                form.Close();
            };
        }
        Application.Run(form);
        return 0;
    }
}
