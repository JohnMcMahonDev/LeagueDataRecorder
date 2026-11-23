using System.Runtime.InteropServices;

namespace League_Data_Recorder;

public partial class Form1
{
    private WorkspaceTabs? recordingWorkspace;
    private TabPage? recordingHomePage;
    private TabPage? recordingSetupPage;
    private TabPage? recordingDiagnosticsPage;
    private DarkButton? sessionSetupButton;
    private DarkButton? diagnosticsButton;
    private Label? recordingSummary;
    private WorkspaceTabs? gameWorkspace;
    private TabPage? gameCommsPage;
    private ToolStripMenuItem? copyGameDataMenuItem;
    private ComboBox sessionGameTypeCombo = null!;
    private Label? copiedGameStatus;
    private System.Windows.Forms.Timer? copyStatusTimer;
    private CancellationTokenSource? gameCopyCts;
    private bool copyingGameData;
    private bool layingOutDraft;
    private bool? draftTeamsSideBySide;
    private int draftLayoutDpi;

    private void InitializeConsolidatedWorkspace()
    {
        historyNavButton.Text = "Games";
        historyNavButton.AccessibleName = "Games";
        historyPageTab.Text = "Games";
        devicesNavButton.Text = "Settings";
        devicesNavButton.AccessibleName = "Transcription settings";
        devicesPageTab.Text = "Settings";
        devicesTitle.Text = "Transcription settings";
        appSubtitle.Text = "Record sessions · Review games · Analyze performance";
        navigationLayout.Controls.Remove(devicesNavButton);
        navigationLayout.ColumnCount = 3;
        navigationLayout.ColumnStyles.Clear();
        for (int i = 0; i < 3; i++) navigationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigationLayout.Width = 330;
        appHeaderLayout.ColumnCount = 3;
        appHeaderLayout.ColumnStyles[1].Width = 330;
        appHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        devicesNavButton.Dock = DockStyle.None;
        devicesNavButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        devicesNavButton.Size = new Size(88, 34);
        devicesNavButton.Margin = new Padding(12, 0, 0, 0);
        AddWorkspaceControl(appHeaderLayout, devicesNavButton, 2, 0);

        ConsolidateRecordingWorkspace();
        ConsolidateGamesWorkspace();
        UpdateWorkspaceRecordingAction();
        UpdateWorkspaceHistoryActions();
        FormClosed += (_, _) => gameCopyCts?.Cancel();
    }

