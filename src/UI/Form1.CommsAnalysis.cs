using System.Text.Json;

namespace League_Data_Recorder;

public partial class Form1
{
    private TabPage? gameCommsAnalysisPage, gameCommsCallsPage;
    private bool IsCommsAnalysisView => gameWorkspace?.SelectedTab is { } page &&
        (page == gameCommsAnalysisPage || page == gameCommsCallsPage);
    private DarkButton? runCommsAnalysisButton;
    private Label? commsAnalysisStatus;
    private LoadingOverlay? commsAnalysisLoading;
    private Label? commsCalloutSummary;
    private DataGridView? commsAnalysisPlayers, commsAnalysisCalls;
    private ComboBox? commsCallFilter, commsSpeakerFilter, commsScopeFilter;
    private TranscriptionResult? commsAnalysisTranscript;
    private IReadOnlyDictionary<string, string> commsAnalysisRoles = new Dictionary<string, string>();
    private CommsAnalysisReport? commsAnalysisReport;
    private CancellationTokenSource? commsAnalysisCts;
    private string? commsAnalysisGame;

    private void InitializeCommsAnalysisPage()
    {
        if (gameWorkspace is null) return;
        gameCommsAnalysisPage = WorkspacePage("Comms analysis");
        gameCommsCallsPage = WorkspacePage("Comms callouts");
        gameWorkspace.TabPages.AddRange([gameCommsAnalysisPage, gameCommsCallsPage]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        heading.Controls.Add(AnalysisLabel("Comms analysis", true), 0, 0);
        var methods = WorkspaceButton("How it works", 102);
        methods.Click += (_, _) => ShowCommsAnalysisMethods();
        runCommsAnalysisButton = WorkspaceButton("Analyze comms", 122);
        runCommsAnalysisButton.Enabled = false;
        runCommsAnalysisButton.Click += async (_, _) => await RunCommsAnalysisAsync();
        heading.Controls.Add(methods, 1, 0);
        heading.Controls.Add(runCommsAnalysisButton, 2, 0);
        layout.Controls.Add(heading, 0, 0);
        commsAnalysisStatus = AnalysisLabel("Select a transcribed game to analyze its comms.");
        layout.Controls.Add(commsAnalysisStatus, 0, 1);
        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var summary = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var calls = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = false };
        body.Controls.AddRange([summary, calls]);
        layout.Controls.Add(body, 0, 2);
        commsAnalysisPlayers = NewCommsAnalysisGrid("Speaking time and callout counts by role");
        AddCommsColumns(commsAnalysisPlayers, ("Player", 140), ("Speech", 68), ("Comms share", 85), ("Fight speech", 85),
            ("Fight comms share", 110), ("Enemy callouts", 115), ("Tracking", 80), ("Objective calls", 108), ("Roam calls", 96));
        commsAnalysisPlayers.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        string[] countRules = [CommsAnalysis.EnemyCountingRule, CommsAnalysis.JungleCountingRule, CommsAnalysis.ObjectiveCountingRule, CommsAnalysis.RoamCountingRule];
        for (int i = 0; i < countRules.Length; i++) commsAnalysisPlayers.Columns[i + 5].ToolTipText = countRules[i];
        summary.Controls.Add(commsAnalysisPlayers);
        historyToolTip.SetToolTip(commsAnalysisPlayers,
            "Click a callout count to see matching calls. Speaking time excludes silence; overlap and fight shares are estimates. See How it works for counting rules.");
        commsAnalysisPlayers.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 5 || commsCallFilter is null || commsSpeakerFilter is null || commsScopeFilter is null ||
                commsAnalysisPlayers.Rows[e.RowIndex].Tag is not CommsAnalysisPlayer player) return;
            commsCallFilter.SelectedIndex = e.ColumnIndex switch { 6 => 3, 7 => 4, 8 => 5, _ => 0 };
            commsScopeFilter.SelectedIndex = e.ColumnIndex switch { 5 => 1, 7 => 4, _ => 0 };
            commsSpeakerFilter.SelectedIndex = Array.IndexOf(CommsAnalysis.Roles, player.Role) + 1;
            gameWorkspace.SelectedTab = gameCommsCallsPage;
        };
        commsAnalysisCalls = NewCommsAnalysisGrid("Detected callouts; double-click a row to open its video timestamp");
        AddCommsColumns(commsAnalysisCalls, ("Time", 95), ("Speaker", 88), ("About", 100), ("Callout type", 180), ("Callout details", 200), ("Transcript", 300));
        commsAnalysisCalls.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        commsAnalysisCalls.CellDoubleClick += (_, e) => SeekToCommsFinding(commsAnalysisCalls, e.RowIndex);
        var callLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        callLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        callLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        callLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        callLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        commsCallFilter = new DarkComboBox { Width = 210, Margin = new Padding(0, 0, 12, 0), DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Filter callout type" };
        commsCallFilter.Items.AddRange(["All callouts", "Summoner spells", "Ultimates", "Tracking", "Objective calls", "Roam calls"]);
        commsCallFilter.SelectedIndex = 0;
        commsCallFilter.SelectedIndexChanged += (_, _) =>
        {
            if (commsScopeFilter is not null)
            {
                if (commsCallFilter.SelectedIndex == 4) commsScopeFilter.SelectedIndex = 4;
                else if (commsScopeFilter.SelectedIndex == 4) commsScopeFilter.SelectedIndex = 0;
            }
            RenderCommsCallouts();
        };
        historyToolTip.SetToolTip(commsCallFilter, "Filter by the type of callout. Double-click a callout to play it in Overview.");
        commsSpeakerFilter = new DarkComboBox { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Filter callouts by speaker" };
        commsSpeakerFilter.Items.Add("All speakers");
        commsSpeakerFilter.Items.AddRange(CommsAnalysis.Roles.Select(CommsRoleName).Cast<object>().ToArray());
        commsSpeakerFilter.SelectedIndex = 0;
        commsSpeakerFilter.SelectedIndexChanged += (_, _) => RenderCommsCallouts();
        commsScopeFilter = new DarkComboBox { Width = 165, DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Whose spells or actions were called" };
        commsScopeFilter.Items.AddRange(["All targets", "Enemy", "Allied teammates", "Unknown side", "Team objectives"]);
        commsScopeFilter.SelectedIndex = 0;
        commsScopeFilter.SelectedIndexChanged += (_, _) => RenderCommsCallouts();
        historyToolTip.SetToolTip(commsScopeFilter, "Enemy = opponents; Allied teammates = other or unspecified allies. The caller's own spell callouts are excluded.");
        var callFilters = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = true };
        callFilters.Controls.Add(commsCallFilter);
        callFilters.Controls.Add(commsScopeFilter);
        callFilters.Controls.Add(commsSpeakerFilter);
        commsCalloutSummary = AnalysisLabel("Detected callouts appear here automatically. Double-click a row to play it in Overview.");
        callLayout.Controls.Add(callFilters, 0, 0);
        callLayout.Controls.Add(commsCalloutSummary, 0, 1);
        callLayout.Controls.Add(commsAnalysisCalls, 0, 2);
        calls.Controls.Add(callLayout);
        gameCommsAnalysisPage.Controls.Add(layout);
        commsAnalysisLoading = LoadingOverlay.Attach(body);
        gameWorkspace.Selected += async (_, e) =>
        {
            if (!IsCommsAnalysisView) return;
            var page = gameWorkspace.SelectedTab!;
            page.Controls.Add(layout);
            bool showCalls = page == gameCommsCallsPage;
            summary.Visible = !showCalls;
            calls.Visible = showCalls;
            if (showCalls) calls.BringToFront(); else summary.BringToFront();
            if (commsAnalysisCts is not null) commsAnalysisLoading?.SetBusy(true, "Analyzing comms…");
            if (commsAnalysisReport is null && commsAnalysisTranscript is not null && commsAnalysisCts is null)
                await RunCommsAnalysisAsync();
        };
        FormClosed += (_, _) => commsAnalysisCts?.Cancel();
    }

    private static DataGridView NewCommsAnalysisGrid(string name)
    {
        var grid = AnalysisGrid(name);
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.DefaultCellStyle.NullValue = "—";
        return grid;
    }
    private static void AddCommsColumns(DataGridView grid, params (string Title, int Width)[] columns)
    {
        foreach (var column in columns) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = column.Title,
            HeaderText = column.Title, Width = column.Width, MinimumWidth = 50, SortMode = DataGridViewColumnSortMode.NotSortable });
    }
    private void ResetCommsAnalysis()
    {
        commsAnalysisCts?.Cancel();
        commsAnalysisLoading?.SetBusy(false);
        commsAnalysisTranscript = null;
        commsAnalysisReport = null;
        commsAnalysisGame = null;
        if (commsCallFilter is not null) commsCallFilter.SelectedIndex = 0;
        if (commsSpeakerFilter is not null) commsSpeakerFilter.SelectedIndex = 0;
        if (commsScopeFilter is not null) commsScopeFilter.SelectedIndex = 0;
        if (runCommsAnalysisButton is not null) runCommsAnalysisButton.Enabled = false;
        foreach (var grid in new[] { commsAnalysisPlayers, commsAnalysisCalls }) grid?.Rows.Clear();
        if (commsAnalysisStatus is not null) commsAnalysisStatus.Text = "Select a transcribed game to analyze its comms.";
        if (commsCalloutSummary is not null) commsCalloutSummary.Text = "Detected callouts appear here automatically. Double-click a row to play it in Overview.";
    }
    private void SetCommsAnalysisInput(TranscriptionResult transcript, IReadOnlyDictionary<string, string>? roles)
    {
        commsAnalysisGame = GetSelectedRecording()?.Name;
        commsAnalysisTranscript = transcript;
        commsAnalysisRoles = roles is null ? new Dictionary<string, string>() : new Dictionary<string, string>(roles);
        if (runCommsAnalysisButton is not null) runCommsAnalysisButton.Enabled = true;
        if (commsAnalysisStatus is not null) commsAnalysisStatus.Text = "Ready. Analyze comms shares, spell callouts, tracking and objective calls from the saved transcript.";
        if (IsCommsAnalysisView) _ = RunCommsAnalysisAsync();
    }
    private async Task ShowCommsAnalysisAsync()
    {
        if (gameWorkspace is null || gameCommsAnalysisPage is null) return;
        gameWorkspace.SelectedTab = gameCommsAnalysisPage;
        if (commsAnalysisReport is null && commsAnalysisCts is null) await RunCommsAnalysisAsync();
    }
    private async Task RunCommsAnalysisAsync()
    {
        if (commsAnalysisTranscript is not { } transcript || commsAnalysisGame is not { } path ||
            GetSelectedRecording()?.Name != path || commsAnalysisCts is not null) return;
        var roles = commsAnalysisRoles;
        using var cts = new CancellationTokenSource();
        commsAnalysisCts = cts;
        commsAnalysisLoading?.SetBusy(true, "Analyzing comms and team Outlook…");
        UpdateHistoryActions();
        if (runCommsAnalysisButton is not null) runCommsAnalysisButton.Enabled = false;
        if (commsAnalysisStatus is not null) commsAnalysisStatus.Text = "Analyzing saved comms and game events…";
        try
        {
            var result = await Task.Run(async () =>
            {
                var catalog = await LeagueAliasCatalog.LoadAsync(cancellationToken: cts.Token);
                var report = CommsAnalysis.Analyze(path, transcript, roles, catalog, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                CommsAnalysis.SaveReport(path, report);
                return report;
            }, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed || Disposing || commsAnalysisGame != path ||
                !ReferenceEquals(transcript, commsAnalysisTranscript)) return;
            commsAnalysisReport = result;
            RenderCommsAnalysis();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            if (cts.IsCancellationRequested || IsDisposed || Disposing || commsAnalysisGame != path) return;
            if (commsAnalysisStatus is not null) commsAnalysisStatus.Text = "Could not analyze this game: " + ex.Message;
            AppendToLog("Comms analysis: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(commsAnalysisCts, cts))
            { commsAnalysisCts = null; if (!IsDisposed && !Disposing) commsAnalysisLoading?.SetBusy(false); }
            if (!IsDisposed && !Disposing && runCommsAnalysisButton is not null)
            {
                UpdateHistoryActions();
                runCommsAnalysisButton.Enabled = commsAnalysisTranscript is not null;
                runCommsAnalysisButton.Text = commsAnalysisReport is null ? "Analyze comms" : "Reanalyze";
                // A selection/role change can finish loading while the previous analysis is cancelling.
                if (IsCommsAnalysisView && commsAnalysisTranscript is not null &&
                    !ReferenceEquals(transcript, commsAnalysisTranscript) && commsAnalysisReport is null)
                    _ = RunCommsAnalysisAsync();
            }
        }
    }
    private static string OutlookScore(double? value) => value.HasValue ? (Math.Clamp(value.Value, 0, 1) * 100).ToString("0.0") + "/100" : "—";
    private void RenderCommsAnalysis()
    {
        if (commsAnalysisReport is not { } report || commsAnalysisPlayers is null) return;
        commsAnalysisPlayers.Rows.Clear();
        foreach (var p in report.Players)
        {
            string name = CommsRoleName(p.Role);
            if (displayedShareChampions.TryGetValue(p.Role, out string? champion)) name += " · " + champion;
            bool available = !CommsAnalysis.Roles.Take(5).Contains(p.Role) || p.HasSpeakerAssignment;
            int row = commsAnalysisPlayers.Rows.Add(name, CommsTime(p.Seconds), CommsPercent(p.Share), CommsTime(p.FightSeconds),
                CommsPercent(p.FightShare), available ? p.EnemyCallouts : "—", available ? p.JungleMentions?.ToString() ?? "—" : "—", available ? p.ObjectiveCalls : "—", available ? p.RoamCalls : "—");
            commsAnalysisPlayers.Rows[row].Tag = p;
            for (int column = 5; column < 9; column++)
                commsAnalysisPlayers.Rows[row].Cells[column].ToolTipText =
                    commsAnalysisPlayers.Columns[column].ToolTipText + "\nClick to show this speaker's matching callouts.";
        }
        RenderCommsCallouts();
        string timing = report.AudioToGameSeconds.HasValue
            ? "All results are generated automatically and saved for this game."
            : "Game-clock timing is unavailable: Tracking and fight shares show —.";
        int enemy = report.Findings.Count(f => CommsAnalysis.IsSpellCall(f) && f.Scope == "Enemy");
        commsAnalysisStatus!.Text = $"{enemy} enemy callouts · " +
            (report.AudioToGameSeconds.HasValue ? $"{report.Findings.Count(f => f.Category == "Jungle <15:00")} Tracking · " : "") +
            $"{report.Findings.Count(f => f.Category == "Objective")} objective calls · {report.Findings.Count(f => f.Category == "Roam")} roam calls.\n{timing}";

        commsAnalysisPlayers.ClearSelection();
    }
    private void RenderCommsCallouts()
    {
        if (commsAnalysisCalls is null) return;
        commsAnalysisCalls.Rows.Clear();
        string filter = commsCallFilter?.SelectedIndex switch { 1 => "Summoner", 2 => "Ultimate", 3 => "Jungle <15:00", 4 => "Objective", 5 => "Roam", _ => "" };
        int speakerIndex = commsSpeakerFilter?.SelectedIndex ?? 0;
        string? speaker = speakerIndex > 0 ? CommsAnalysis.Roles[speakerIndex - 1] : null;
        string targetScope = commsScopeFilter?.SelectedIndex switch { 1 => "Enemy", 2 => "Ally", 3 => "Unknown", 4 => "Team", _ => "" };
        foreach (var f in commsAnalysisReport?.Findings ?? [])
        {
            if (filter.Length > 0 && f.Category != filter) continue;
            if (targetScope.Length > 0 && f.Scope != targetScope) continue;
            if (filter.Length == 0 && targetScope == "Enemy" && !CommsAnalysis.IsSpellCall(f)) continue;
            if (speaker is not null && f.Role != speaker) continue;
            string at = f.GameSeconds is >= 0 ? CommsTime(f.GameSeconds) : "A " + CommsTime(f.AudioSeconds);
            int row = commsAnalysisCalls.Rows.Add(at, CommsRoleName(f.Role), CommsAnalysis.ScopeName(f.Scope), CommsAnalysis.TopicName(f.Category), f.Subject, f.Text);
            commsAnalysisCalls.Rows[row].Tag = f;
            foreach (DataGridViewCell cell in commsAnalysisCalls.Rows[row].Cells)
                cell.ToolTipText = (f.GameSeconds is >= 0 ? "Game time" : "Audio time (A)") + " · " + CommsAnalysis.TopicName(f.Category) + "\n" + f.Text + "\n\n" + f.Evidence;
        }
        commsAnalysisCalls.ClearSelection();
        if (commsCalloutSummary is not null)
        {
            string scope = speaker is not null ? CommsRoleName(speaker) + " · " : "All speakers · ";
            string type = filter.Length > 0 ? CommsAnalysis.TopicName(filter) : targetScope == "Enemy" ? "spell callouts" : "all callout types";
            if (targetScope.Length > 0) type = CommsAnalysis.ScopeName(targetScope) + " · " + type;
            string rule = filter switch { "Summoner" => CommsAnalysis.SummonerCountingRule,
                "Ultimate" => CommsAnalysis.UltimateCountingRule, "Jungle <15:00" => CommsAnalysis.JungleCountingRule,
                "Objective" => CommsAnalysis.ObjectiveCountingRule, "Roam" => CommsAnalysis.RoamCountingRule,
                _ => "Counts describe what was said. Double-click a row to play the callout in Overview." };
            string result = filter == "Jungle <15:00" && commsAnalysisReport?.AudioToGameSeconds is null
                ? "unavailable — game-clock timing is missing"
                : speaker is not null && CommsAnalysis.Roles.Take(5).Contains(speaker) &&
                    commsAnalysisReport?.Players.Any(p => p.Role == speaker && p.HasSpeakerAssignment) != true
                    ? "unavailable — no confident speaker assignment" : $"{commsAnalysisCalls.Rows.Count} detected";
            commsCalloutSummary.Text = $"{scope}{type}: {result}.\n{rule}";
        }
    }
    private void SeekToCommsFinding(DataGridView grid, int row)
    { if (row >= 0 && grid.Rows[row].Tag is CommsAnalysisFinding finding) SeekToAnalysisAudio(finding.AudioSeconds); }
    private void SeekToAnalysisAudio(double seconds)
    {
        if (commsAnalysisGame != GetSelectedRecording()?.Name || videoReview?.HasVideo != true) return;
        if (gameWorkspace is not null) gameWorkspace.SelectedIndex = 0;
        videoReview.SeekToComms(seconds);
    }
    private void ShowCommsAnalysisMethods()
    {
        var report = commsAnalysisReport ?? new CommsAnalysisReport();
        using var dialog = new Form { Text = "Comms analysis methods and data coverage", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(720, 480), MinimumSize = new Size(480, 320), BackColor = AppTheme.Surface, ForeColor = AppTheme.Text, Font = Font };
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = AppTheme.Surface, ForeColor = AppTheme.Text, BorderStyle = BorderStyle.None,
            Text = report.ShareMethod + "\r\n\r\n" + report.FightMethod + "\r\n\r\n" + report.ObjectiveMethod + "\r\n\r\n" + report.RoamMethod + "\r\n\r\n" + report.CalloutMethod + "\r\n\r\n" +
                string.Join("\r\n\r\n", report.Notes) + "\r\n\r\nRecovered overlapping speech contributes estimated time to its identified speakers. Callouts with unresolved overlap stay separate; consistent speakers can be attributed when non-overlap words anchor the sentence. Transcription and speaker-role errors can affect results." };
        dialog.Padding = new Padding(16);
        dialog.Controls.Add(text);
        dialog.ShowDialog(this);
    }
    private static string CommsRoleName(string role) => role switch { "adc" => "ADC", "unknown" => "Unassigned", "overlap" => "Overlap", _ => char.ToUpperInvariant(role[0]) + role[1..] };
    private static string CommsPercent(double? value) => value.HasValue ? $"{value:F1}%" : "—";
    private static string CommsTime(double? seconds) => seconds is double s && double.IsFinite(s) && s >= 0 ? $"{(int)(s / 60):00}:{s % 60:00.0}" : "—";
}
