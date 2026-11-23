
namespace League_Data_Recorder;

internal sealed class SpeakerRoleEditorDialog : Form
{
    private readonly DataGridView speakers = new DarkTranscriptGrid();
    internal IReadOnlyDictionary<string, string> Assignments { get; private set; } =
        new Dictionary<string, string>();
    internal bool UseAutomaticRoles { get; private set; }

    internal SpeakerRoleEditorDialog(
        string gameName,
        IReadOnlyList<SpeakerRoleEditEntry> entries,
        string? selectedSpeaker
    )
    {
        Text = "Speaker roles · " + gameName;
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9F);
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.Text;
        MinimumSize = new Size(790, 400);
        Size = new Size(960, 540);
        Padding = new Padding(16);
        HandleCreated += (_, _) => AppTheme.ApplyNative(this);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var help = new Label
        {
            Dock = DockStyle.Fill,
            Text =
                "Changes apply to every line for each speaker in this game. Assign each player role once; Coach and Unassigned may repeat.\n"
                + "You can swap roles before saving. Saving confirms and locks the whole map until you choose Use automatic roles.",
            ForeColor = AppTheme.Muted,
            Padding = new Padding(0, 0, 0, 8),
        };
        layout.Controls.Add(help, 0, 0);

        speakers.Dock = DockStyle.Fill;
        speakers.AccessibleName = "Speaker role assignments";
        speakers.AllowUserToAddRows = false;
        speakers.AllowUserToDeleteRows = false;
        speakers.AllowUserToResizeRows = false;
        speakers.RowHeadersVisible = false;
        speakers.MultiSelect = false;
        speakers.SelectionMode = DataGridViewSelectionMode.CellSelect;
        speakers.EditMode = DataGridViewEditMode.EditOnEnter;
        speakers.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
        speakers.BackgroundColor = BackColor;
        speakers.BorderStyle = BorderStyle.None;
        speakers.GridColor = AppTheme.Border;
        speakers.EnableHeadersVisualStyles = false;
        speakers.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = AppTheme.Raised,
            ForeColor = ForeColor,
            Padding = new Padding(4),
        };
        speakers.ColumnHeadersHeight = 32;
        speakers.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = BackColor,
            ForeColor = ForeColor,
            SelectionBackColor = AppTheme.Selection,
            SelectionForeColor = Color.White,
            Padding = new Padding(4),
            WrapMode = DataGridViewTriState.True,
        };
        speakers.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "speaker",
                HeaderText = "Speaker ID",
                ReadOnly = true,
                Width = 105,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            }
        );
        speakers.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "current",
                HeaderText = "Current label",
                ReadOnly = true,
                Width = 110,
                ToolTipText = "A question mark means the automatic label is tentative.",
                SortMode = DataGridViewColumnSortMode.NotSortable,
            }
        );
        var options = new[]
        {
            new { Value = "top", Label = "Top" },
            new { Value = "jungle", Label = "Jungle" },
            new { Value = "mid", Label = "Mid" },
            new { Value = "adc", Label = "ADC" },
            new { Value = "support", Label = "Support" },
            new { Value = "coach", Label = "Coach" },
            new { Value = "unassigned", Label = "Unassigned" },
        };
        speakers.Columns.Add(
            new DataGridViewComboBoxColumn
            {
                Name = "role",
                HeaderText = "Role",
                Width = 125,
                DataSource = options,
                DisplayMember = "Label",
                ValueMember = "Value",
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            }
        );
        speakers.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "example",
                HeaderText = "Example comms",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 180,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            }
        );
        foreach (var entry in entries)
            speakers.Rows.Add(entry.SpeakerId, entry.CurrentLabel, entry.Role, entry.Example);
        layout.Controls.Add(speakers, 0, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 10, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        var automatic = MakeButton("Use automatic roles", 180);
        automatic.Click += (_, _) =>
        {
            UseAutomaticRoles = true;
            DialogResult = DialogResult.OK;
        };
        footer.Controls.Add(automatic, 0, 0);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        var save = MakeButton("Save roles", 100);
        save.Click += (_, _) => SaveRoles();
        var cancel = MakeButton("Cancel", 90);
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        footer.Controls.Add(actions, 1, 0);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        AppTheme.Apply(this);
        Shown += (_, _) =>
        {
            int row = entries
                .ToList()
                .FindIndex(e =>
                    e.SpeakerId.Equals(selectedSpeaker, StringComparison.OrdinalIgnoreCase)
                );
            if (speakers.Rows.Count > 0)
                speakers.CurrentCell = speakers.Rows[Math.Max(0, row)].Cells["role"];
        };
    }

    private Button MakeButton(string text, int width) =>
        new DarkButton()
        {
            Text = text,
            Size = new Size(width, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Raised,
            ForeColor = ForeColor,
            Margin = new Padding(4, 0, 0, 0),
        };

    private void SaveRoles()
    {
        try
        {
            if (!speakers.EndEdit())
                return;
            var assignments = speakers
                .Rows.Cast<DataGridViewRow>()
                .ToDictionary(
                    r => r.Cells["speaker"].Value as string ?? "",
                    r => r.Cells["role"].Value as string ?? ""
                );
            Assignments = TranscribeAudio.ValidateSpeakerRoleEdits(assignments);
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Check speaker roles",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }
}
