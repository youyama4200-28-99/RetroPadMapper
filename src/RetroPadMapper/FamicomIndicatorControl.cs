using System.Drawing.Drawing2D;

namespace RetroPadMapper;

internal sealed class FamicomIndicatorControl : Control
{
    private const float CanvasWidth = 640;
    private const float CanvasHeight = 280;
    private readonly ControllerService _controller;
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private int _lastMask;
    private Image? _customImage;
    private string _loadedImagePath = "";

    internal FamicomIndicatorControl(ControllerService controller, AppSettings settings)
    {
        _controller = controller; _settings = settings; _lastMask = controller.PressedButtons;
        DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.FromArgb(24, 25, 28);
        _timer.Tick += (_, _) =>
        {
            var mask = _controller.PressedButtons;
            if (mask == _lastMask) return;
            _lastMask = mask; Invalidate();
        };
        _timer.Start();
    }

    internal void BindingsChanged() => Invalidate();
    internal void AppearanceChanged() { DisposeCustomImage(); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Min(ClientSize.Width / CanvasWidth, ClientSize.Height / CanvasHeight);
        g.TranslateTransform((ClientSize.Width - CanvasWidth * scale) / 2, (ClientSize.Height - CanvasHeight * scale) / 2);
        g.ScaleTransform(scale, scale);

        switch (_settings.IndicatorStyle)
        {
            case IndicatorStyle.Nes:
                DrawController(g, Color.FromArgb(190, 192, 190), Color.FromArgb(45, 46, 47), Color.FromArgb(170, 25, 38),
                    Color.FromArgb(255, 70, 86), Color.FromArgb(18, 18, 20), Color.WhiteSmoke, "NES");
                break;
            case IndicatorStyle.Generic:
                DrawController(g, Color.FromArgb(55, 63, 76), Color.FromArgb(31, 36, 44), Color.FromArgb(50, 130, 210),
                    Color.FromArgb(70, 205, 255), Color.FromArgb(14, 17, 22), Color.WhiteSmoke, "GAME CONTROLLER");
                break;
            case IndicatorStyle.CustomImage:
                DrawCustom(g);
                break;
            default:
                DrawController(g, Color.FromArgb(206, 204, 194), Color.FromArgb(49, 50, 52), Color.FromArgb(147, 25, 42),
                    Color.FromArgb(255, 91, 105), Color.FromArgb(21, 21, 23), Color.WhiteSmoke, "FAMILY COMPUTER");
                break;
        }
    }

    private void DrawController(Graphics g, Color shellColor, Color faceColor, Color accentColor, Color activeColor,
        Color darkColor, Color lightColor, string title)
    {
        using var shell = new SolidBrush(shellColor); using var face = new SolidBrush(faceColor);
        using var accent = new SolidBrush(accentColor); using var active = new SolidBrush(activeColor);
        using var dark = new SolidBrush(darkColor); using var light = new SolidBrush(lightColor);
        using var muted = new SolidBrush(Color.FromArgb(180, lightColor)); using var outline = new Pen(Color.FromArgb(100, darkColor), 3);

        var body = new RectangleF(8, 18, 624, 202);
        g.FillRoundedRectangle(shell, body, 20); g.DrawRoundedRectangle(outline, body, 20);
        g.FillRoundedRectangle(face, new RectangleF(28, 43, 584, 132), 10);
        using (var titleFont = new Font("Segoe UI", 7, FontStyle.Bold)) g.DrawString(title, titleFont, accent, 265, 25);

        DrawDpad(g, dark, active);
        DrawRoundButton(g, 493, 108, 29, PadButton.B, "B", accent, active, light);
        DrawRoundButton(g, 558, 88, 29, PadButton.A, "A", accent, active, light);
        DrawPill(g, 272, 120, 55, 20, PadButton.Select, "SELECT", dark, active, muted);
        DrawPill(g, 342, 120, 55, 20, PadButton.Start, "START", dark, active, muted);
        DrawPill(g, 45, 27, 52, 18, PadButton.L, "L", dark, active, light);
        DrawPill(g, 543, 27, 52, 18, PadButton.R, "R", dark, active, light);
        DrawSmall(g, 435, 59, PadButton.X, "X", dark, active, light);
        DrawSmall(g, 470, 59, PadButton.Y, "Y", dark, active, light);
        DrawSmall(g, 320, 59, PadButton.Home, "HOME", dark, active, light);

        DrawMap(g, PadButton.Up, 132, 225, lightColor); DrawMap(g, PadButton.Down, 132, 244, lightColor);
        DrawMap(g, PadButton.Left, 55, 225, lightColor); DrawMap(g, PadButton.Right, 210, 225, lightColor);
        DrawMap(g, PadButton.B, 490, 225, lightColor); DrawMap(g, PadButton.A, 575, 225, lightColor);
        DrawMap(g, PadButton.Select, 292, 244, lightColor); DrawMap(g, PadButton.Start, 375, 244, lightColor);
        DrawDisconnected(g, active);
    }

