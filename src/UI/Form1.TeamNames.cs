
namespace League_Data_Recorder;

public partial class Form1
{
    private ToolStripMenuItem? editTeamNamesMenuItem;
    private void InitializeTeamNameEditing()
    {
        historyScrollView.Columns.Add(new ColumnHeader { Text = "Teams", Width = 220 });
        historyScrollView.TileSize = new Size(historyScrollView.TileSize.Width, ThemeDrawing.Px(this, 94));
        editTeamNamesMenuItem = new ToolStripMenuItem("Edit game properties…") { Enabled = false };
        editTeamNamesMenuItem.Click += async (_, _) =>
        {
            if (deletingRecording || GetSelectedRecording() is not { } selected) return;
            string path = selected.Name;
            try
            {
                var names = ReadRecordingTeamNames(path);
                using var dialog = new Form { Text = "Edit game properties", StartPosition = FormStartPosition.CenterParent,
                    ClientSize = new Size(480, 262), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
                    MinimizeBox = false, ShowInTaskbar = false, BackColor = AppTheme.Surface, ForeColor = AppTheme.Text,
                    Font = Font, Padding = new Padding(16) };
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                foreach (int height in new[] { 38, 38, 40, 62, 42 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                var ally = new TextBox { Text = names.Ally, MaxLength = 80, Dock = DockStyle.Fill, BackColor = AppTheme.Inset, ForeColor = AppTheme.Text, AccessibleName = "Ally team name" };
                var enemy = new TextBox { Text = names.Enemy, MaxLength = 80, Dock = DockStyle.Fill, BackColor = AppTheme.Inset, ForeColor = AppTheme.Text, AccessibleName = "Enemy team name" };
                layout.Controls.Add(AnalysisLabel("Ally team"), 0, 0); layout.Controls.Add(ally, 1, 0);
                layout.Controls.Add(AnalysisLabel("Enemy team"), 0, 1); layout.Controls.Add(enemy, 1, 1);
                var type = CreateGameTypeCombo(names.GameType);
                layout.Controls.Add(AnalysisLabel("Game type"), 0, 2); layout.Controls.Add(type, 1, 2);
                var help = AnalysisLabel("Applies to this recording, including Analysis and copied game data. Other games keep their properties. Game type is used by the Analysis filter.");
                layout.Controls.Add(help, 0, 3); layout.SetColumnSpan(help, 2);
                var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var save = WorkspaceButton("Save", 86); save.DialogResult = DialogResult.OK;
                var cancel = WorkspaceButton("Cancel", 86); cancel.DialogResult = DialogResult.Cancel;
                actions.Controls.Add(save); actions.Controls.Add(cancel);
                layout.Controls.Add(actions, 0, 4); layout.SetColumnSpan(actions, 2);
                dialog.Controls.Add(layout); dialog.AcceptButton = save; dialog.CancelButton = cancel;
                void ValidateNames() => save.Enabled = RecordingTeamNames.Clean(ally.Text).Length > 0 && RecordingTeamNames.Clean(enemy.Text).Length > 0;
                ally.TextChanged += (_, _) => ValidateNames(); enemy.TextChanged += (_, _) => ValidateNames(); ValidateNames();
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                new RecordingTeamNames { Ally = ally.Text, Enemy = enemy.Text, GameType = type.Text }.Save(path);
                UpdateSelectedTeamNames();
                if (selected.SubItems.Count > 2) selected.SubItems[2].Text = ally.Text.Trim() + " vs " + enemy.Text.Trim();
                selected.ToolTipText = $"{ally.Text.Trim()} vs {enemy.Text.Trim()} · {selected.Text} · {selected.SubItems[1].Text}";
                ShowGameCopyStatus("Game properties saved.", AppTheme.Success);
                await RefreshAnalysisAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
            { MessageBox.Show(this, "Could not save game properties: " + ex.Message, "Game properties", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        recordingActionsMenu.Items.Insert(1, editTeamNamesMenuItem);
    }
    private RecordingTeamNames ReadRecordingTeamNames(string gamePath)
        => RecordingTeamNames.ReadDisplay(gamePath, folderTextBox.Text.Trim());
    private void UpdateSelectedTeamNames()
    {
        alliesLabel.Text = "Allies"; enemiesLabel.Text = "Enemy";
        if (GetSelectedRecording() is not { } selected) return;
        try
        {
            var names = ReadRecordingTeamNames(selected.Name);
            if (names.Ally.Length > 0) alliesLabel.Text = names.Ally;
            if (names.Enemy.Length > 0) enemiesLabel.Text = names.Enemy;
            if (names.Ally.Length > 0 && names.Enemy.Length > 0)
                gameTitleLabel.Text = names.Ally + " vs " + names.Enemy;
            gameMetadataLabel.Text = $"{selected.Text}  ·  {selected.SubItems[1].Text}  ·  {names.GameType}";
            historyToolTip.SetToolTip(gameTitleLabel, names.GameType + " · More → Edit game properties");
            historyToolTip.SetToolTip(alliesLabel, "Ally team: " + names.Ally);
            historyToolTip.SetToolTip(enemiesLabel, "Enemy team: " + names.Enemy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { AppendToLog("Could not read team names: " + ex.Message); }
    }
}
