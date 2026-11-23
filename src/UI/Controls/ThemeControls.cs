using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace League_Data_Recorder;

// These controls keep their standard WinForms input, accessibility and designer
// behavior. Only their presentation changes; no extra UI package is needed.
public sealed class DarkButton : Button
{
    private bool hovered;
    private bool pressed;
    private bool navigationSelected;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool NavigationSelected
    {
        get => navigationSelected;
        set { navigationSelected = value; Invalidate(); }
    }

    public DarkButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        BackColor = AppTheme.Raised;
        ForeColor = AppTheme.Text;
        Cursor = Cursors.Hand;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.BorderColor = AppTheme.Border;
        FlatAppearance.MouseOverBackColor = AppTheme.Hover;
        FlatAppearance.MouseDownBackColor = AppTheme.Selection;
    }

    protected override CreateParams CreateParams
    {
        get
        {
#pragma warning disable WFO5001 // .NET 9's opt-in theme API; painting is supplied here.
            SetStyle(ControlStyles.ApplyThemingImplicitly, false);
#pragma warning restore WFO5001
            return base.CreateParams;
        }
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { pressed = true; Invalidate(); }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.Clear(Parent?.BackColor ?? AppTheme.Background);
        bool primary = BackColor.ToArgb() == AppTheme.Primary.ToArgb();
        Color fill = !Enabled ? AppTheme.Inset : pressed ? AppTheme.Selection :
            hovered ? primary ? AppTheme.PrimaryHover : AppTheme.Hover : BackColor;
        Color border = Focused && ShowFocusCues ? AppTheme.Accent :
            navigationSelected ? AppTheme.Primary : primary && Enabled ? fill : AppTheme.Border;
        var bounds = new RectangleF(.5F, .5F, Width - 1F, Height - 1F);
        using var path = ThemeDrawing.RoundedRectangle(bounds, ThemeDrawing.Px(this, 5));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
        if (FlatAppearance.BorderSize > 0 || navigationSelected || Focused && ShowFocusCues)
        {
            using var pen = new Pen(border);
            e.Graphics.DrawPath(pen, path);
        }
        if (navigationSelected)
        {
            int inset = ThemeDrawing.Px(this, 14);
            using var accent = new SolidBrush(AppTheme.Accent);
            e.Graphics.FillRectangle(accent, inset, Height - ThemeDrawing.Px(this, 3),
                Math.Max(1, Width - inset * 2), ThemeDrawing.Px(this, 2));
        }
        var textBounds = Rectangle.Inflate(ClientRectangle, -ThemeDrawing.Px(this, 6), -2);
        textBounds = new Rectangle(textBounds.Left + Padding.Left, textBounds.Top + Padding.Top,
            Math.Max(0, textBounds.Width - Padding.Horizontal), Math.Max(0, textBounds.Height - Padding.Vertical));
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
        else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
            Enabled ? ForeColor : AppTheme.Muted, flags);
    }
}

