namespace RetroRadio;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // --export-builtins <folder>: writes every built-in radio as an editable .radio.json template.
        if (args.Length == 2 && args[0] == "--export-builtins")
        {
            Directory.CreateDirectory(args[1]);
            foreach (var d in BuiltInRadios.All())
                d.Save(Path.Combine(args[1], string.Concat(d.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-')) + ".radio.json"));
            return;
        }
        // --snapshot out.png [design]: saves a screenshot of the window and exits (used for testing).
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            var form = new DesignerForm(args.Length > 2 ? args[2] : null);
            form.Shown += async (_, _) =>
            {
                await Task.Delay(1500);
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(args[1]);
                form.Close();
            };
            Application.Run(form);
            return;
        }
        Application.Run(new DesignerForm(args.Length > 0 ? args[0] : null));
    }
}
