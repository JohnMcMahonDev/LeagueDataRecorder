using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace League_Data_Recorder;

public partial class Form1
{
    private ComboBox analysisTeamCombo = null!;
    private ComboBox analysisTypeCombo = null!;
    private ComboBox analysisOpponentCombo = null!;
    private DateTimePicker analysisFromDate = null!;
    private DateTimePicker analysisToDate = null!;
    private Button analysisRefreshButton = null!;
    private Label analysisSummaryLabel = null!;
    private Label analysisStatusLabel = null!;
    private Label analysisChampionLabel = null!;
    private DarkTranscriptGrid analysisPlayersGrid = null!;
    private DarkTranscriptGrid analysisChampionsGrid = null!;
    private DarkTranscriptGrid analysisObjectivesGrid = null!;
    private AnalysisDataset analysisDataset = new();
    private List<AnalysisTeamGame> analysisFilteredGames = new();
    private CancellationTokenSource? analysisLoadCts;
    private LoadingOverlay analysisLoading = null!;
    private int analysisLoadVersion;
    private bool analysisRendering;
    private bool analysisShowAllStats;
    private WorkspaceTabs analysisWorkspace = null!;
    private TabPage analysisPlayersPage = null!;
    private TabPage analysisChampionsPage = null!;
    private DarkButton analysisFiltersButton = null!;
    private DarkSurface analysisExtraFilters = null!;
    private RowStyle analysisExtraFiltersRow = null!;

    private sealed record AnalysisChoice(string Key, string Name)
    {
        public override string ToString() => Name;
    }

    // Runtime composition keeps calculations and file access out of InitializeComponent.
    // The navigation button and page remain normal, selectable controls in the designer.
    private void InitializeAnalysisPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty,
            BackColor = AppTheme.Background,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        analysisExtraFiltersRow = new RowStyle(SizeType.Absolute, 0);
        layout.RowStyles.Add(analysisExtraFiltersRow);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        heading.Controls.Add(AnalysisLabel("Team analysis", true), 0, 0);
        analysisRefreshButton = AnalysisButton("Refresh");
        heading.Controls.Add(analysisRefreshButton, 1, 0);
        layout.Controls.Add(heading, 0, 0);

        var filters = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        filters.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        analysisTeamCombo = AnalysisCombo("Team");
        analysisTypeCombo = AnalysisCombo("Game type");
        analysisOpponentCombo = AnalysisCombo("Opponent");
        analysisFromDate = AnalysisDate("Start date (check to enable)");
        analysisToDate = AnalysisDate("End date (check to enable)");
        filters.Controls.Add(AnalysisLabel("Team"), 0, 0);
        filters.Controls.Add(analysisTeamCombo, 0, 1);
        filters.Controls.Add(AnalysisLabel("Game type"), 1, 0);
        filters.Controls.Add(analysisTypeCombo, 1, 1);
        analysisFiltersButton = WorkspaceButton("Filters", 118);
        analysisFiltersButton.Dock = DockStyle.Top;
        analysisFiltersButton.Height = 31;
        filters.Controls.Add(analysisFiltersButton, 2, 1);
        analysisTypeCombo.Items.Add(new AnalysisChoice("", "All game types"));
        analysisTypeCombo.SelectedIndex = 0;
        layout.Controls.Add(filters, 0, 1);