    private void ConsolidateRecordingWorkspace()
    {
        ResetWorkspaceTable(recordingLayout, 1, 4);
        recordingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 52F, 76F, 16F })
            recordingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        recordingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ResetWorkspaceTable(sessionActionsLayout, 4, 1);
        foreach (float width in new[] { 170F, 156F, 136F })
            sessionActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        sessionActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sessionActionsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var command = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        startRecordingButton.Margin = stopRecordingButton.Margin = Padding.Empty;
        startRecordingButton.Text = "●  Start recording";
        startRecordingButton.BackColor = Color.FromArgb(37, 113, 78);
        startRecordingButton.ForeColor = Color.White;
        startRecordingButton.FlatAppearance.BorderSize = 1;
        startRecordingButton.NavigationSelected = false;
        stopRecordingButton.Text = "■  Stop recording";
        stopRecordingButton.BackColor = Color.FromArgb(139, 49, 61);
        stopRecordingButton.ForeColor = Color.White;
        command.Controls.Add(startRecordingButton);
        command.Controls.Add(stopRecordingButton);
        sessionSetupButton = WorkspaceButton("Session setup", 144);
        diagnosticsButton = WorkspaceButton("Activity log", 124);
        sessionSetupButton.Dock = diagnosticsButton.Dock = DockStyle.Fill;
        AddWorkspaceControl(sessionActionsLayout, command, 0, 0);
        AddWorkspaceControl(sessionActionsLayout, sessionSetupButton, 1, 0);
        AddWorkspaceControl(sessionActionsLayout, diagnosticsButton, 2, 0);
        AddWorkspaceControl(recordingLayout, sessionActionsLayout, 0, 0);
        AddWorkspaceControl(recordingLayout, sessionStatsLayout, 0, 1);

        recordingWorkspace = WorkspaceView();
        recordingHomePage = WorkspacePage("Session overview");
        recordingSetupPage = WorkspacePage("Session setup");
        recordingDiagnosticsPage = WorkspacePage("Activity log");
        recordingWorkspace.TabPages.AddRange(new[] { recordingHomePage, recordingSetupPage, recordingDiagnosticsPage });
        AddWorkspaceControl(recordingLayout, recordingWorkspace, 0, 3);
        settingsTitle.Text = "Session setup";
        var setup = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 580));
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        setup.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddWorkspaceControl(setup, settingsPanel, 1, 0);
        recordingSetupPage.Controls.Add(setup);
        recordingDiagnosticsPage.Controls.Add(logPanel);
        logTitle.Text = "Session activity";
        InitializeSessionGameType();

        var summaryCard = new DarkSurface
        { Dock = DockStyle.Top, Height = 182, BackColor = AppTheme.Surface, Padding = new Padding(22, 16, 22, 16) };
        var summaryLayout = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        summaryLayout.Controls.Add(new Label
        {
            Text = "Session overview", Dock = DockStyle.Fill, Margin = Padding.Empty,
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold), ForeColor = AppTheme.Text,
        }, 0, 0);
        recordingSummary = new Label
        { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = AppTheme.Muted, AutoEllipsis = true };
        AddWorkspaceControl(summaryLayout, recordingSummary, 0, 1);
        summaryCard.Controls.Add(summaryLayout);
        recordingHomePage.Controls.Add(summaryCard);
        void UpdateSummary()
        {
            string folder = folderTextBox.Text.Trim();
            string ally = allyTeamTextBox.Text.Trim(), enemy = enemyTeamTextBox.Text.Trim();
            string[] captures = new[]
            {
                recordVideoCheckbox.Checked ? "Video" : "", recordAudioCheckbox.Checked ? "Voice comms" : "",
                saveReplayCheckbox.Checked ? "Replay" : "", saveRawDataCheckbox.Checked ? "Raw data" : "",
            };
            recordingSummary.Text = (folder.Length > 0 ? "Folder: " + folder : "Choose a recordings folder in Session setup.") +
                "\n\n" + (ally.Length + enemy.Length > 0 ? (ally.Length > 0 ? ally : "Allies") + " vs " +
                (enemy.Length > 0 ? enemy : "Opponents") : "Team names can be set in Session setup.") +
                "\n" + GameTypes.Normalize(sessionGameTypeCombo.Text) + "  ·  " + string.Join("  ·  ", captures.Where(s => s.Length > 0));
            historyToolTip.SetToolTip(recordingSummary, recordingSummary.Text);
        }
        foreach (TextBox box in new[] { folderTextBox, allyTeamTextBox, enemyTeamTextBox })
            box.TextChanged += (_, _) => UpdateSummary();
        foreach (CheckBox box in new[] { recordVideoCheckbox, recordAudioCheckbox, saveReplayCheckbox, saveRawDataCheckbox })
            box.CheckedChanged += (_, _) => UpdateSummary();
        sessionGameTypeCombo.TextChanged += (_, _) => UpdateSummary();
        sessionSetupButton.Click += (_, _) => recordingWorkspace.SelectedTab =
            recordingWorkspace.SelectedTab == recordingSetupPage ? recordingHomePage : recordingSetupPage;
        diagnosticsButton.Click += (_, _) => recordingWorkspace.SelectedTab =
            recordingWorkspace.SelectedTab == recordingDiagnosticsPage ? recordingHomePage : recordingDiagnosticsPage;
        recordingWorkspace.SelectedIndexChanged += (_, _) =>
        {
            StyleSectionButton(sessionSetupButton, recordingWorkspace.SelectedTab == recordingSetupPage);
            StyleSectionButton(diagnosticsButton, recordingWorkspace.SelectedTab == recordingDiagnosticsPage);
        };
        UpdateSummary();
    }

    private void UpdateWorkspaceRecordingAction()
    {
        if (recordingWorkspace is null) return;
        startRecordingButton.BackColor = startRecordingButton.Enabled ? Color.FromArgb(37, 113, 78) : AppTheme.Inset;
        startRecordingButton.FlatAppearance.BorderColor = Color.FromArgb(67, 163, 112);
        stopRecordingButton.BackColor = Color.FromArgb(139, 49, 61);
        if (!stopRecordingButton.Text.StartsWith("■", StringComparison.Ordinal))
            stopRecordingButton.Text = "■  " + stopRecordingButton.Text;
        bool active = startingSession || recordingSession || stoppingSession || savingSession || currentSessionData != null;
        startRecordingButton.Visible = !active;
        stopRecordingButton.Visible = active;
        (active ? stopRecordingButton : startRecordingButton).BringToFront();
    }

    private void ConsolidateGamesWorkspace()
    {
        historyListTitle.Text = "Games";
        ResetWorkspaceTable(historyDetailLayout, 1, 5);
        historyDetailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 64F, 46F })
            historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 4));
        AddWorkspaceControl(historyDetailLayout, matchHeadingLayout, 0, 0);
        AddWorkspaceControl(historyDetailLayout, historyToolbarLayout, 0, 1);

        gameWorkspace = WorkspaceView();
        var overview = WorkspacePage("Overview");
        gameCommsPage = WorkspacePage("Transcript");
        gameWorkspace.TabPages.AddRange(new[] { overview, gameCommsPage });
        gameWorkspace.SelectedIndexChanged += (_, _) => videoReview?.Pause();
        InitializeCommsAnalysisPage();
        AddWorkspaceControl(historyDetailLayout, gameWorkspace, 0, 2);
        ResetWorkspaceTable(matchOverviewLayout, 1, 3);
        matchOverviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        matchOverviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        matchOverviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        matchOverviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        AddWorkspaceControl(matchOverviewLayout, previewPanel, 0, 0);
        AddWorkspaceControl(matchOverviewLayout, draftPanel, 0, 2);
        InitializeCompactDraftLayout();
        overview.Controls.Add(matchOverviewLayout);
        gameCommsPage.Controls.Add(commsPanel);

        ResetWorkspaceTable(historyToolbarLayout, 4, 1);
        historyToolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float width in new[] { 116F, 116F, 80F })
            historyToolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        historyToolbarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        var view = new DarkComboBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 12, 2),
            AccessibleName = "Game view", DropDownWidth = 240 };
        view.Items.AddRange(["Overview", "Transcript", "Comms · Players", "Comms · Callouts"]);
        view.SelectedIndex = 0;
        view.SelectedIndexChanged += (_, _) => gameWorkspace.SelectedIndex = view.SelectedIndex;
        gameWorkspace.SelectedIndexChanged += (_, _) => view.SelectedIndex = gameWorkspace.SelectedIndex;
        historyToolbarLayout.Controls.Add(view, 0, 0);
        AddWorkspaceControl(historyToolbarLayout, transcribeButton, 1, 0);
        copyGameDataMenuItem = new ToolStripMenuItem("Copy game data") { Enabled = false,
            ToolTipText = "Copy both teams' statistics. Paste into Google Sheets with Ctrl+V." };
        copyGameDataMenuItem.Click += async (_, _) => await CopySelectedGameDataAsync();
        recordingActionsMenu.Items.Insert(0, copyGameDataMenuItem);
        AddWorkspaceControl(historyToolbarLayout, openReplayButton, 2, 0);
        AddWorkspaceControl(historyToolbarLayout, moreActionsButton, 3, 0);
        moreActionsButton.Text = "More";

        InitializeTeamNameEditing();
        transcribeButton.Click += (_, _) => { if (gameWorkspace is not null) gameWorkspace.SelectedTab = gameCommsPage; };

        var footer = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        transcriptionStatusLabel.AutoEllipsis = true;
        copiedGameStatus = new Label
        { Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, ForeColor = AppTheme.Muted };
        AddWorkspaceControl(footer, transcriptionStatusLabel, 0, 0);
        AddWorkspaceControl(footer, copiedGameStatus, 1, 0);
        AddWorkspaceControl(historyDetailLayout, footer, 0, 3);
        AddWorkspaceControl(historyDetailLayout, transcriptionProgressBar, 0, 4);
        copyStatusTimer = new System.Windows.Forms.Timer(components) { Interval = 6000 };
        copyStatusTimer.Tick += (_, _) => { copiedGameStatus.Text = ""; copyStatusTimer.Stop(); };
    }

    private void InitializeCompactDraftLayout()
    {
        draftLayout.Controls.Remove(draftTitle);
        draftTitle.Visible = false;
        components.Add(draftTitle);
        draftPanel.AccessibleName = "Match picks";
        foreach (var (_, boxes) in GetDraftIconRows())
            foreach (PictureBox box in boxes)
            {
                box.Dock = DockStyle.Fill;
                box.Padding = Padding.Empty;
                box.SizeMode = PictureBoxSizeMode.Zoom;
            }
        draftPanel.ClientSizeChanged += (_, _) => UpdateCompactDraftLayout();
        draftPanel.DpiChangedAfterParent += (_, _) => UpdateCompactDraftLayout();
        draftPanel.HandleCreated += (_, _) => UpdateCompactDraftLayout();
        UpdateCompactDraftLayout();
    }

    private void UpdateCompactDraftLayout()
    {
        if (layingOutDraft || IsDisposed || Disposing) return;
        int Px(int value) => ThemeDrawing.Px(draftPanel, value);
        int portrait = Px(44), pickGap = Px(6);
        int stripWidth = portrait * 5 + pickGap * 4;
        var padding = new Padding(Px(12), Px(8), Px(12), Px(10));
        bool sideBySide = draftPanel.ClientSize.Width - padding.Horizontal >= stripWidth * 2 + Px(20);
        if (draftTeamsSideBySide == sideBySide && draftLayoutDpi == draftPanel.DeviceDpi) return;

        layingOutDraft = true;
        matchOverviewLayout.SuspendLayout();
        draftPanel.SuspendLayout();
        draftLayout.SuspendLayout();
        try
        {
            draftTeamsSideBySide = sideBySide;
            draftLayoutDpi = draftPanel.DeviceDpi;
            draftPanel.Padding = padding;
            foreach (var (_, boxes) in GetDraftIconRows())
                for (int i = 0; i < boxes.Length; i++)
                    boxes[i].Margin = new Padding(0, 0, i == boxes.Length - 1 ? 0 : pickGap, 0);
            foreach (TableLayoutPanel strip in new[] { bPicksLayout, rPicksLayout })
            {
                strip.Margin = Padding.Empty;
                strip.ColumnStyles.Clear();
                for (int i = 0; i < 5; i++)
                    strip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, portrait + (i < 4 ? pickGap : 0)));
                strip.RowStyles.Clear();
                strip.RowStyles.Add(new RowStyle(SizeType.Absolute, portrait));
            }

            if (sideBySide)
            {
                ResetWorkspaceTable(draftLayout, 4, 2);
                draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, stripWidth));
                draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(20)));
                draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, stripWidth));
                draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                draftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(20)));
                draftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, portrait));
                AddWorkspaceControl(draftLayout, alliesLabel, 0, 0);
                AddWorkspaceControl(draftLayout, enemiesLabel, 2, 0);
                AddWorkspaceControl(draftLayout, bPicksLayout, 0, 1);
                AddWorkspaceControl(draftLayout, rPicksLayout, 2, 1);
            }
            else
            {
                ResetWorkspaceTable(draftLayout, 3, 3);
                draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(56)));
                draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, stripWidth));
                draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                draftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, portrait));
                draftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, pickGap));
                draftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, portrait));
                AddWorkspaceControl(draftLayout, alliesLabel, 0, 0);
                AddWorkspaceControl(draftLayout, enemiesLabel, 0, 2);
                AddWorkspaceControl(draftLayout, bPicksLayout, 1, 0);
                AddWorkspaceControl(draftLayout, rPicksLayout, 1, 2);
            }
            matchOverviewLayout.RowStyles[1].Height = Px(8);
            matchOverviewLayout.RowStyles[2].Height = padding.Vertical +
                (sideBySide ? Px(20) + portrait : portrait * 2 + pickGap);
        }
        finally
        {
            draftLayout.ResumeLayout(true);
            draftPanel.ResumeLayout(true);
            matchOverviewLayout.ResumeLayout(true);
            layingOutDraft = false;
        }
    }

    private void UpdateWorkspaceHistoryActions()
    {
        if (copyGameDataMenuItem is null) return;
        var selected = GetSelectedRecording();
        copyGameDataMenuItem.Enabled = selected != null && !copyingGameData && !deletingRecording &&
            File.Exists(selected.Name + "_ParsedPostGameData.json");
    }

    private async Task CopySelectedGameDataAsync()
    {
        if (copyingGameData || deletingRecording || GetSelectedRecording() is not { } selected) return;
        string path = selected.Name, root = folderTextBox.Text.Trim();
        using var cts = new CancellationTokenSource();
        gameCopyCts = cts;
        copyingGameData = true;
        UpdateHistoryActions();
        ShowGameCopyStatus("Preparing game data…", AppTheme.Muted);
        try
        {
            var result = await Task.Run(() =>
            {
                var data = GameAnalysis.LoadRecording(path, root, cts.Token);
                return (Export: GameSheetExport.Build(data), Warnings: data.Warnings);
            }, cts.Token);
            for (int attempt = 0; ; attempt++)
            {
                if (IsDisposed || Disposing || cts.IsCancellationRequested) return;
                if (!string.Equals(GetSelectedRecording()?.Name, path, StringComparison.OrdinalIgnoreCase))
                { ShowGameCopyStatus("Selection changed. Copy the new game.", AppTheme.Muted); return; }
                try { Clipboard.SetText(result.Export.Text, TextDataFormat.UnicodeText); break; }
                catch (ExternalException) when (attempt < 2) { await Task.Delay(100, cts.Token); }
            }
            ShowGameCopyStatus($"Copied {result.Export.PlayerCount} players · {result.Export.GameId}", AppTheme.Success);
            if (copiedGameStatus is not null)
                historyToolTip.SetToolTip(copiedGameStatus, "Paste into Google Sheets with Ctrl+V. Missing stats are blank.\n" + string.Join("\n", result.Warnings));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                ShowGameCopyStatus(ex is ExternalException ? "Clipboard busy. Try copying again." : "Game data could not be copied.", AppTheme.Error);
                if (copiedGameStatus is not null) historyToolTip.SetToolTip(copiedGameStatus, ex.Message);
                AppendToLog("Could not copy game data: " + ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(gameCopyCts, cts)) gameCopyCts = null;
            copyingGameData = false;
            if (!IsDisposed && !Disposing)
            {
                UpdateHistoryActions();
                if (copiedGameStatus?.Text.Length > 0) copyStatusTimer?.Start();
            }
        }
    }

    private void ShowGameCopyStatus(string text, Color color)
    {
        if (copiedGameStatus is null || IsDisposed || Disposing) return;
        copiedGameStatus.Text = text;
        copiedGameStatus.ForeColor = color;
        copyStatusTimer?.Stop();
        if (!copyingGameData) copyStatusTimer?.Start();
    }

    private static void AddWorkspaceControl(TableLayoutPanel table, Control control, int column, int row)
    {
        // Set cell coordinates before reparenting into a fixed-size table.
        Control? previous = control.Parent;
        bool separateParent = previous != null && !ReferenceEquals(previous, table);
        table.SuspendLayout();
        if (separateParent) previous!.SuspendLayout();
        try
        {
            previous?.Controls.Remove(control);
            table.SetColumnSpan(control, 1);
            table.SetRowSpan(control, 1);
            table.SetCellPosition(control, new TableLayoutPanelCellPosition(column, row));
            table.Controls.Add(control);
        }
        finally
        {
            if (separateParent) previous!.ResumeLayout(true);
            table.ResumeLayout(true);
        }
    }

    private static void ResetWorkspaceTable(TableLayoutPanel table, int columns, int rows)
    {
        table.Controls.Clear();
        table.ColumnStyles.Clear(); table.RowStyles.Clear();
        table.ColumnCount = columns; table.RowCount = rows;
    }

    private static WorkspaceTabs WorkspaceView()
    {
        var tabs = new WorkspaceTabs { Dock = DockStyle.Fill, Margin = Padding.Empty };
        tabs.UseRuntimeNavigation();
        return tabs;
    }

    private static TabPage WorkspacePage(string name) => new(name)
    { BackColor = AppTheme.Background, Padding = Padding.Empty, Margin = Padding.Empty };

    private static DarkButton WorkspaceButton(string text, int width) => new()
    { Text = text, AccessibleName = text, Size = new Size(width, 34), Margin = new Padding(0, 0, 8, 0) };

    private static void StyleSectionButton(DarkButton button, bool selected)
    {
        button.NavigationSelected = selected;
        button.BackColor = selected ? AppTheme.Selection : AppTheme.Surface;
        button.ForeColor = selected ? AppTheme.Text : AppTheme.Muted;
    }

    private static FlowLayoutPanel SectionNavigation(WorkspaceTabs tabs, params (TabPage Page, string Text)[] items)
    {
        var layout = new FlowLayoutPanel
        { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, AutoSize = false };
        var buttons = items.Select(item => (Button: WorkspaceButton(item.Text, Math.Max(82, item.Text.Length * 8 + 24)), item.Page)).ToArray();
        void Update()
        { foreach (var item in buttons) StyleSectionButton(item.Button, tabs.SelectedTab == item.Page); }
        foreach (var item in buttons)
        {
            item.Button.Click += (_, _) => tabs.SelectedTab = item.Page;
            layout.Controls.Add(item.Button);
        }
        tabs.SelectedIndexChanged += (_, _) => Update();
        Update();
        return layout;
    }
}
