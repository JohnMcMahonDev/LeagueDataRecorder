using System.Runtime.InteropServices;

namespace League_Data_Recorder;

internal static class AppTheme
{
    internal static readonly Color Background = Color.FromArgb(18, 21, 27);
    internal static readonly Color Surface = Color.FromArgb(25, 29, 37);
    internal static readonly Color Inset = Color.FromArgb(20, 24, 31);
    internal static readonly Color Raised = Color.FromArgb(34, 40, 51);
    internal static readonly Color Border = Color.FromArgb(49, 57, 71);
    internal static readonly Color Hover = Color.FromArgb(43, 51, 65);
    internal static readonly Color Selection = Color.FromArgb(36, 56, 82);
    internal static readonly Color Primary = Color.FromArgb(54, 101, 170);
    internal static readonly Color PrimaryHover = Color.FromArgb(66, 118, 194);
    internal static readonly Color Text = Color.FromArgb(227, 232, 241);
    internal static readonly Color Muted = Color.FromArgb(157, 169, 188);
    internal static readonly Color Accent = Color.FromArgb(146, 184, 239);
    internal static readonly Color Success = Color.FromArgb(123, 204, 167);
    internal static readonly Color Error = Color.FromArgb(235, 147, 155);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> styled = new();

    // This also covers runtime-created dialogs and DataGridView editing controls.
    // Hooks are installed once; no repaint timer or replacement scroll model is used.
    internal static void Apply(Control root)
    {
        if (root.IsDisposed || root.Disposing ||
            System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
            return;
        if (!styled.TryGetValue(root, out _))
        {
            styled.Add(root, new object());
            if (root is Form form)
            {
                form.HandleCreated += (_, _) => ApplyNative(form);
                if (form.IsHandleCreated) ApplyNative(form);
            }
            else if (root is TextBoxBase or ListView or ComboBox or ScrollBar ||
                     root is ScrollableControl { AutoScroll: true })
            {
                root.HandleCreated += (_, _) => ThemeScrollbars(root);
                if (root.IsHandleCreated) ThemeScrollbars(root);
            }
            if (root is TextBoxBase text)
            {
                text.ForeColor = Text;
                if (text is not RichTextBox) text.BackColor = Inset;
            }
            if (root is ComboBox combo)
            {
                combo.BackColor = Inset;
                combo.ForeColor = Text;
                combo.FlatStyle = FlatStyle.Flat;
            }
            if (root is DataGridView grid)
            {
                grid.BackgroundColor = grid.DefaultCellStyle.BackColor.IsEmpty ? Surface : grid.DefaultCellStyle.BackColor;
                grid.GridColor = Border;
                grid.BorderStyle = BorderStyle.None;
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.RowHeadersDefaultCellStyle.BackColor = Raised;
                grid.RowHeadersDefaultCellStyle.ForeColor = Muted;
                grid.EditingControlShowing += (_, e) => Apply(e.Control);
            }
            if (root is Button button)
            {
                button.FlatAppearance.MouseOverBackColor = Hover;
                button.FlatAppearance.MouseDownBackColor = Selection;
                button.FlatAppearance.BorderColor = Border;
                button.UseVisualStyleBackColor = false;
            }
            root.ControlAdded += (_, e) => { if (e.Control is { } child) Apply(child); };
        }
        foreach (Control child in root.Controls) Apply(child);
    }

    internal static void StyleToolTip(ToolTip tooltip)
    {
        tooltip.OwnerDraw = true;
        tooltip.BackColor = Raised;
        tooltip.ForeColor = Text;
        tooltip.Popup += (_, e) => e.ToolTipSize = new Size(e.ToolTipSize.Width + 12, e.ToolTipSize.Height + 8);
        tooltip.Draw += (_, e) =>
        {
            using (var brush = new SolidBrush(Raised)) e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font,
                Rectangle.Inflate(e.Bounds, -6, -4), Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        };
    }

    internal static void ApplyNative(Form form)
    {
        if (!OperatingSystem.IsWindows() || SystemInformation.HighContrast)
            return;
        int enabled = 1;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int border = ColorTranslator.ToWin32(Border);
            int caption = ColorTranslator.ToWin32(Background);
            int text = ColorTranslator.ToWin32(Text);
            _ = DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
            _ = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        }
    }

    internal static void ThemeScrollbars(Control control)
    {
        // Let WinForms choose each native control's theme when its app-wide
        // dark mode is active (combos and lists use different theme classes).
#pragma warning disable WFO5001
        if (Application.IsDarkModeEnabled) return;
#pragma warning restore WFO5001
        if (OperatingSystem.IsWindows() && !SystemInformation.HighContrast && control.IsHandleCreated)
            _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int size
    );

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string? id);
}

public sealed class WorkspaceTabs : TabControl
{
    private bool runtimeNavigation;

    public WorkspaceTabs() { }

    protected override CreateParams CreateParams
    {
        get
        {
#pragma warning disable WFO5001
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
#pragma warning restore WFO5001
            return base.CreateParams;
        }
    }

    internal void UseRuntimeNavigation()
    {
        runtimeNavigation = true;
        Appearance = TabAppearance.FlatButtons;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(1, 1);
        Multiline = true;
        TabStop = false;
        PerformLayout();
    }

    protected override void WndProc(ref Message message)
    {
        // Native tabs remain visible in Visual Studio for selecting each page.
        if (message.Msg == 0x1328 && runtimeNavigation && !DesignMode)
        {
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
    }
}

public sealed class DarkSurface : Panel
{
    public DarkSurface()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
#pragma warning disable WFO5001
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
#pragma warning restore WFO5001
            return base.CreateParams;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode && AutoScroll) AppTheme.ThemeScrollbars(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width > 0 && Height > 0)
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}

public sealed class DarkRecordingList : ListView
{
    public DarkRecordingList()
    {
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode) AppTheme.ThemeScrollbars(this);
    }
}

public sealed class DarkTranscriptGrid : DataGridView
{
    public DarkTranscriptGrid()
    {
        DoubleBuffered = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
#pragma warning disable WFO5001
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
#pragma warning restore WFO5001
            return base.CreateParams;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode) AppTheme.ThemeScrollbars(this);
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    internal DarkMenuRenderer()
        : base(new DarkMenuColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(AppTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? AppTheme.Muted : e.Item.ForeColor;
        base.OnRenderItemText(e);
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => AppTheme.Raised;
        public override Color ImageMarginGradientBegin => AppTheme.Raised;
        public override Color ImageMarginGradientMiddle => AppTheme.Raised;
        public override Color ImageMarginGradientEnd => AppTheme.Raised;
        public override Color MenuItemSelected => AppTheme.Selection;
        public override Color MenuItemSelectedGradientBegin => AppTheme.Selection;
        public override Color MenuItemSelectedGradientEnd => AppTheme.Selection;
        public override Color MenuItemBorder => AppTheme.Border;
        public override Color MenuBorder => AppTheme.Border;
        public override Color SeparatorDark => AppTheme.Border;
        public override Color SeparatorLight => AppTheme.Raised;
    }
}