public sealed class DarkComboBox : ComboBox
{
    public DarkComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = AppTheme.Inset;
        ForeColor = AppTheme.Text;
        ItemHeight = 23;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
        bool inField = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool selected = !inField && (e.State & DrawItemState.Selected) != 0;
        using var brush = new SolidBrush(selected ? AppTheme.Selection : BackColor);
        e.Graphics.FillRectangle(brush, e.Bounds);
        string label = (e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text) ?? "";
        var textBounds = Rectangle.Inflate(e.Bounds, -ThemeDrawing.Px(this, 7), 0);
        TextRenderer.DrawText(e.Graphics, label, e.Font ?? Font, textBounds,
            Enabled ? ForeColor : AppTheme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        base.OnDrawItem(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if ((m.Msg is 0x000F or 0x0085) && IsHandleCreated) // WM_PAINT / WM_NCPAINT
        {
            using var graphics = Graphics.FromHwnd(Handle);
            DrawChrome(graphics);
        }
        else if (m.Msg == 0x0318 && m.WParam != IntPtr.Zero) // WM_PRINTCLIENT
        {
            using var graphics = Graphics.FromHdc(m.WParam);
            DrawChrome(graphics);
        }
    }

    private void DrawChrome(Graphics graphics)
    {
        if (Width < 2 || Height < 2) return;
        int arrowWidth = SystemInformation.VerticalScrollBarWidth + ThemeDrawing.Px(this, 4);
        var arrow = new Rectangle(Width - arrowWidth - 1, 1, arrowWidth, Height - 2);
        using (var brush = new SolidBrush(Enabled ? AppTheme.Raised : AppTheme.Inset))
            graphics.FillRectangle(brush, arrow);
        ThemeDrawing.Chevron(graphics, new Point(arrow.Left + arrow.Width / 2, Height / 2),
            ThemeDrawing.Px(this, 4), false, Enabled ? AppTheme.Muted : AppTheme.Border);
        using var pen = new Pen(Focused ? AppTheme.Accent : AppTheme.Border);
        graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

public sealed class DarkDateTimePicker : DateTimePicker
{
    public DarkDateTimePicker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = AppTheme.Inset;
        ForeColor = AppTheme.Text;
        CalendarMonthBackground = AppTheme.Surface;
        CalendarForeColor = AppTheme.Text;
        CalendarTitleBackColor = AppTheme.Raised;
        CalendarTitleForeColor = AppTheme.Text;
        CalendarTrailingForeColor = AppTheme.Muted;
    }

    protected override void OnValueChanged(EventArgs e) { base.OnValueChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.Clear(BackColor);
        int padding = ThemeDrawing.Px(this, 6);
        int left = padding;
        if (ShowCheckBox)
        {
            int side = ThemeDrawing.Px(this, 13);
            var box = new Rectangle(left, (Height - side) / 2, side, side);
            ThemeDrawing.CheckBox(e.Graphics, box, Checked, Enabled);
            left = box.Right + padding;
        }
        int arrowWidth = SystemInformation.VerticalScrollBarWidth + ThemeDrawing.Px(this, 4);
        var arrow = new Rectangle(Width - arrowWidth - 1, 1, arrowWidth, Height - 2);
        using (var brush = new SolidBrush(Enabled ? AppTheme.Raised : AppTheme.Inset))
            e.Graphics.FillRectangle(brush, arrow);
        ThemeDrawing.Chevron(e.Graphics, new Point(arrow.Left + arrow.Width / 2, Height / 2),
            ThemeDrawing.Px(this, 4), false, AppTheme.Muted);
        string format = Format switch
        {
            DateTimePickerFormat.Long => "D", DateTimePickerFormat.Time => "T",
            DateTimePickerFormat.Custom => CustomFormat ?? "d", _ => "d",
        };
        var textBounds = new Rectangle(left, 0, Math.Max(0, arrow.Left - left - padding), Height);
        TextRenderer.DrawText(e.Graphics, Value.ToString(format, CultureInfo.CurrentCulture), Font, textBounds,
            Enabled && (!ShowCheckBox || Checked) ? ForeColor : AppTheme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var pen = new Pen(Focused ? AppTheme.Accent : AppTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);
        if (!OperatingSystem.IsWindows()) return;
        IntPtr calendar = SendMessage(Handle, 0x1008, IntPtr.Zero, IntPtr.Zero); // DTM_GETMONTHCAL
        if (calendar == IntPtr.Zero) return;
        // The themed MonthCalendar ignores its color properties. Disable only
        // this popup's visual style so the specified dark calendar colors apply.
        _ = SetWindowTheme(calendar, "", "");
        Color[] colors = { AppTheme.Surface, AppTheme.Text, AppTheme.Raised,
            AppTheme.Text, AppTheme.Surface, AppTheme.Muted };
        for (int i = 0; i < colors.Length; i++)
            _ = SendMessage(calendar, 0x100A, new IntPtr(i), new IntPtr(ColorTranslator.ToWin32(colors[i]))); // MCM_SETCOLOR
        Invalidate();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string? id);
}

public sealed class DarkProgressBar : ProgressBar
{
    private readonly System.Windows.Forms.Timer animation;

    public DarkProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = AppTheme.Border;
        ForeColor = AppTheme.Accent;
        animation = new System.Windows.Forms.Timer { Interval = 30 };
        animation.Tick += (_, _) => Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimation(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); UpdateAnimation(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateAnimation(); }
    protected override void OnHandleDestroyed(EventArgs e) { animation?.Stop(); base.OnHandleDestroyed(e); }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is 0x0402 or 0x0405 or 0x040A) { UpdateAnimation(); Invalidate(); }
    }

    private void UpdateAnimation()
    {
        if (animation is null) return; // Base constructors can create a native handle.
        bool animate = IsHandleCreated && Visible && Enabled && Style == ProgressBarStyle.Marquee && MarqueeAnimationSpeed > 0;
        if (animate) animation.Interval = Math.Clamp(MarqueeAnimationSpeed, 16, 100);
        animation.Enabled = animate;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var brush = new SolidBrush(Enabled ? ForeColor : AppTheme.Muted);
        if (Style == ProgressBarStyle.Marquee)
        {
            int width = Math.Max(ThemeDrawing.Px(this, 32), Width / 4);
            double cycle = Environment.TickCount64 % 1800 / 1800D;
            int left = (int)((Width + width) * cycle) - width;
            e.Graphics.FillRectangle(brush, left, 0, width, Height);
        }
        else
        {
            double ratio = (Value - Minimum) / (double)Math.Max(1, Maximum - Minimum);
            e.Graphics.FillRectangle(brush, 0, 0, (int)(Width * ratio), Height);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) animation?.Dispose();
        base.Dispose(disposing);
    }
}

internal static class ThemeDrawing
{
    internal static int Px(Control control, int value) => Math.Max(1, (int)Math.Round(value * control.DeviceDpi / 96D));

    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void Chevron(Graphics graphics, Point center, int radius, bool up, Color color)
    {
        using var pen = new Pen(color, 1.5F);
        int direction = up ? -1 : 1;
        graphics.DrawLines(pen, new[] { new Point(center.X - radius, center.Y - direction * radius / 2),
            new Point(center.X, center.Y + direction * radius / 2), new Point(center.X + radius, center.Y - direction * radius / 2) });
    }

    internal static void CheckBox(Graphics graphics, Rectangle bounds, bool isChecked, bool enabled)
    {
        using (var brush = new SolidBrush(isChecked && enabled ? AppTheme.Primary : AppTheme.Inset))
            graphics.FillRectangle(brush, bounds);
        using (var border = new Pen(isChecked && enabled ? AppTheme.PrimaryHover : AppTheme.Border))
            graphics.DrawRectangle(border, bounds);
        if (!isChecked) return;
        using var pen = new Pen(enabled ? AppTheme.Text : AppTheme.Muted, 1.8F);
        graphics.DrawLines(pen, new[]
        {
            new Point(bounds.Left + bounds.Width / 5, bounds.Top + bounds.Height / 2),
            new Point(bounds.Left + bounds.Width * 2 / 5, bounds.Top + bounds.Height * 3 / 4),
            new Point(bounds.Left + bounds.Width * 4 / 5, bounds.Top + bounds.Height / 4),
        });
    }
}
