using System.Diagnostics;

namespace League_Data_Recorder;

public partial class Form1
{
    private readonly Dictionary<string, string> analysisChampionIconPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> analysisChampionDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> analysisChampionImages = new(StringComparer.OrdinalIgnoreCase);
    private Bitmap? analysisMissingChampionIcon;

    private void InitializeAnalysisChampionIcons()
    {
        // Reuse the recorder's existing versioned Data Dragon cache, including
        // when offline. Startup's existing refresh can supply a newer catalog.
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "League Data Recorder", "DataDragon");
        try
        {
            if (!Directory.Exists(cache)) return;
            var newest = Directory.EnumerateDirectories(cache)
                .Select(path => (Path: path, Version: Version.TryParse(Path.GetFileName(path), out var v) ? v : null))
                .Where(x => x.Version != null && File.Exists(Path.Combine(x.Path, "champion.json")) &&
                    Directory.Exists(Path.Combine(x.Path, "icons")))
                .OrderByDescending(x => x.Version).FirstOrDefault();
            if (newest.Path != null)
                SetAnalysisChampionIconSource(Path.Combine(newest.Path, "champion.json"), Path.Combine(newest.Path, "icons"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { Debug.WriteLine("Could not read cached analysis champion icons: " + ex.Message); }
    }

    private void SetAnalysisChampionIconSource(string catalog, string icons)
    {
        if (IsDisposed || Disposing) return;
        var champions = LoadChampionCatalog(catalog);
        analysisChampionIconPaths.Clear();
        analysisChampionDisplayNames.Clear();
        foreach (var (id, champion) in champions)
        {
            string path = Path.Combine(icons, champion.IconFileName);
            foreach (string name in new[] { champion.Name, champion.Id, "Champion " + id })
            {
                analysisChampionIconPaths[ChampionIconKey(name)] = path;
                analysisChampionDisplayNames[ChampionIconKey(name)] = champion.Name;
            }
        }
        if (analysisChampionsGrid?.DataSource != null) RenderAnalysisChampions();
        videoPortraits.Clear();
        videoReview?.RefreshFightPortraits();
        if (commsRoleContext is { } view)
            _ = UpdateVideoCommsAsync(view.GamePath, view.Transcript, view.Labels, commsRequestId);
    }

    private static string ChampionIconKey(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());

    private string AnalysisChampionDisplayName(string name) =>
        analysisChampionDisplayNames.GetValueOrDefault(ChampionIconKey(name), name);

    private Image GetAnalysisChampionIcon(string name)
    {
        if (analysisChampionIconPaths.TryGetValue(ChampionIconKey(name), out string? path))
        {
            if (analysisChampionImages.TryGetValue(path, out var cached)) return cached;
            try
            {
                if (File.Exists(path))
                {
                    using var original = Image.FromFile(path);
                    var icon = new Bitmap(original);
                    analysisChampionImages[path] = icon;
                    return icon;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
            { Debug.WriteLine("Could not load champion portrait: " + ex.Message); }
        }
        if (analysisMissingChampionIcon == null)
        {
            analysisMissingChampionIcon = new Bitmap(32, 32);
            using var graphics = Graphics.FromImage(analysisMissingChampionIcon);
            graphics.Clear(AppTheme.Raised);
            using var pen = new Pen(AppTheme.Muted, 1.5F);
            graphics.DrawEllipse(pen, 8, 8, 16, 16);
            graphics.DrawLine(pen, 12, 16, 20, 16);
        }
        return analysisMissingChampionIcon;
    }

    private void ConfigureAnalysisChampionCells()
    {
        analysisChampionsGrid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && analysisChampionsGrid.Columns[e.ColumnIndex].Name == "ChampionIcon")
                e.ToolTipText = analysisChampionsGrid.Rows[e.RowIndex].Cells["Name"].Value as string ?? "Unknown champion";
        };
        analysisChampionsGrid.DataBindingComplete += (_, _) =>
        {
            if (!analysisChampionsGrid.Columns.Contains("ChampionIcon")) return;
            foreach (DataGridViewRow row in analysisChampionsGrid.Rows)
            {
                string name = row.Cells["Name"].Value as string ?? "Unknown champion";
                row.Cells["ChampionIcon"].ToolTipText = name;
                row.Cells["ChampionIcon"].AccessibilityObject.Name = name;
            }
        };
    }

    private void DisposeAnalysisImages()
    {
        foreach (Image image in analysisChampionImages.Values) image.Dispose();
        analysisChampionImages.Clear();
        analysisMissingChampionIcon?.Dispose();
        analysisMissingChampionIcon = null;
    }
}
