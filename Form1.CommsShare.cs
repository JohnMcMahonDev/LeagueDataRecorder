using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace League_Data_Recorder;

public partial class Form1
{
    private DarkButton? commsShareButton;
    private IReadOnlyDictionary<string, CommsShare>? displayedCommsShares;
    private IReadOnlyDictionary<string, string> displayedShareChampions = new Dictionary<string, string>();

    private void InitializeCommsShareDisplay()
    {
        // Keep the transcript and draft clear. A single header button opens the
        // complete breakdown instead of repeating percentages on every portrait.
        commsShareButton = new DarkButton
        {
            Text = "Analyze comms", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2), Enabled = false,
            AccessibleName = "Analyze speaking time, teamfight share and callouts"
        };
        commsHeadingLayout.ColumnCount = 3;
        commsHeadingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ThemeDrawing.Px(this, 116)));
        commsHeadingLayout.Controls.Add(commsShareButton, 2, 0);
        commsShareButton.Click += async (_, _) => await ShowCommsAnalysisAsync();

        // Fixed square portraits stay centered within the five draft cells.
        // No cover painting or stretching to the width of the cell.
        foreach (var (_, boxes) in GetDraftIconRows())
            foreach (PictureBox box in boxes)
            {
                int size = ThemeDrawing.Px(box, 44);
                box.Dock = DockStyle.None;
                box.Anchor = AnchorStyles.None;
                box.Size = new Size(size, size);
                box.MaximumSize = box.Size;
                box.Margin = new Padding(2);
                box.SizeMode = PictureBoxSizeMode.Zoom;
            }
        matchOverviewLayout.RowStyles[2].Height = ThemeDrawing.Px(matchOverviewLayout, 144);
        draftLayout.RowStyles[1].Height = 50;
        draftLayout.RowStyles[2].Height = 50;
        draftTitle.Text = "Match picks";
    }

    private void ClearCommsShares()
    {
        ResetCommsAnalysis();
        displayedCommsShares = null;
        displayedShareChampions = new Dictionary<string, string>();
        if (commsShareButton is not null) commsShareButton.Enabled = false;
        draftTitle.Text = "Match picks";
    }

    private void UpdateCommsShares(TranscriptionResult? transcript, IReadOnlyDictionary<string, string>? roles,
        IReadOnlyDictionary<string, string> champions)
    {
        ClearCommsShares();
        if (transcript is null) return;
        displayedCommsShares = CommsShareCalculator.Calculate(transcript.Words ?? [], roles);
        displayedShareChampions = new Dictionary<string, string>(champions);
        SetCommsAnalysisInput(transcript, roles);
        if (commsShareButton is not null)
        {
            commsShareButton.Enabled = true;
            historyToolTip.SetToolTip(commsShareButton, "Open player speaking time, estimated teamfight share, spell callouts, jungle tracking, roams and objective calls.");
        }
    }

    private void ShowCommsShare()
    {
        if (displayedCommsShares is not { } shares) return;
        using var dialog = new Form
        {
            Text = "Comms share", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, ClientSize = new Size(ThemeDrawing.Px(this, 450), ThemeDrawing.Px(this, 344)),
            BackColor = AppTheme.Surface, ForeColor = AppTheme.Text, Font = Font,
            Padding = new Padding(ThemeDrawing.Px(this, 14))
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ThemeDrawing.Px(this, 36)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ThemeDrawing.Px(this, 38)));
        layout.Controls.Add(new Label { Text = "Share of timed speech · Silence excluded", Dock = DockStyle.Fill,
            ForeColor = AppTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 0);
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None,
            BackgroundColor = AppTheme.Surface, GridColor = AppTheme.Raised, EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, TabStop = false
        };
        grid.DefaultCellStyle.BackColor = AppTheme.Surface;
        grid.DefaultCellStyle.ForeColor = AppTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = AppTheme.Raised;
        grid.DefaultCellStyle.SelectionForeColor = AppTheme.Text;
        grid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Raised;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.Muted;
        grid.ColumnHeadersHeight = ThemeDrawing.Px(this, 28);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = ThemeDrawing.Px(this, 28);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Player", FillWeight = 55, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Share", FillWeight = 22, SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Speaking time", FillWeight = 30, SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        foreach (string role in new[] { "top", "jungle", "mid", "adc", "support", "coach", "unknown", "overlap" })
        {
            string name = role switch { "adc" => "ADC", "unknown" => "Unassigned", "overlap" => "Overlap", _ => char.ToUpperInvariant(role[0]) + role[1..] };
            if (displayedShareChampions.TryGetValue(role, out string? champion)) name += " · " + AnalysisChampionDisplayName(champion);
            var share = shares[role];
            grid.Rows.Add(name, CommsAnalysis.Roles.Take(5).Contains(role) ? $"{share.Percentage:F1}%" : "—", $"{(int)(share.Seconds / 60):00}:{(int)(share.Seconds % 60):00}");
        }
        grid.ClearSelection();
        layout.Controls.Add(grid, 0, 1);
        var close = new DarkButton { Text = "Close", Dock = DockStyle.Right, Width = ThemeDrawing.Px(this, 90),
            DialogResult = DialogResult.OK, Margin = new Padding(0, ThemeDrawing.Px(this, 6), 0, 0) };
        layout.Controls.Add(close, 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = dialog.CancelButton = close;
        dialog.ShowDialog(this);
    }
}
