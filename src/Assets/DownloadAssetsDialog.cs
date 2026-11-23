using System.Diagnostics;
namespace League_Data_Recorder;
internal sealed class DownloadAssetsDialog : Form
{
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Preparing downloads…" };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly Button retry = new() { Text = "Retry", AutoSize = true, Enabled = false };
    private readonly Button close = new() { Text = "Later", AutoSize = true };
    private readonly CancellationTokenSource cancellation = new();
    private readonly string[] ids;
    private bool running;
    internal bool Succeeded { get; private set; }
    internal DownloadAssetsDialog(string[] assetIds)
    {
        ids = assetIds;
        Text = "Downloads and component notices"; Width = 700; Height = 390; MinimumSize = new(580, 370);
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 24)); layout.RowStyles.Add(new(SizeType.Absolute, 42));
        long bytes = AssetDownloads.Catalog.Where(a => assetIds.Contains(a.Id) && !File.Exists(AssetPaths.Resolve(a.Path))).Sum(a => a.Size);
        string description = $"Missing components: {bytes / 1073741824d:0.00} GB to download. Existing local files are kept. You can stop and resume downloads.\n\n";
        description += assetIds.Contains("syncthing")
            ? "Syncthing is free and open source (MPL-2.0). Game data remains on the participating computers. Public discovery and encrypted relays help establish connections."
            : "Components download from their upstream publishers. FFmpeg uses an LGPL build. Analysis runs locally. audEERING’s emotion model is CC BY-NC-SA 4.0 for non-commercial use; commercial use needs a separate license. Whisper and Silero use MIT; NeMo diarization uses OpenMDW 1.1.";
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = description }, 0, 0);
        layout.Controls.Add(status, 0, 1); layout.Controls.Add(progress, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var notices = new Button { Text = "Licenses / notices", AutoSize = true };
        notices.Click += (_, _) => OpenNotices(); close.Click += (_, _) => Close(); retry.Click += async (_, _) => await RunAsync();
        actions.Controls.AddRange([close, retry, notices]); layout.Controls.Add(actions, 0, 3); Controls.Add(layout);
        AppTheme.Apply(this);
        Shown += async (_, _) => await RunAsync(); FormClosing += (_, _) => cancellation.Cancel();
    }
    internal static void OpenNotices()
    {
        using var viewer = new Form { Text = "Component licenses and notices", Width = 850, Height = 650, StartPosition = FormStartPosition.CenterParent };
        var files = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        var text = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = true, WordWrap = true };
        var paths = new List<string> { Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md") };
        string redistribution = Path.Combine(AppContext.BaseDirectory, "docs", "REDISTRIBUTION.md");
        if (File.Exists(redistribution)) paths.Add(redistribution);
        string noticeDirectory = Path.Combine(AppContext.BaseDirectory, "Notices");
        if (Directory.Exists(noticeDirectory)) paths.AddRange(Directory.EnumerateFiles(noticeDirectory)
            .Where(path => new[] { ".txt", ".md", ".rtf" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).Order());
        files.Items.AddRange(paths.Select(Path.GetFileName).Cast<object>().ToArray());
        files.SelectedIndexChanged += (_, _) =>
        {
            try
            {
                string path = paths[files.SelectedIndex];
                if (Path.GetExtension(path).Equals(".rtf", StringComparison.OrdinalIgnoreCase)) text.LoadFile(path);
                else text.Text = File.ReadAllText(path);
            }
            catch (IOException ex) { text.Text = "Notice could not be opened: " + ex.Message; }
        };
        text.LinkClicked += (_, e) =>
        {
            if (Uri.TryCreate(e.LinkText, UriKind.Absolute, out var url) && url.Scheme == "https")
                try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { }
        };
        viewer.Controls.Add(text); viewer.Controls.Add(files); AppTheme.Apply(viewer);
        files.SelectedIndex = 0; viewer.ShowDialog();
    }
    private async Task RunAsync()
    {
        if (running || cancellation.IsCancellationRequested) return;
        running = true; retry.Enabled = false;
        try
        {
            await AssetDownloads.EnsureAsync(ids, new Progress<DownloadProgress>(p =>
            {
                if (IsDisposed || Disposing) return;
                status.Text = p.Message + (p.Total > 0 ? $" · {p.Completed / 1048576d:0} / {p.Total / 1048576d:0} MB" : "");
                progress.Value = p.Total > 0 ? (int)Math.Clamp(p.Completed * 1000d / p.Total, 0, 1000) : 0;
            }), cancellation.Token);
            Succeeded = true;
            if (!IsDisposed) { status.Text = "Downloads complete. Ready to use."; progress.Value = 1000; close.Text = "Done"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) { status.Text = "Download stopped: " + ex.Message; retry.Enabled = true; close.Text = "Close"; } }
        finally { running = false; }
    }
}
