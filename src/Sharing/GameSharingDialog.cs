
namespace League_Data_Recorder;

internal sealed class GameSharingDialog : Form
{
    private readonly SyncthingSharing sharing;
    private readonly Func<string?> selectedGame;
    private readonly Func<string> recordingsFolder;
    private readonly Func<Task> refreshHistory;
    private readonly Func<bool> chooseDestination;
    private readonly CancellationToken token;
    private readonly DataGridView shares = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = AppTheme.Surface,
        BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false
    };
    private readonly ListBox devices = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox link = new() { Dock = DockStyle.Fill, PlaceholderText = "Paste a game share link", MaxLength = 8192 };
    private readonly Label selection = Caption("");
    private readonly Label status = Caption("Keep both apps open while a game transfers.");
    private readonly Label details = Caption("Select a transfer to view its details.");
    private readonly DarkButton create = ActionButton("Create share link", 146);
    private readonly DarkButton receive = ActionButton("Receive game", 128);
    private readonly DarkButton copy = ActionButton("Copy link", 100);
    private readonly DarkButton approve = ActionButton("Approve recipient", 156);
    private readonly DarkButton stop = ActionButton("Stop sharing", 118);
    private readonly Panel approvals = new() { Dock = DockStyle.Fill, Visible = false, Padding = new(0, 8, 0, 0) };
    private readonly RowStyle approvalsRow = new(SizeType.Absolute, 0);
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    private bool rebinding;
    internal bool Busy { get; private set; }
    private GameShareEntry? Selected => shares.CurrentRow?.Tag as GameShareEntry;

    internal GameSharingDialog(SyncthingSharing engine, Func<string?> selection, Func<string> recordings,
        Func<bool> destination, Func<Task> refresh, CancellationToken ct)
    {
        sharing = engine; selectedGame = selection; recordingsFolder = recordings;
        refreshHistory = refresh; chooseDestination = destination; token = ct;
        Text = "Game sharing";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(880, 650); MinimumSize = new(760, 580);
        StartPosition = FormStartPosition.CenterParent; BackColor = AppTheme.Background; ForeColor = AppTheme.Text;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (float height in new[] { 38F, 144F, 36F }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 58));
        layout.RowStyles.Add(approvalsRow);
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        var title = Caption("Game sharing"); title.Font = new(Font.FontFamily, 16, FontStyle.Bold); title.ForeColor = AppTheme.Text;
        layout.Controls.Add(title, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        cards.ColumnStyles.Add(new(SizeType.Percent, 50)); cards.ColumnStyles.Add(new(SizeType.Percent, 50));
        var sendCard = Card("Send a game", out var sendContent); sendCard.Margin = new(0, 0, 8, 8);
        var receiveCard = Card("Receive a game", out var receiveContent); receiveCard.Margin = new(8, 0, 0, 8);
        sendContent.Controls.Add(this.selection, 0, 1);
        sendContent.Controls.Add(create, 0, 2);
        receiveContent.Controls.Add(link, 0, 1);
        receiveContent.Controls.Add(receive, 0, 2);
        cards.Controls.Add(sendCard, 0, 0); cards.Controls.Add(receiveCard, 1, 0); layout.Controls.Add(cards, 0, 1);

        var transferHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        transferHeading.ColumnStyles.Add(new(SizeType.Percent, 100));
        transferHeading.ColumnStyles.Add(new(SizeType.Absolute, 108)); transferHeading.ColumnStyles.Add(new(SizeType.Absolute, 126));
        var heading = Caption("Transfers"); heading.ForeColor = AppTheme.Text;
        transferHeading.Controls.Add(heading, 0, 0); transferHeading.Controls.Add(copy, 1, 0); transferHeading.Controls.Add(stop, 2, 0);
        layout.Controls.Add(transferHeading, 0, 2);
        foreach (var column in new[] { ("Game", 36F), ("Direction", 14F), ("Size", 12F), ("Status", 38F) })
            shares.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Item1, FillWeight = column.Item2,
                SortMode = DataGridViewColumnSortMode.NotSortable, MinimumWidth = 64 });
        shares.DefaultCellStyle.BackColor = AppTheme.Surface; shares.DefaultCellStyle.ForeColor = AppTheme.Text;
        shares.DefaultCellStyle.SelectionBackColor = AppTheme.Selection; shares.DefaultCellStyle.SelectionForeColor = AppTheme.Text;
        shares.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Raised; shares.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.Muted;
        shares.RowTemplate.Height = 32; shares.ColumnHeadersHeight = 30;
        shares.AccessibleName = "Sent and received games with transfer status";
        layout.Controls.Add(shares, 0, 3); layout.Controls.Add(details, 0, 4);

        var approvalLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        approvalLayout.ColumnStyles.Add(new(SizeType.Percent, 100)); approvalLayout.ColumnStyles.Add(new(SizeType.Absolute, 166));
        approvalLayout.RowStyles.Add(new(SizeType.Absolute, 26)); approvalLayout.RowStyles.Add(new(SizeType.Percent, 100));
        var approvalTitle = Caption("Recipients · compare the pairing code before approving");
        approvalLayout.Controls.Add(approvalTitle, 0, 0); approvalLayout.SetColumnSpan(approvalTitle, 2);
        approvalLayout.Controls.Add(devices, 0, 1); approvalLayout.Controls.Add(approve, 1, 1);
        approvals.Controls.Add(approvalLayout); layout.Controls.Add(approvals, 0, 5);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.Absolute, 88));
        var close = ActionButton("Close", 80); close.Click += (_, _) => Close();
        footer.Controls.Add(status, 0, 0); footer.Controls.Add(close, 1, 0); layout.Controls.Add(footer, 0, 6);
        Controls.Add(layout); CancelButton = close;

        create.Click += async (_, _) => await WorkAsync(async () =>
        {
            string source = selectedGame() ?? throw new InvalidOperationException("Select a saved game in Games. Wait for recording or transcription to finish before sharing it.");
            var entry = await sharing.CreateAsync(source, new Progress<string>(s => { if (!IsDisposed) status.Text = s; }), token);
            RebindShares(entry); Clipboard.SetText(entry.Link.Encode());
            status.Text = "Link copied. Your teammate can request access; approve their device below.";
        });
        receive.Click += async (_, _) => await ReceiveLinkAsync(link.Text);
        link.TextChanged += (_, _) => UpdateActions();
        copy.Click += async (_, _) => await WorkAsync(() =>
        {
            if (Selected is { Outbound: true, Stopped: false } e && e.Link.Expires > DateTimeOffset.UtcNow)
            { Clipboard.SetText(e.Link.Encode()); status.Text = "Share link copied."; }
            return Task.CompletedTask;
        });
        stop.Click += async (_, _) => await WorkAsync(async () =>
        {
            if (Selected is not { } e) return;
            await sharing.StopAsync(e, token); status.Text = "Transfer stopped. Saved games are kept."; RebindShares(e);
        });
        approve.Click += async (_, _) => await WorkAsync(async () =>
        {
            if (Selected is not { Outbound: true, Stopped: false } e || devices.SelectedItem is not PendingShareDevice device) return;
            string code = e.Link.PairCode(device.DeviceId);
            if (MessageBox.Show(this, $"Share {e.Link.Title} with this device?\n\nDevice: {device.DeviceId}\nPairing code: {code}\n\nConfirm this code with your teammate. Device names are unverified.",
                "Approve game download", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await sharing.ApproveAsync(e, device.DeviceId, token); RebindShares(e);
            status.Text = "Approved. The game will transfer automatically.";
        });
        shares.SelectionChanged += (_, _) => { if (!rebinding) UpdateActions(); };
        devices.SelectedIndexChanged += (_, _) => UpdateActions();
        FormClosing += (_, e) =>
        {
            if (Busy && !token.IsCancellationRequested)
            { e.Cancel = true; status.Text = "Wait for the current operation to finish, or close the app to cancel."; }
        };
        FormClosed += (_, _) => timer.Dispose();
        Shown += async (_, _) => { RebindShares(); await RefreshAsync(); timer.Start(); };
        timer.Tick += async (_, _) => { if (!Busy) await RefreshAsync(); };
        AppTheme.Apply(this); UpdateActions();
    }

    private static DarkButton ActionButton(string text, int width) => new()
    { Text = text, AccessibleName = text, Size = new(width, 32), Margin = new(0, 0, 8, 0), Anchor = AnchorStyles.Left };

    private static Label Caption(string text) => new()
    { Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = AppTheme.Muted, Margin = Padding.Empty };

    private static Panel Card(string title, out TableLayoutPanel content)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Padding = new(12) };
        content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.Absolute, 28)); content.RowStyles.Add(new(SizeType.Absolute, 34));
        content.RowStyles.Add(new(SizeType.Percent, 100));
        var heading = Caption(title); heading.ForeColor = AppTheme.Text;
        content.Controls.Add(heading, 0, 0); card.Controls.Add(content); return card;
    }

    private void UpdateActions()
    {
        if (IsDisposed || rebinding) return;
        var entry = Selected;
        bool outgoing = entry is { Outbound: true, Stopped: false } && entry.Link.Expires > DateTimeOffset.UtcNow;
        create.Enabled = !Busy && selectedGame() is not null;
        selection.Text = selectedGame() is { } path ? Path.GetFileName(path) : "Select a saved game in Games first.";
        receive.Enabled = !Busy && !string.IsNullOrWhiteSpace(link.Text);
        copy.Enabled = !Busy && outgoing;
        stop.Enabled = !Busy && entry is { Stopped: false };
        stop.Text = entry?.Outbound == false ? "Stop download" : "Stop sharing";
        approve.Enabled = !Busy && outgoing && devices.SelectedItem is PendingShareDevice d && !entry!.Recipients.Contains(d.DeviceId);
        bool showApprovals = outgoing && devices.Items.Count > 0;
        approvals.Visible = showApprovals; approvalsRow.Height = showApprovals ? 110 : 0;
        details.Text = entry is null ? "Share links expire after 7 days. Only a snapshot of the selected game is sent."
            : entry.Outbound ? $"{entry.Link.Title}\nLink expires {entry.Link.Expires.LocalDateTime:g} · {entry.Recipients.Count} approved recipient(s)"
            : $"{entry.Link.Title}\nPairing code: {entry.Link.PairCode(sharing.DeviceId)} · Ask the sender to approve this code.";
    }

    internal async Task ReceiveLinkAsync(string text)
    {
        while (Busy && !IsDisposed) await Task.Delay(100, token);
        if (IsDisposed) return;
        link.Text = text;
        await WorkAsync(async () =>
        {
            var invitation = GameShareLink.Parse(text);
            if (!chooseDestination()) return;
            if (MessageBox.Show(this, $"Receive {invitation.Title} ({invitation.Bytes / 1073741824d:0.00} GB)?\n\nThe sender must approve this computer. Files are verified before import. Accept game data only from someone you trust.",
                "Receive game", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            var entry = await sharing.ReceiveAsync(invitation, token); RebindShares(entry); link.Clear();
            status.Text = "Waiting for sender approval. Confirm the pairing code shown above.";
        });
    }

    private void RebindShares(GameShareEntry? selected = null)
    {
        if (IsDisposed) return;
        string? id = (selected ?? Selected)?.Link.FolderId;
        rebinding = true;
        try
        {
            shares.Rows.Clear();
            foreach (var entry in sharing.Entries)
            {
                int index = shares.Rows.Add(entry.Link.Title, entry.Outbound ? "Sending" : "Receiving",
                    $"{entry.Link.Bytes / 1073741824d:0.00} GB", entry.Status);
                shares.Rows[index].Tag = entry;
                if (entry.Link.FolderId == id) shares.CurrentCell = shares.Rows[index].Cells[0];
            }
        }
        finally { rebinding = false; }
        UpdateActions();
    }

    private async Task RefreshAsync() => await WorkAsync(async () =>
    {
        await sharing.StartAsync(token);
        var pending = (await sharing.PendingAsync(token)).ToList();
        foreach (string id in sharing.Entries.Where(e => e.Outbound).SelectMany(e => e.Recipients).Distinct())
            if (pending.All(p => p.DeviceId != id)) pending.Add(new(id, "Previously approved"));
        string? previous = (devices.SelectedItem as PendingShareDevice)?.DeviceId;
        devices.BeginUpdate(); devices.Items.Clear(); devices.Items.AddRange(pending.Cast<object>().ToArray());
        devices.SelectedItem = pending.FirstOrDefault(p => p.DeviceId == previous); devices.EndUpdate();
        int imported = await sharing.PollAsync(recordingsFolder(), token); RebindShares();
        if (imported > 0) { await refreshHistory(); status.Text = $"Received {imported} game(s). Open Games to review."; }
    });

    private async Task WorkAsync(Func<Task> action)
    {
        if (Busy) return;
        Busy = true; UpdateActions();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        finally { Busy = false; UpdateActions(); }
    }
}
