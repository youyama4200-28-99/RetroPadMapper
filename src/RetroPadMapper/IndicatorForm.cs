namespace RetroPadMapper;

internal sealed class IndicatorForm : Form
{
    internal IndicatorForm(ControllerService controller, AppSettings settings)
    {
        Text = "RetroPad Mapper — 入力インジケータ";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 330);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimumSize = SizeFromClientSize(new Size(560, 275));
        ShowInTaskbar = false;
        TopMost = settings.IndicatorTopMost;
        Controls.Add(new FamicomIndicatorControl(controller, settings) { Dock = DockStyle.Fill });
        if (settings.IndicatorX >= 0 && settings.IndicatorY >= 0)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(settings.IndicatorX, settings.IndicatorY);
        }
        else StartPosition = FormStartPosition.CenterScreen;
    }

    internal FamicomIndicatorControl Indicator => (FamicomIndicatorControl)Controls[0];
}
