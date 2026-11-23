namespace League_Data_Recorder;

public partial class Form1
{
    private SyncthingSharing? gameSharing;
    private GameSharingDialog? gameSharingDialog;
    private readonly CancellationTokenSource backgroundServices = new();
    private System.Windows.Forms.Timer? sharePollTimer;
    private bool pollingShares;
    private DarkButton? gameSharingButton;
    private bool shuttingDownServices;
    private bool openingSharing;
    private string? startupShareLink;

    private void InitializeGameSharing()
    {
        var shareButton = WorkspaceButton("Sharing", 96);
        gameSharingButton = shareButton;
        shareButton.Dock = DockStyle.Fill;
        shareButton.Margin = new Padding(6, 0, 0, 0);
        var gamesHeading = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
        gamesHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gamesHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        AddWorkspaceControl(gamesHeading, historyListTitle, 0, 0);
        gamesHeading.Controls.Add(shareButton, 1, 0);
        historyListLayout.Controls.Add(gamesHeading, 0, 0);
        historyToolTip.SetToolTip(shareButton, "Send a game, receive a share link, or manage transfers.");
        shareButton.Click += async (_, _) => await OpenSharingAsync();
        var shareMenu = new ToolStripMenuItem("Share game link…");
        shareMenu.Click += async (_, _) => await OpenSharingAsync(); recordingActionsMenu.Items.Insert(0, shareMenu);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new(8, 4, 8, 4) };
        var downloads = WorkspaceButton("Downloads", 110);
        downloads.Click += async (_, _) => await DownloadComponentsAsync(); bar.Controls.Add(downloads);
        var notices = WorkspaceButton("Licenses and credits", 160);
        notices.Click += (_, _) => DownloadAssetsDialog.OpenNotices(); bar.Controls.Add(notices);
        historyToolTip.SetToolTip(notices, "Component licenses, model usage terms, and Riot Games attribution.");
        var riotNotice = new LinkLabel { Text = "Riot Games attribution", AutoSize = true, Margin = new Padding(12, 9, 0, 0),
            LinkColor = AppTheme.Accent, ActiveLinkColor = AppTheme.Text, VisitedLinkColor = AppTheme.Accent };
        riotNotice.LinkClicked += (_, _) => DownloadAssetsDialog.OpenNotices();
        bar.Controls.Add(riotNotice);
        devicesPageTab.Controls.Add(bar); bar.BringToFront();
        Shown += async (_, _) =>
        {
            try { ShareLinkActivation.Register(); } catch (Exception ex) { AppendToLog("Paste share links into Sharing: " + ex.Message); }
            if (AssetDownloads.MissingStartupAssets) await DownloadComponentsAsync();
            try
            {
                gameSharing ??= new SyncthingSharing();
                if (gameSharing.Entries.Any(e => !e.Stopped && e.ImportedTo is null)) await EnsureSharingAsync();
                if (startupShareLink is { } link) { startupShareLink = null; await OpenSharingAsync(link); }
            }
            catch (Exception ex) { AppendToLog("Sharing startup: " + ex.Message); }
            sharePollTimer = new System.Windows.Forms.Timer(components) { Interval = 10000 };
            sharePollTimer.Tick += async (_, _) => await PollSharesAsync(); sharePollTimer.Start();
        };
    }
    private async Task DownloadComponentsAsync()
    {
        using var dialog = new DownloadAssetsDialog(AssetDownloads.Catalog.Where(a => a.Id != "syncthing").Select(a => a.Id).ToArray());
        dialog.ShowDialog(this);
        await RefreshAccelerationStatusAsync();
    }
    private async Task EnsureSharingAsync()
    {
        if (!File.Exists(AssetPaths.Resolve("runtime/syncthing/syncthing.exe")))
        {
            using var dialog = new DownloadAssetsDialog(["syncthing"]); dialog.ShowDialog(this);
            if (!dialog.Succeeded) throw new InvalidOperationException("Syncthing was not installed. Retry from Sharing.");
        }
        gameSharing ??= new SyncthingSharing(); await gameSharing.StartAsync(backgroundServices.Token);
    }
    private string? ShareableSelection() => !recordingSession && !startingSession && !stoppingSession && !savingSession &&
        transcribingGamePath is null && !deletingRecording && !editingSpeakerRoles ? GetSelectedRecording()?.Tag as string : null;
    private async Task OpenSharingAsync(string? link = null)
    {
        if (shuttingDownServices || openingSharing) return;
        openingSharing = true;
        try
        {
            await EnsureSharingAsync();
            if (gameSharingDialog is null || gameSharingDialog.IsDisposed)
                gameSharingDialog = new GameSharingDialog(gameSharing!, ShareableSelection, SharingRecordingsRoot, ChooseShareDestination,
                    () => RefreshRecordingsListAsync(SharingRecordingsRoot()), backgroundServices.Token);
            if (!gameSharingDialog.Visible) gameSharingDialog.Show(this);
            gameSharingDialog.BringToFront();
            if (link is not null) await gameSharingDialog.ReceiveLinkAsync(link);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Game sharing"); }
        finally { openingSharing = false; }
    }
    internal void ReceiveShareLink(string link)
    {
        if (!IsHandleCreated || !Visible) { startupShareLink = link; return; }
        BeginInvoke(new Action(async () => { if (!shuttingDownServices) { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); await OpenSharingAsync(link); } }));
    }
    private string SharingRecordingsRoot() => string.IsNullOrWhiteSpace(folderTextBox.Text) ? "" : GetRecordingDir("Recordings");
    private bool ChooseShareDestination()
    {
        if (!string.IsNullOrWhiteSpace(folderTextBox.Text)) return true;
        using var dialog = new FolderBrowserDialog { Description = "Choose a folder for received game data. Games will be stored in its Recordings subfolder." };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        folderTextBox.Text = dialog.SelectedPath; SaveAppPreferences(); return true;
    }
    private async Task PollSharesAsync()
    {
        if (pollingShares || shuttingDownServices || gameSharing is null || gameSharingDialog is { IsDisposed: false } || recordingSession || savingSession) return;
        pollingShares = true;
        try
        {
            if (!gameSharing.Running)
            {
                if (!gameSharing.Entries.Any(e => !e.Stopped && e.ImportedTo is null)) return;
                await gameSharing.StartAsync(backgroundServices.Token);
            }
            int pending = (await gameSharing.PendingAsync(backgroundServices.Token)).Length;
            if (gameSharingButton is not null)
            {
                gameSharingButton.Text = pending > 0 ? $"Sharing · {pending}" : "Sharing";
                gameSharingButton.AccessibleName = pending > 0 ? $"Game sharing, {pending} devices awaiting approval" : "Game sharing";
            }
            int imported = await gameSharing.PollAsync(SharingRecordingsRoot(), backgroundServices.Token);
            if (imported > 0 && !IsDisposed) { AppendToLog($"Imported {imported} shared game(s)."); await RefreshRecordingsListAsync(SharingRecordingsRoot()); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) AppendToLog("Sharing: " + ex.Message); }
        finally { pollingShares = false; }
    }
    private async Task StopBackgroundServicesAsync()
    {
        shuttingDownServices = true; sharePollTimer?.Stop(); backgroundServices.Cancel();
        if (gameSharing is not null) await gameSharing.DisposeAsync();
    }
}
