namespace RetroPadMapper;

internal static class UiSnapshot
{
    internal static int Run(string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            ApplicationConfiguration.Initialize();
            var settings = new AppSettings { DebugMode = true };
            using var controller = new ControllerService();
            using var form = new MainForm(settings, new SettingsStore(), controller)
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
            form.Show();
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(outputDirectory, "settings-window.png"));
            }
            var tabs = Find<TabControl>(form);
            if (tabs is not null)
            {
                tabs.SelectedIndex = 1;
                Application.DoEvents();
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(outputDirectory, "display-settings.png"));
            }
            var customImagePath = Path.Combine(outputDirectory, "custom-image-sample.png");
            using (var sample = new Bitmap(480, 160))
            using (var graphics = Graphics.FromImage(sample))
            {
                graphics.Clear(Color.FromArgb(30, 65, 95));
                using var font = new Font("Segoe UI", 24, FontStyle.Bold);
                graphics.DrawString("CUSTOM IMAGE", font, Brushes.White, 115, 55);
                sample.Save(customImagePath);
            }
            settings.IndicatorImagePath = customImagePath;
            using var indicator = new FamicomIndicatorControl(controller, settings) { Size = new Size(680, 300) };
            indicator.CreateControl();
            foreach (var style in Enum.GetValues<IndicatorStyle>())
            {
                settings.IndicatorStyle = style;
                indicator.AppearanceChanged();
                using var bitmap = new Bitmap(indicator.Width, indicator.Height);
                indicator.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(outputDirectory, $"indicator-{style.ToString().ToLowerInvariant()}.png"));
            }
            settings.IndicatorStyle = IndicatorStyle.Famicom;
            using (var indicatorWindow = new IndicatorForm(controller, settings)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000)
            })
            {
                indicatorWindow.Show();
                Application.DoEvents();
                using var windowBitmap = new Bitmap(indicatorWindow.Width, indicatorWindow.Height);
                indicatorWindow.DrawToBitmap(windowBitmap, new Rectangle(Point.Empty, windowBitmap.Size));
                windowBitmap.Save(Path.Combine(outputDirectory, "indicator-window.png"));
                indicatorWindow.Hide();
            }
            form.Hide();
            return 0;
        }
        catch { return 1; }
    }

    private static T? Find<T>(Control root) where T : Control
    {
        if (root is T match) return match;
        foreach (Control child in root.Controls)
        {
            var nested = Find<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
