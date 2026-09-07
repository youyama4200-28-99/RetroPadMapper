using System.Drawing.Drawing2D;

namespace RetroPadMapper;

internal sealed class FamicomIndicatorControl : Control
{
    private readonly ControllerService _controller;
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private int _lastMask;

    internal FamicomIndicatorControl(ControllerService controller, AppSettings settings)
    {
        _controller = controller;
        _settings = settings;
        _lastMask = controller.PressedButtons;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(24, 25, 28);
        _timer.Tick += (_, _) =>
        {
            var mask = _controller.PressedButtons;
            if (mask == _lastMask) return;
            _lastMask = mask;
            Invalidate();
        };
        _timer.Start();
    }

    internal void BindingsChanged() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Min(ClientSize.Width / 640f, ClientSize.Height / 250f);
        g.TranslateTransform((ClientSize.Width - 640 * scale) / 2, (ClientSize.Height - 250 * scale) / 2);
        g.ScaleTransform(scale, scale);

        using var shell = new SolidBrush(Color.FromArgb(206, 204, 194));
        using var face = new SolidBrush(Color.FromArgb(49, 50, 52));
        using var red = new SolidBrush(Color.FromArgb(147, 25, 42));
        using var active = new SolidBrush(Color.FromArgb(255, 91, 105));
        using var black = new SolidBrush(Color.FromArgb(21, 21, 23));
        using var white = new SolidBrush(Color.WhiteSmoke);
        using var gray = new SolidBrush(Color.FromArgb(175, 177, 180));
        using var outline = new Pen(Color.FromArgb(92, 90, 85), 3);

        var body = new RectangleF(8, 24, 624, 200);
        g.FillRoundedRectangle(shell, body, 20);
        g.DrawRoundedRectangle(outline, body, 20);
        g.FillRoundedRectangle(face, new RectangleF(28, 47, 584, 130), 10);

        DrawDpad(g, black, active);
        DrawRoundButton(g, 493, 111, 29, PadButton.B, "B", red, active, white);
        DrawRoundButton(g, 558, 91, 29, PadButton.A, "A", red, active, white);
        DrawPill(g, 272, 123, 55, 20, PadButton.Select, "SELECT", black, active, gray);
        DrawPill(g, 342, 123, 55, 20, PadButton.Start, "START", black, active, gray);
        DrawPill(g, 45, 31, 52, 18, PadButton.L, "L", black, active, white);
        DrawPill(g, 543, 31, 52, 18, PadButton.R, "R", black, active, white);
        DrawSmall(g, 435, 62, PadButton.X, "X", black, active, white);
        DrawSmall(g, 470, 62, PadButton.Y, "Y", black, active, white);
        DrawSmall(g, 320, 62, PadButton.Home, "HOME", black, active, white);

        DrawMap(g, PadButton.Up, 132, 180); DrawMap(g, PadButton.Down, 132, 198);
        DrawMap(g, PadButton.Left, 55, 180); DrawMap(g, PadButton.Right, 210, 180);
        DrawMap(g, PadButton.B, 490, 180); DrawMap(g, PadButton.A, 575, 180);
        DrawMap(g, PadButton.Select, 292, 180); DrawMap(g, PadButton.Start, 375, 180);

        if (_controller.ConnectedControllerId.Length == 0)
        {
            using var font = new Font("Segoe UI", 10, FontStyle.Bold);
            g.DrawString("コントローラー未接続", font, active, 248, 94);
        }
    }

    private void DrawDpad(Graphics g, Brush normal, Brush active)
    {
        g.FillRectangle(IsPressed(PadButton.Up) ? active : normal, 113, 69, 42, 44);
        g.FillRectangle(IsPressed(PadButton.Down) ? active : normal, 113, 113, 42, 44);
        g.FillRectangle(IsPressed(PadButton.Left) ? active : normal, 69, 107, 44, 42);
        g.FillRectangle(IsPressed(PadButton.Right) ? active : normal, 155, 107, 44, 42);
        g.FillRectangle(normal, 113, 107, 42, 42);
        using var center = new SolidBrush(Color.FromArgb(55, 55, 58));
        g.FillEllipse(center, 124, 118, 20, 20);
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

    private void DrawMap(Graphics g, PadButton button, float x, float y)
    {
        var label = _settings.Bindings.GetValueOrDefault(button, OutputBinding.None).Label;
        if (label.StartsWith("キー: ")) label = label[4..];
        if (label.Length > 10) label = label[..9] + "…";
        using var font = new Font("Segoe UI", 7f);
        using var ink = new SolidBrush(Color.FromArgb(67, 65, 61));
        using var format = new StringFormat { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString($"{Display(button)}→{label}", font, ink, new RectangleF(x - 48, y, 96, 15), format);
    }

    private bool IsPressed(PadButton button) => (_lastMask & (1 << (int)button)) != 0;
    private static string Display(PadButton b) => b switch { PadButton.Select => "SEL", PadButton.Start => "START", _ => b.ToString().ToUpperInvariant() };

    private static void DrawCentered(Graphics g, string text, float x, float y, float size, FontStyle style, Brush brush)
    {
        using var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, new PointF(x, y), format);
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}

internal static class GraphicsExtensions
{
    internal static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = Rounded(bounds, radius);
        graphics.FillPath(brush, path);
    }
    internal static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
    {
        using var path = Rounded(bounds, radius);
        graphics.DrawPath(pen, path);
    }
    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
