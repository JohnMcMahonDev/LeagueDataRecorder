using System.Drawing;
using System.Windows.Forms;

namespace League_Data_Recorder;

internal sealed class LoadingOverlay : Panel
{
    private readonly Label caption;
    private LoadingOverlay()
    {
        Dock = DockStyle.Fill; Visible = false; BackColor = AppTheme.Surface;
        AccessibleRole = AccessibleRole.StatusBar; AccessibleName = "Loading";
        var center = new TableLayoutPanel { Anchor = AnchorStyles.None, Size = new Size(360, 76), ColumnCount = 1, RowCount = 2 };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
        caption = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = AppTheme.Text, AutoEllipsis = true };
        var progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 32 };
        center.Controls.Add(caption, 0, 0); center.Controls.Add(progress, 0, 1); Controls.Add(center);
        Resize += (_, _) => { center.Width = Math.Max(1, Math.Min(360, Width - 24)); center.Left = Math.Max(0, (Width - center.Width) / 2); center.Top = Math.Max(0, (Height - center.Height) / 2); };
    }
    internal static LoadingOverlay Attach(Control parent)
    {
        var overlay = new LoadingOverlay(); parent.Controls.Add(overlay); return overlay;
    }
    internal void SetBusy(bool busy, string text = "Loading…")
    {
        caption.Text = text; AccessibleName = text;
        Visible = busy;
        if (busy) BringToFront();
    }
}