    private void DrawCustom(Graphics g)
    {
        var image = LoadCustomImage();
        if (image is not null)
        {
            var target = Fit(image.Size, new RectangleF(12, 8, 616, 215));
            g.DrawImage(image, target);
        }
        else
        {
            using var placeholder = new SolidBrush(Color.FromArgb(43, 46, 54));
            g.FillRoundedRectangle(placeholder, new RectangleF(12, 8, 616, 215), 16);
            using var font = new Font("Segoe UI", 13, FontStyle.Bold);
            using var ink = new SolidBrush(Color.WhiteSmoke);
            g.DrawString("設定から任意画像を選択してください", font, ink, 168, 98);
        }

        var buttons = new[] { PadButton.Up, PadButton.Down, PadButton.Left, PadButton.Right, PadButton.A, PadButton.B, PadButton.Start, PadButton.Select };
        for (var i = 0; i < buttons.Length; i++)
        {
            var x = 18 + i * 77;
            using var fill = new SolidBrush(IsPressed(buttons[i]) ? Color.FromArgb(255, 82, 100) : Color.FromArgb(35, 38, 45));
            g.FillRoundedRectangle(fill, new RectangleF(x, 232, 68, 28), 8);
            DrawCentered(g, Display(buttons[i]), x + 34, 246, 9, FontStyle.Bold, Brushes.White);
        }
        if (_controller.ConnectedControllerId.Length == 0)
        {
            using var backdrop = new SolidBrush(Color.FromArgb(190, 20, 22, 27));
            g.FillRoundedRectangle(backdrop, new RectangleF(220, 12, 200, 27), 8);
            using var font = new Font("Segoe UI", 9, FontStyle.Bold);
            g.DrawString("コントローラー未接続", font, Brushes.OrangeRed, 246, 17);
        }
    }

    private Image? LoadCustomImage()
    {
        var path = _settings.IndicatorImagePath;
        if (_loadedImagePath == path) return _customImage;
        DisposeCustomImage(); _loadedImagePath = path;
        try { if (File.Exists(path)) _customImage = Image.FromFile(path); } catch { _customImage = null; }
        return _customImage;
    }

    private static RectangleF Fit(Size source, RectangleF bounds)
    {
        var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        var width = source.Width * scale; var height = source.Height * scale;
        return new RectangleF(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
    }

    private void DrawDisconnected(Graphics g, Brush brush)
    {
        if (_controller.ConnectedControllerId.Length != 0) return;
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        g.DrawString("コントローラー未接続", font, brush, 248, 92);
    }

    private void DrawDpad(Graphics g, Brush normal, Brush active)
    {
        g.FillRectangle(IsPressed(PadButton.Up) ? active : normal, 113, 66, 42, 44);
        g.FillRectangle(IsPressed(PadButton.Down) ? active : normal, 113, 110, 42, 44);
        g.FillRectangle(IsPressed(PadButton.Left) ? active : normal, 69, 104, 44, 42);
        g.FillRectangle(IsPressed(PadButton.Right) ? active : normal, 155, 104, 44, 42);
        g.FillRectangle(normal, 113, 104, 42, 42);
    }

    private void DrawRoundButton(Graphics g, float x, float y, float radius, PadButton button, string text, Brush normal, Brush active, Brush ink)
    {
        g.FillEllipse(IsPressed(button) ? active : normal, x - radius, y - radius, radius * 2, radius * 2);
        DrawCentered(g, text, x, y, 15, FontStyle.Bold, ink);
    }

    private void DrawPill(Graphics g, float x, float y, float width, float height, PadButton button, string text, Brush normal, Brush active, Brush ink)
    {
        g.FillRoundedRectangle(IsPressed(button) ? active : normal, new RectangleF(x, y, width, height), height / 2);
        DrawCentered(g, text, x + width / 2, y + height / 2, 8, FontStyle.Bold, ink);
    }

    private void DrawSmall(Graphics g, float x, float y, PadButton button, string text, Brush normal, Brush active, Brush ink)
    {
        g.FillEllipse(IsPressed(button) ? active : normal, x - 13, y - 13, 26, 26);
        DrawCentered(g, text, x, y, text.Length > 1 ? 6 : 10, FontStyle.Bold, ink);
    }

    private void DrawMap(Graphics g, PadButton button, float x, float y, Color inkColor)
    {
        var label = CompactLabel(_settings.Bindings.GetValueOrDefault(button, OutputBinding.None).Label);
        using var font = new Font("Segoe UI", 6.5f); using var ink = new SolidBrush(inkColor);
        using var format = new StringFormat { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString($"{Display(button)}→{label}", font, ink, new RectangleF(x - 37, y, 74, 17), format);
    }

    private static string CompactLabel(string label)
    {
        label = label.Replace("Xbox: D-pad ", "D", StringComparison.OrdinalIgnoreCase)
                     .Replace("Xbox: ", "", StringComparison.OrdinalIgnoreCase)
                     .Replace("DirectInput: POV ", "POV", StringComparison.OrdinalIgnoreCase)
                     .Replace("DirectInput: Button ", "Btn", StringComparison.OrdinalIgnoreCase)
                     .Replace("キー: ", "", StringComparison.OrdinalIgnoreCase);
        return label.Length > 8 ? label[..7] + "…" : label;
    }

    private bool IsPressed(PadButton button) => (_lastMask & (1 << (int)button)) != 0;
    private static string Display(PadButton b) => b switch { PadButton.Select => "SEL", PadButton.Start => "START", _ => b.ToString().ToUpperInvariant() };

    private static void DrawCentered(Graphics g, string text, float x, float y, float size, FontStyle style, Brush brush)
    {
        using var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, new PointF(x, y), format);
    }

    private void DisposeCustomImage() { _customImage?.Dispose(); _customImage = null; _loadedImagePath = ""; }
    protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); DisposeCustomImage(); } base.Dispose(disposing); }
}

internal static class GraphicsExtensions
{
    internal static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    { using var path = Rounded(bounds, radius); graphics.FillPath(brush, path); }
    internal static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
    { using var path = Rounded(bounds, radius); graphics.DrawPath(pen, path); }
    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2; var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}