        analysisExtraFilters = new DarkSurface
        { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Padding = new Padding(10, 5, 10, 5), Margin = new Padding(0, 0, 0, 8), Visible = false };
        var advanced = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = Padding.Empty };
        foreach (float width in new[] { 42F, 29F, 29F }) advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        advanced.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Control[] extra = { analysisOpponentCombo, analysisFromDate, analysisToDate };
        string[] captions = { "Opponent", "From (optional)", "To (optional)" };
        for (int i = 0; i < extra.Length; i++)
        {
            advanced.Controls.Add(AnalysisLabel(captions[i]), i, 0);
            advanced.Controls.Add(extra[i], i, 1);
        }
        var reset = WorkspaceButton("Reset", 78);
        reset.Dock = DockStyle.Fill;
        advanced.Controls.Add(reset, 3, 1);
        reset.Click += (_, _) =>
        {
            analysisRendering = true;
            try
            {
                if (analysisOpponentCombo.Items.Count > 0) analysisOpponentCombo.SelectedIndex = 0;
                analysisFromDate.Checked = analysisToDate.Checked = false;
            }
            finally { analysisRendering = false; }
            RenderAnalysis();
        };
        analysisExtraFilters.Controls.Add(advanced);
        layout.Controls.Add(analysisExtraFilters, 0, 2);
        analysisFiltersButton.Click += (_, _) =>
        {
            // Use row height as the state: Control.Visible also reflects hidden parent pages.
            bool show = analysisExtraFiltersRow.Height == 0;
            analysisExtraFiltersRow.Height = show ? ThemeDrawing.Px(this, 78) : 0;
            analysisExtraFilters.Visible = show;
            UpdateAnalysisFilterSummary();
        };

        analysisSummaryLabel = AnalysisLabel("Choose a team to view its saved games.");
        analysisSummaryLabel.ForeColor = AppTheme.Text;
        analysisSummaryLabel.BackColor = AppTheme.Surface;
        analysisSummaryLabel.Padding = new Padding(12, 3, 12, 3);
        analysisSummaryLabel.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(analysisSummaryLabel, 0, 3);

        analysisWorkspace = WorkspaceView();
        analysisPlayersPage = WorkspacePage("Players");
        analysisChampionsPage = WorkspacePage("Champion breakdown");
        var objectivesPage = WorkspacePage("Objectives");
        analysisWorkspace.TabPages.AddRange(new[] { analysisPlayersPage, analysisChampionsPage, objectivesPage });
        var detailLayout = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        var navButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var playersTab = WorkspaceButton("Players", 100);
        var objectivesTab = WorkspaceButton("Objectives", 110);
        var moreStats = WorkspaceButton("More stats", 118);
        playersTab.Click += (_, _) => analysisWorkspace.SelectedTab = analysisPlayersPage;
        objectivesTab.Click += (_, _) => analysisWorkspace.SelectedTab = objectivesPage;
        void UpdateNavigation()
        {
            StyleSectionButton(playersTab, analysisWorkspace.SelectedTab != objectivesPage);
            StyleSectionButton(objectivesTab, analysisWorkspace.SelectedTab == objectivesPage);
            moreStats.Visible = analysisWorkspace.SelectedTab != objectivesPage;
        }
        analysisWorkspace.SelectedIndexChanged += (_, _) => UpdateNavigation();
        UpdateNavigation();
        navButtons.Controls.AddRange(new Control[] { playersTab, objectivesTab });
        nav.Controls.Add(navButtons, 0, 0);
        nav.Controls.Add(moreStats, 1, 0);
        moreStats.Click += (_, _) =>
        {
            analysisShowAllStats = !analysisShowAllStats;
            StyleSectionButton(moreStats, analysisShowAllStats);
            ApplyAnalysisColumnVisibility(analysisPlayersGrid, false);
            ApplyAnalysisColumnVisibility(analysisChampionsGrid, true);
        };
        detailLayout.Controls.Add(nav, 0, 0);
        detailLayout.Controls.Add(analysisWorkspace, 0, 1);
        layout.Controls.Add(detailLayout, 0, 4);

        var playersLayout = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        playersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        playersLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        playersLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var playerHeading = AnalysisLabel("Player averages · Click a player name for champion stats", true);
        playerHeading.AutoEllipsis = true;
        analysisPlayersGrid = AnalysisGrid("Player averages");
        playersLayout.Controls.Add(playerHeading, 0, 0);
        playersLayout.Controls.Add(analysisPlayersGrid, 0, 1);
        analysisPlayersPage.Controls.Add(playersLayout);

        var championsLayout = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        championsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        championsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        championsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var championHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        championHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        championHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var back = WorkspaceButton("Back to players", 118);
        back.Click += (_, _) => { analysisWorkspace.SelectedTab = analysisPlayersPage; analysisPlayersGrid.Focus(); };
        championHeading.Controls.Add(back, 0, 0);
        analysisChampionLabel = AnalysisLabel("Champion breakdown", true);
        analysisChampionLabel.AutoEllipsis = true;
        championHeading.Controls.Add(analysisChampionLabel, 1, 0);
        analysisChampionsGrid = AnalysisGrid("Champion averages; champion names are available on each portrait");
        ConfigureAnalysisChampionCells();
        championsLayout.Controls.Add(championHeading, 0, 0);
        championsLayout.Controls.Add(analysisChampionsGrid, 0, 1);
        analysisChampionsPage.Controls.Add(championsLayout);

        var objectivesLayout = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        objectivesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        objectivesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        objectivesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        objectivesLayout.Controls.Add(AnalysisLabel("With = secured by your team · Vs = secured by the opponent. Missing histories are excluded."), 0, 0);
        analysisObjectivesGrid = AnalysisGrid("Team objective results");
        objectivesLayout.Controls.Add(analysisObjectivesGrid, 0, 1);
        objectivesPage.Controls.Add(objectivesLayout);

        analysisStatusLabel = AnalysisLabel("Open Analysis to load saved games. Per-minute stats use each game's duration.");
        analysisStatusLabel.AutoEllipsis = true;
        layout.Controls.Add(analysisStatusLabel, 0, 5);
        analysisPageTab.Controls.Add(layout);
        analysisLoading = LoadingOverlay.Attach(analysisPageTab);
        analysisRefreshButton.Click += async (_, _) => await RefreshAnalysisAsync();
        analysisTeamCombo.SelectedIndexChanged += (_, _) =>
        {
            if (analysisRendering) return;
            UpdateAnalysisOpponents();
            RenderAnalysis();
        };
        analysisTypeCombo.SelectedIndexChanged += (_, _) => RenderAnalysis();
        analysisOpponentCombo.SelectedIndexChanged += (_, _) => RenderAnalysis();
        analysisFromDate.ValueChanged += (_, _) => RenderAnalysis();
        analysisToDate.ValueChanged += (_, _) => RenderAnalysis();
        analysisPlayersGrid.SelectionChanged += (_, _) => RenderAnalysisChampions();
        analysisPlayersGrid.CellContentClick += (_, e) =>
        { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && analysisPlayersGrid.Columns[e.ColumnIndex].Name == "Name") OpenAnalysisChampionBreakdown(); };
        analysisPlayersGrid.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            OpenAnalysisChampionBreakdown(); e.Handled = e.SuppressKeyPress = true;
        };
        tabControl1.Selected += async (_, e) =>
        { if (e.TabPage == analysisPageTab) await RefreshAnalysisAsync(); };
        folderTextBox.TextChanged += async (_, _) =>
        {
            analysisLoadCts?.Cancel();
            if (tabControl1.SelectedTab == analysisPageTab) await RefreshAnalysisAsync();
        };
        FormClosed += (_, _) => { ++analysisLoadVersion; analysisLoadCts?.Cancel(); };
        InitializeAnalysisChampionIcons();
        BindAnalysisStats(analysisPlayersGrid, new List<AnalysisStatRow>(), "Player");
        BindAnalysisStats(analysisChampionsGrid, new List<AnalysisStatRow>(), "Champion");
    }

    private void OpenAnalysisChampionBreakdown()
    {
        if (analysisPlayersGrid.CurrentRow == null) return;
        RenderAnalysisChampions();
        analysisWorkspace.SelectedTab = analysisChampionsPage;
        analysisChampionsGrid.Focus();
    }

    private void UpdateAnalysisFilterSummary()
    {
        int active = (AnalysisSelectedKey(analysisOpponentCombo).Length > 0 ? 1 : 0) +
            (analysisFromDate.Checked ? 1 : 0) + (analysisToDate.Checked ? 1 : 0);
        analysisFiltersButton.Text = active > 0 ? $"Filters ({active})" : "Filters";
        StyleSectionButton(analysisFiltersButton, analysisExtraFiltersRow.Height > 0 || active > 0);
    }

    private void analysisNavButton_Click(object? sender, EventArgs e) => tabControl1.SelectedTab = analysisPageTab;

    private async Task RefreshAnalysisAsync()
    {
        if (IsDisposed || Disposing) return;
        analysisLoadCts?.Cancel();
        var cts = new CancellationTokenSource();
        analysisLoadCts = cts;
        int version = ++analysisLoadVersion;
        string root = folderTextBox.Text.Trim();
        string team = AnalysisSelectedKey(analysisTeamCombo);
        analysisRefreshButton.Enabled = false;
        analysisLoading.SetBusy(true, "Loading saved games and comms analysis…");
        analysisStatusLabel.Text = "Loading game statistics and analyzing saved comms…";
        try
        {
            var dataset = await Task.Run(async () =>
            {
                var data = GameAnalysis.Load(root, cts.Token);
                var catalog = await LeagueAliasCatalog.LoadAsync(cancellationToken: cts.Token);
                foreach (string path in data.Games.Select(g => g.RecordingPath).Distinct())
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        var transcript = await ReadTranscriptAsync(path);
                        if (transcript is null) continue;
                        var roles = await TranscribeAudio.ReadSpeakerRoleLabelsAsync(transcript.JsonPath, cancellationToken: cts.Token);
                        var report = CommsAnalysis.Analyze(path, transcript, roles, catalog, cts.Token);
                        GameAnalysis.AttachComms(data, path, report);
                        cts.Token.ThrowIfCancellationRequested();
                        CommsAnalysis.SaveReport(path, report);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
                    { data.Warnings.Add($"{Path.GetFileName(path)} comms: {ex.Message}"); }
                }
                return data;
            }, cts.Token);
            if (IsDisposed || Disposing || version != analysisLoadVersion) return;
            analysisDataset = dataset;
            analysisRendering = true;
            try
            {
                string previousType = AnalysisSelectedKey(analysisTypeCombo);
                analysisTypeCombo.Items.Clear();
                analysisTypeCombo.Items.Add(new AnalysisChoice("", "All game types"));
                foreach (string type in GameTypes.Suggestions.Concat(dataset.Games.Select(g => g.GameType))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                    analysisTypeCombo.Items.Add(new AnalysisChoice(type, type));
                analysisTypeCombo.SelectedItem = analysisTypeCombo.Items.Cast<AnalysisChoice>()
                    .FirstOrDefault(t => t.Key.Equals(previousType, StringComparison.OrdinalIgnoreCase)) ?? analysisTypeCombo.Items[0];
                analysisTeamCombo.Items.Clear();
                var teams = dataset.Games.GroupBy(g => g.TeamKey)
                    .OrderByDescending(g => g.Select(x => x.GameId).Distinct().Count())
                    .ThenBy(g => g.First().TeamName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new AnalysisChoice(g.Key, g.First().TeamName)).ToArray();
                analysisTeamCombo.Items.AddRange(teams.Cast<object>().ToArray());
                var chosen = teams.FirstOrDefault(t => t.Key == team) ??
                    teams.FirstOrDefault(t => t.Name.Equals(allyTeamTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)) ??
                    teams.FirstOrDefault();
                if (chosen != null) analysisTeamCombo.SelectedItem = chosen;
            }
            finally { analysisRendering = false; }
            UpdateAnalysisOpponents();
            RenderAnalysis();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        {
            if (IsDisposed || Disposing || version != analysisLoadVersion) return;
            analysisDataset = new AnalysisDataset();
            analysisDataset.Warnings.Add("Could not load saved games: " + ex.Message);
            analysisRendering = true;
            analysisTeamCombo.Items.Clear();
            analysisRendering = false;
            UpdateAnalysisOpponents();
            RenderAnalysis();
        }
        finally
        {
            if (ReferenceEquals(analysisLoadCts, cts)) analysisLoadCts = null;
            cts.Dispose();
            if (!IsDisposed && !Disposing && version == analysisLoadVersion)
            { analysisRefreshButton.Enabled = true; analysisLoading.SetBusy(false); }
        }
    }

    private void UpdateAnalysisOpponents()
    {
        string previous = AnalysisSelectedKey(analysisOpponentCombo);
        string team = AnalysisSelectedKey(analysisTeamCombo);
        analysisRendering = true;
        try
        {
            analysisOpponentCombo.Items.Clear();
            analysisOpponentCombo.Items.Add(new AnalysisChoice("", "All opponents"));
            foreach (var opponent in analysisDataset.Games.Where(g => g.TeamKey == team)
                .GroupBy(g => g.OpponentKey).Select(g => new AnalysisChoice(g.Key, g.First().OpponentName)).OrderBy(x => x.Name))
                analysisOpponentCombo.Items.Add(opponent);
            analysisOpponentCombo.SelectedItem = analysisOpponentCombo.Items.Cast<AnalysisChoice>()
                .FirstOrDefault(x => x.Key == previous) ?? analysisOpponentCombo.Items[0];
        }
        finally { analysisRendering = false; }
    }

    private void RenderAnalysis()
    {
        if (analysisRendering || analysisSummaryLabel is null) return;
        UpdateAnalysisFilterSummary();
        DateTime? from = analysisFromDate.Checked ? analysisFromDate.Value.Date : null;
        DateTime? to = analysisToDate.Checked ? analysisToDate.Value.Date : null;
        analysisFilteredGames = GameAnalysis.Filter(analysisDataset, AnalysisSelectedKey(analysisTeamCombo),
            AnalysisSelectedKey(analysisTypeCombo), AnalysisSelectedKey(analysisOpponentCombo), from, to);
        string previousPlayer = AnalysisSelectedPlayer();
        var games = analysisFilteredGames;
        int wins = games.Count(g => g.Won == true), losses = games.Count(g => g.Won == false);
        string Rate(int w, int count) => count == 0 ? "—" : ((double)w / count).ToString("P1");
        string Duration(IEnumerable<AnalysisTeamGame> items) => GameAnalysis.Average(items.Select(g => g.Minutes)) is double minutes
            ? $"{(int)Math.Round(minutes * 60) / 60}:{(int)Math.Round(minutes * 60) % 60:00}" : "—";
        analysisSummaryLabel.Text = games.Count == 0 ? "No games match these filters." :
            $"{games.Count} games   ·   {wins} wins   ·   {losses} losses   ·   {Rate(wins, wins + losses)} win rate" +
            $"   ·   {games.SelectMany(g => g.Players).Select(p => p.Identity).Distinct().Count()} players\n" +
            $"Team Outlook {OutlookScore(games.Select(g => g.Outlook).Average())}   ·   Avg game {Duration(games)}   ·   Avg win {Duration(games.Where(g => g.Won == true))}" +
            $"   ·   Avg loss {Duration(games.Where(g => g.Won == false))}" +
            (games.Count > wins + losses ? $"   ·   {games.Count - wins - losses} unknown results" : "");
        analysisRendering = true;
        try
        {
            BindAnalysisStats(analysisPlayersGrid, GameAnalysis.PlayerRows(games), "Player");
            if (analysisPlayersGrid.Rows.Count > 0)
            {
                var row = analysisPlayersGrid.Rows.Cast<DataGridViewRow>()
                    .FirstOrDefault(r => (string?)r.Cells["Key"].Value == previousPlayer) ?? analysisPlayersGrid.Rows[0];
                analysisPlayersGrid.ClearSelection();
                analysisPlayersGrid.CurrentCell = row.Cells["Name"];
                row.Selected = true;
            }
            RenderAnalysisObjectives();
        }
        finally { analysisRendering = false; }
        RenderAnalysisChampions();
        analysisStatusLabel.Text = from > to ? "Start date must be on or before end date." :
            analysisDataset.Games.Count == 0 ? analysisDataset.Warnings.FirstOrDefault() ?? "No saved post-game files found in this folder." :
            $"{games.Count} games shown. Rates are averaged per game; — = unavailable or zero denominator." +
            (analysisDataset.Warnings.Count > 0 ? $"  {analysisDataset.Warnings.Count} data issue(s); hover here for details." : "");
        historyToolTip.SetToolTip(analysisStatusLabel, analysisStatusLabel.Text + "\n" + string.Join("\n", analysisDataset.Warnings.Take(8)));
    }

    private string AnalysisSelectedPlayer() => analysisPlayersGrid.CurrentRow?.Cells["Key"].Value as string ?? "";
    private static string AnalysisSelectedKey(ComboBox combo) => (combo.SelectedItem as AnalysisChoice)?.Key ?? "";

    private void RenderAnalysisChampions()
    {
        if (analysisRendering) return;
        string identity = AnalysisSelectedPlayer();
        string player = analysisPlayersGrid.CurrentRow?.Cells["Name"].Value as string ?? "Select a player above";
        analysisChampionLabel.Text = player + " · Champion breakdown";
        if (identity.Length == 0 && analysisWorkspace.SelectedTab == analysisChampionsPage)
            analysisWorkspace.SelectedTab = analysisPlayersPage;
        BindAnalysisStats(analysisChampionsGrid, GameAnalysis.ChampionRows(analysisFilteredGames, identity), "Champion");
    }

    private void RenderAnalysisObjectives()
    {
        var table = new DataTable();
        table.Columns.Add("Objective", typeof(string));
        foreach (string prefix in new[] { "With", "Vs" })
        {
            table.Columns.Add(prefix + "Games", typeof(int));
            table.Columns.Add(prefix + "Wins", typeof(int));
            table.Columns.Add(prefix + "Losses", typeof(int));
            table.Columns.Add(prefix + "WinRate", typeof(double));
        }
        foreach (string objective in GameAnalysis.ObjectiveNames)
        {
            var row = table.NewRow();
            row["Objective"] = objective;
            foreach (string prefix in new[] { "With", "Vs" })
            {
                var obtained = analysisFilteredGames.Where(g =>
                {
                    var owner = prefix == "With" ? g : analysisDataset.Games.FirstOrDefault(o =>
                        o.GameId == g.GameId && o.TeamId != g.TeamId);
                    return owner != null && owner.Objectives.TryGetValue(objective, out var taken) && taken == true;
                }).ToList();
                int wins = obtained.Count(g => g.Won == true), losses = obtained.Count(g => g.Won == false);
                row[prefix + "Games"] = obtained.Count;
                row[prefix + "Wins"] = wins;
                row[prefix + "Losses"] = losses;
                row[prefix + "WinRate"] = wins + losses == 0 ? DBNull.Value : (object)((double)wins / (wins + losses));
            }
            table.Rows.Add(row);
        }
        analysisObjectivesGrid.DataSource = table;
        foreach (DataGridViewColumn column in analysisObjectivesGrid.Columns)
        {
            column.Width = column.Index == 0 ? 120 : 106;
            column.HeaderText = column.Name.Replace("WinRate", " win %").Replace("Games", " games")
                .Replace("Wins", " wins").Replace("Losses", " losses");
            column.DefaultCellStyle.Format = column.Name.EndsWith("WinRate", StringComparison.Ordinal) ? "P1" : "0";
        }
        analysisObjectivesGrid.Columns["Objective"]!.Frozen = true;
    }

    private void BindAnalysisStats(DataGridView grid, List<AnalysisStatRow> rows, string title)
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string)); table.Columns.Add("Role", typeof(string));
        string[] metrics = { "Kills", "Deaths", "Assists", "Kda", "DamagePerMinute", "TakenPerMinute", "CsPerMinute",
            "GoldPerMinute", "VisionPerMinute", "WinRate", "CommsShare", "FightCommsShare", "Callouts", "Tracking", "SummonerCallouts", "UltimateCallouts", "EnemyCallouts", "EnemySummonerCallouts", "EnemyUltimateCallouts", "RoamCalls", "ObjectiveCalls" };
        foreach (string metric in metrics) table.Columns.Add(metric, typeof(double));
        table.Columns.Add("Games", typeof(int));
        table.Columns.Add("DamagePerMinutePerDeath", typeof(double)); table.Columns.Add("Key", typeof(string));
        object Value(double? number) => number.HasValue ? number.Value : DBNull.Value;
        foreach (var r in rows)
            table.Rows.Add(title == "Champion" ? AnalysisChampionDisplayName(r.Name) : r.Name, r.Role, Value(r.Kills), Value(r.Deaths), Value(r.Assists), Value(r.Kda),
                Value(r.DamagePerMinute), Value(r.TakenPerMinute), Value(r.CsPerMinute), Value(r.GoldPerMinute),
                Value(r.VisionPerMinute), Value(r.WinRate), Value(r.CommsShare), Value(r.FightCommsShare), Value(r.Callouts),
                Value(r.Tracking), Value(r.SummonerCallouts), Value(r.UltimateCallouts), Value(r.EnemyCallouts),
                Value(r.EnemySummonerCallouts), Value(r.EnemyUltimateCallouts), Value(r.RoamCalls), Value(r.ObjectiveCalls), r.Games, Value(r.DamagePerMinutePerDeath), r.Key);
        bool champions = title == "Champion";
        if (champions)
        {
            table.Columns.Add("ChampionIcon", typeof(Image));
            foreach (DataRow row in table.Rows) row["ChampionIcon"] = GetAnalysisChampionIcon((string)row["Name"]);
        }
        grid.DataSource = null;
        grid.Columns.Clear();
        grid.AutoGenerateColumns = false;
        foreach (DataColumn dataColumn in table.Columns)
        {
            DataGridViewColumn column = dataColumn.ColumnName == "ChampionIcon"
                ? new DataGridViewImageColumn { ImageLayout = DataGridViewImageCellLayout.Zoom }
                : dataColumn.ColumnName == "Name" && !champions
                    ? new DataGridViewLinkColumn { LinkColor = AppTheme.Accent, ActiveLinkColor = AppTheme.Accent,
                        VisitedLinkColor = AppTheme.Accent, TrackVisitedState = false, LinkBehavior = LinkBehavior.AlwaysUnderline }
                    : new DataGridViewTextBoxColumn();
            column.Name = column.DataPropertyName = dataColumn.ColumnName;
            column.ValueType = dataColumn.DataType;
            grid.Columns.Add(column);
        }
        grid.DataSource = table;
        grid.Columns["Key"]!.Visible = false;
        var headers = new Dictionary<string, string>
        {
            ["Name"] = title, ["Kda"] = "KDA", ["DamagePerMinute"] = "Damage/min", ["TakenPerMinute"] = "Taken/min",
            ["CsPerMinute"] = "CS/min", ["GoldPerMinute"] = "Gold/min", ["VisionPerMinute"] = "Vision/min",
            ["WinRate"] = "Win %", ["DamagePerMinutePerDeath"] = "Dmg/min/death",
            ["CommsShare"] = "Comms share", ["FightCommsShare"] = "Fight comms share", ["Callouts"] = "All spell calls", ["Tracking"] = "Tracking", ["RoamCalls"] = "Roam calls", ["ObjectiveCalls"] = "Objective calls",
            ["SummonerCallouts"] = "All summoner calls", ["UltimateCallouts"] = "All ultimate calls",
            ["EnemyCallouts"] = "Enemy callouts",
            ["EnemySummonerCallouts"] = "Enemy Summoner Spell Timer Callout", ["EnemyUltimateCallouts"] = "Enemy Ult Timer Callout",
        };
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.HeaderText = headers.GetValueOrDefault(column.Name, column.Name);
            column.Width = column.Name == "Name" ? 200 : column.Name == "Role" ? 76 :
                column.Name == "Games" ? 62 : column.Name == "WinRate" ? 78 :
                column.Name is "Kills" or "Deaths" or "Assists" ? 60 :
                column.Name.Contains("PerMinute") ? 106 : 70;
            if (column.Name is "CommsShare" or "FightCommsShare") column.Width = 112;
            if (column.Name is "RoamCalls" or "ObjectiveCalls") column.Width = 112;
            if (column.Name.EndsWith("Callouts", StringComparison.Ordinal)) column.Width = column.Name == "EnemyCallouts" ? 108 : 136;
            column.DefaultCellStyle.Alignment = column.ValueType == typeof(string) ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Format = column.Name is "WinRate" or "CommsShare" or "FightCommsShare" ? "P1" : column.Name == "Games" ? "0" : "0.00";
            column.SortMode = DataGridViewColumnSortMode.Automatic;
        }
        string[] order = { champions ? "ChampionIcon" : "Name", "Role", "Games", "CommsShare", "FightCommsShare", "EnemyCallouts", "Tracking", "RoamCalls", "ObjectiveCalls", "EnemySummonerCallouts", "EnemyUltimateCallouts", "Callouts", "SummonerCallouts", "UltimateCallouts", "WinRate", "Kills", "Deaths", "Assists", "Kda",
            "CsPerMinute", "DamagePerMinute", "TakenPerMinute", "GoldPerMinute", "VisionPerMinute", "DamagePerMinutePerDeath", "Key" };
        for (int i = 0; i < order.Length; i++) grid.Columns[order[i]]!.DisplayIndex = i;
        grid.Columns[champions ? "ChampionIcon" : "Name"]!.Frozen = true;
        grid.RowTemplate.Height = ThemeDrawing.Px(grid, champions ? 54 : 32);
        foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
        if (champions)
        {
            var portraits = grid.Columns["ChampionIcon"]!;
            portraits.HeaderText = "Champ";
            portraits.Width = ThemeDrawing.Px(grid, 54);
            portraits.SortMode = DataGridViewColumnSortMode.NotSortable;
            portraits.DefaultCellStyle.Padding = new Padding(2);
            portraits.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            portraits.DefaultCellStyle.NullValue = GetAnalysisChampionIcon("");
            portraits.DefaultCellStyle.Format = "";
        }
        ApplyAnalysisColumnVisibility(grid, champions);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.Columns["CommsShare"]!.ToolTipText = "Average player comms share across games with timed transcripts. Recovered overlap is allocated across identified speakers.";
        grid.Columns["FightCommsShare"]!.ToolTipText = "Average player comms share in estimated fight windows. Games without usable fight timing are excluded.";
        grid.Columns["Callouts"]!.ToolTipText = "Average spell calls about enemies, allied teammates or unresolved targets. The caller's own spells are excluded. Tracking, roams and objective calls are separate.";
        grid.Columns["EnemyCallouts"]!.ToolTipText = "Average per eligible game. " + CommsAnalysis.EnemyCountingRule;
        grid.Columns["EnemySummonerCallouts"]!.ToolTipText = "Average summoner spell calls about enemies, including one count for an unexpanded team-wide call.";
        grid.Columns["EnemyUltimateCallouts"]!.ToolTipText = "Average ultimate calls about enemies.";
        grid.Columns["RoamCalls"]!.ToolTipText = "Average per transcribed game. " + CommsAnalysis.RoamCountingRule;
        grid.Columns["ObjectiveCalls"]!.ToolTipText = "Average per transcribed game. " + CommsAnalysis.ObjectiveCountingRule;
        grid.Columns["Tracking"]!.ToolTipText = "Average tracking calls per game with usable clock timing. " + CommsAnalysis.JungleCountingRule;
        grid.Columns["SummonerCallouts"]!.ToolTipText = "Average per transcribed game. " + CommsAnalysis.SummonerCountingRule;
        grid.Columns["UltimateCallouts"]!.ToolTipText = "Average per transcribed game. " + CommsAnalysis.UltimateCountingRule;
        var byKey = rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in grid.Rows)
            if (row.Cells["Key"].Value is string key && byKey.TryGetValue(key, out var stats))
                foreach (string metric in new[] { "CommsShare", "FightCommsShare", "Callouts", "Tracking", "SummonerCallouts", "UltimateCallouts", "EnemyCallouts", "EnemySummonerCallouts", "EnemyUltimateCallouts", "RoamCalls", "ObjectiveCalls" })
                {
                    int coverage = metric == "FightCommsShare" ? stats.FightCommsGames : metric == "Tracking" ? stats.TrackingGames : stats.CommsGames;
                    row.Cells[metric].ToolTipText = grid.Columns[metric]!.ToolTipText + $"\nAvailable in {coverage} of {stats.Games} games. Missing games are excluded.";
                }
        grid.Columns["Name"]!.DefaultCellStyle.ForeColor = AppTheme.Accent;
        if (!champions)
        {
            grid.Columns["Name"]!.ToolTipText = "Click a player name to view their champion statistics. Enter opens the selected player.";
            foreach (DataGridViewRow row in grid.Rows) row.Cells["Name"].ToolTipText = grid.Columns["Name"]!.ToolTipText;
        }
        grid.Columns["Kills"]!.DefaultCellStyle.ForeColor = AppTheme.Success;
        grid.Columns["Deaths"]!.DefaultCellStyle.ForeColor = AppTheme.Error;
        grid.Columns["Assists"]!.DefaultCellStyle.ForeColor = Color.FromArgb(244, 213, 140);
        grid.Columns["DamagePerMinute"]!.ToolTipText = "Damage dealt to champions divided by game minutes, averaged across games.";
        grid.Columns["CsPerMinute"]!.ToolTipText = "Lane minions plus neutral minions; jungle subcategories are not added twice.";
        grid.Columns["DamagePerMinutePerDeath"]!.ToolTipText = "Average damage/min divided by average deaths, matching the sheet. Zero deaths displays —.";
        grid.Columns["Kda"]!.ToolTipText = "(Total kills + total assists) / total deaths. Zero deaths displays —.";
        grid.Columns["WinRate"]!.ToolTipText = "Wins / games with a known result. Unknown results are excluded.";
    }

    private void ApplyAnalysisColumnVisibility(DataGridView grid, bool champions)
    {
        if (!grid.Columns.Contains("Name")) return;
        grid.Columns["Name"]!.Visible = !champions;
        grid.Columns["Role"]!.Visible = !champions;
        foreach (string metric in new[] { "TakenPerMinute", "GoldPerMinute", "VisionPerMinute", "DamagePerMinutePerDeath", "Callouts", "SummonerCallouts", "UltimateCallouts", "EnemySummonerCallouts", "EnemyUltimateCallouts" })
            grid.Columns[metric]!.Visible = analysisShowAllStats;
    }

    private static DarkTranscriptGrid AnalysisGrid(string name)
    {
        var grid = new DarkTranscriptGrid
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = name, ReadOnly = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = AppTheme.Surface, BorderStyle = BorderStyle.None, GridColor = AppTheme.Border,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 40, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = AppTheme.Surface, ForeColor = AppTheme.Text, SelectionBackColor = AppTheme.Selection,
            SelectionForeColor = AppTheme.Text, NullValue = "—", Padding = new Padding(6, 0, 6, 0),
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = AppTheme.Inset;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = AppTheme.Raised, ForeColor = AppTheme.Muted, SelectionBackColor = AppTheme.Raised,
            SelectionForeColor = AppTheme.Text, Padding = new Padding(6), WrapMode = DataGridViewTriState.True,
        };
        grid.RowTemplate.Height = 32;
        grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex < 0 || !grid.Columns[e.ColumnIndex].Name.EndsWith("WinRate", StringComparison.Ordinal)
                || e.Value is not double rate) return;
            e.CellStyle!.BackColor = rate >= .6 ? Color.FromArgb(30, 65, 52) :
                rate < .4 ? Color.FromArgb(75, 38, 46) : Color.FromArgb(70, 60, 35);
            e.CellStyle.ForeColor = AppTheme.Text;
        };
        return grid;
    }

    private static Label AnalysisLabel(string text, bool heading = false) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 8, 0),
        ForeColor = heading ? AppTheme.Text : AppTheme.Muted,
    };
    private static ComboBox AnalysisCombo(string name) => new DarkComboBox()
    {
        Dock = DockStyle.Top, Margin = new Padding(0, 0, 10, 0), DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat, BackColor = AppTheme.Raised, ForeColor = AppTheme.Text, AccessibleName = name,
        DropDownWidth = 350,
    };
    private static DateTimePicker AnalysisDate(string name) => new DarkDateTimePicker()
    {
        Dock = DockStyle.Top, Margin = new Padding(0, 0, 10, 0), Format = DateTimePickerFormat.Short,
        ShowCheckBox = true, Checked = false, AccessibleName = name, MinimumSize = new Size(0, 31),
    };
    private static Button AnalysisButton(string text)
    {
        var button = new DarkButton
        {
            Text = text, AccessibleName = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            Margin = new Padding(4, 0, 0, 8), BackColor = AppTheme.Raised, ForeColor = AppTheme.Text, Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = AppTheme.Border;
        return button;
    }

}
