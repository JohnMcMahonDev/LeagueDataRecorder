using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LeagueTelemetry;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windows.ApplicationModel;
using Windows.Gaming.Input;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace League_Data_Recorder;

#region History workspace

public partial class Form1
{
    private void InitializeRuntimeAppearance()
    {
        tabControl1.UseRuntimeNavigation();
        recordingActionsMenu.Renderer = new DarkMenuRenderer();
        recordingActionsMenu.ShowImageMargin = false;
        AppTheme.StyleToolTip(historyToolTip);
        UpdateNavigation();
    }

    private void recordingNavButton_Click(object? sender, EventArgs e) =>
        tabControl1.SelectedTab = recordingPageTab;

    private void historyNavButton_Click(object? sender, EventArgs e) =>
        tabControl1.SelectedTab = historyPageTab;

    private void devicesNavButton_Click(object? sender, EventArgs e) =>
        tabControl1.SelectedTab = devicesPageTab;

    private void tabControl1_SelectedIndexChanged(object? sender, EventArgs e)
    {
        videoReview?.Pause();
        UpdateNavigation();
    }

    private void moreActionsButton_Click(object? sender, EventArgs e) =>
        recordingActionsMenu.Show(moreActionsButton, new Point(0, moreActionsButton.Height));

    private void Form1_HandleCreated(object? sender, EventArgs e)
    {
        if (!IsDesignerHosted) AppTheme.ApplyNative(this);
    }

    private void UpdateNavigation()
    {
        foreach (
            var (button, page) in new[]
            {
                (recordingNavButton, recordingPageTab),
                (historyNavButton, historyPageTab),
                (analysisNavButton, analysisPageTab),
                (devicesNavButton, devicesPageTab),
            }
        )
        {
            bool selected = tabControl1.SelectedTab == page;
            button.BackColor = selected ? AppTheme.Selection : AppTheme.Background;
            button.ForeColor = selected ? AppTheme.Text : AppTheme.Muted;
            button.FlatAppearance.BorderSize = 0;
            if (button is DarkButton darkButton) darkButton.NavigationSelected = selected;
        }
    }

    #region Recordings and transcript

    private async Task RefreshRecordingsListAsync(string recordingsFolder)
    {
        var recordings = new List<(ListViewItem Item, DateTimeOffset Date)>();
        ListViewItem[] items;
        string? previousGame = GetSelectedRecording()?.Tag as string;
        historyListStatus.Text = "Loading recordings…";

        historyScrollView.BeginUpdate();
        try
        {
            historyScrollView.Items.Clear();
            recordingThumbnails.Images.Clear();

            if (!Directory.Exists(recordingsFolder))
            {
                historyListStatus.Text = string.IsNullOrWhiteSpace(recordingsFolder)
                    ? "Choose a folder on the Recording tab."
                    : "No recordings found.";
                return;
            }

            foreach (
                string gameFolder in Directory.EnumerateDirectories(recordingsFolder, "Game_*")
            )
            {
                string[] parts = Path.GetFileName(gameFolder).Split('_', 3);
                if (parts.Length != 3 || !long.TryParse(parts[1], out long gameId) || gameId <= 0)
                    continue;

                string gamePath = Path.Combine(gameFolder, $"Game_{gameId}");
                if (
                    !File.Exists($"{gamePath}_ParsedGameData.json")
                    || !File.Exists($"{gamePath}_ParsedPostGameData.json")
                )
                {
                    continue;
                }

                recordings.Add(
                    CreateRecordingTile(gameFolder, $"{gamePath}_DraftData.json", gamePath)
                );
            }

            items = recordings
                .OrderByDescending(recording => recording.Date)
                .Select(recording => recording.Item)
                .ToArray();
            historyScrollView.Items.AddRange(items);
            historyListStatus.Text = items.Length switch
            {
                0 => "No recordings found.",
                1 => "1 recording",
                _ => $"{items.Length} recordings",
            };

            ListViewItem? previousItem = items.FirstOrDefault(item =>
                string.Equals(item.Tag as string, previousGame, StringComparison.OrdinalIgnoreCase)
            );
            if (previousItem != null)
            {
                previousItem.Selected = true;
                previousItem.EnsureVisible();
            }
        }
        finally
        {
            historyScrollView.EndUpdate();
        }

        foreach (ListViewItem item in items)
        {
            string gamePath = item.Name;
            if (IsDisposed || Disposing || item.ListView != historyScrollView)
                return;

            try
            {
                using Image? thumbnail = await LoadRecordingThumbnailAsync(
                    gamePath,
                    (uint)recordingThumbnails.ImageSize.Width
                );
                // Another refresh may have replaced these rows while awaiting the video preview.
                if (IsDisposed || Disposing || item.ListView != historyScrollView)
                    return;

                if (thumbnail == null)
                    continue;

                // Create the handle before adding images so ImageList copies each bitmap.
                _ = recordingThumbnails.Handle;
                int imageIndex = recordingThumbnails.Images.Count;
                recordingThumbnails.Images.Add(thumbnail);
                item.ImageIndex = imageIndex;
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing || item.ListView != historyScrollView)
                    return;

                AppendToLog(
                    $"Could not load thumbnail for {Path.GetFileName(gamePath)}: {ex.Message}"
                );
            }
        }
    }

    private (ListViewItem Item, DateTimeOffset Date) CreateRecordingTile(
        string gameFolder,
        string draftPath,
        string gamePath
    )
    {
        DateTimeOffset gameDate = File.GetLastWriteTimeUtc(draftPath);
        try
        {
            var draft = JsonConvert.DeserializeAnonymousType(
                File.ReadAllText(draftPath),
                new { endTime = (DateTimeOffset?)null }
            );
            if (
                draft?.endTime is DateTimeOffset draftEndedAt
                && draftEndedAt.Year > 1
                && draftEndedAt.Year < 9999
            )
            {
                gameDate = draftEndedAt;
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            AppendToLog(
                $"Could not read draft date for {Path.GetFileName(gamePath)}: {ex.Message}"
            );
        }

        string lengthText = "Duration unavailable";
        try
        {
            var timing = JsonConvert.DeserializeAnonymousType(
                File.ReadAllText($"{gamePath}_MediaTiming.json"),
                new { GameDurationSeconds = (double?)null }
            );

            if (
                timing?.GameDurationSeconds is double seconds
                && double.IsFinite(seconds)
                && seconds > 0
                && seconds < TimeSpan.MaxValue.TotalSeconds
            )
            {
                TimeSpan duration = TimeSpan.FromSeconds(seconds);
                lengthText = $"{(long)duration.TotalMinutes}:{duration.Seconds:00}";
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            AppendToLog(
                $"Could not read game length for {Path.GetFileName(gamePath)}: {ex.Message}"
            );
        }

        string dateText = gameDate.ToLocalTime().ToString("MMM d, yyyy  h:mm tt");
        string teamsText = "";
        try
        {
            var names = ReadRecordingTeamNames(gamePath);
            if (names.Ally.Length > 0 && names.Enemy.Length > 0) teamsText = names.Ally + " vs " + names.Enemy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { AppendToLog("Could not read recording team names: " + ex.Message); }
        var item = new ListViewItem(new[] { dateText, lengthText, teamsText }, -1)
        {
            Tag = gameFolder,
            Name = gamePath,
            ToolTipText = $"{dateText} · {lengthText}" + (teamsText.Length > 0 ? " · " + teamsText : ""),
        };
        return (item, gameDate);
    }

    private async Task<Image?> LoadRecordingThumbnailAsync(string gamePath, uint requestedWidth)
    {
        string thumbnailPath = Path.Combine(Path.GetDirectoryName(gamePath)!, "thumbnail.jpg");
        if (File.Exists(thumbnailPath))
        {
            try
            {
                using Image savedThumbnail = Image.FromFile(thumbnailPath);
                return new Bitmap(savedThumbnail);
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or UnauthorizedAccessException
                            or ArgumentException
                            or OutOfMemoryException
                            or System.Runtime.InteropServices.ExternalException
                )
            {
                AppendToLog($"Could not open {thumbnailPath}: {ex.Message}");
            }
        }

        string videoPath = $"{gamePath}_Video.mp4";
        if (!File.Exists(videoPath))
            return null;

        StorageFile video = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(videoPath));
        using StorageItemThumbnail? preview = await video.GetThumbnailAsync(
            ThumbnailMode.SingleItem,
            requestedWidth
        );
        if (preview == null || preview.Type != ThumbnailType.Image || preview.Size == 0)
            return null;

        using var reader = new DataReader(preview.GetInputStreamAt(0));
        uint size = checked((uint)preview.Size);
        if (await reader.LoadAsync(size) != size)
            return null;

        var bytes = new byte[checked((int)size)];
        reader.ReadBytes(bytes);
        using var stream = new MemoryStream(bytes);
        using Image image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private ListViewItem? GetSelectedRecording()
    {
        return historyScrollView.SelectedItems.Count > 0
            ? historyScrollView.SelectedItems[0]
            : null;
    }

    private void UpdateHistoryActions()
    {
        bool hasSelection = GetSelectedRecording() != null;
        SetPrimaryActionEnabled(
            transcribeButton,
            hasSelection
                && transcribingGamePath == null
                && !testingAcceleration
                && !checkingAcceleration
                && !deletingRecording
                && !editingSpeakerRoles
        );
        openReplayButton.Enabled =
            hasSelection && !openingReplay && !deletingRecording && !editingSpeakerRoles;
        openFolderMenuItem.Enabled = hasSelection && !deletingRecording && !editingSpeakerRoles;
        deleteGameMenuItem.Enabled = hasSelection && CanDeleteRecording();
        moreActionsButton.Enabled = hasSelection && !deletingRecording && !editingSpeakerRoles;
        if (editTeamNamesMenuItem is not null)
            editTeamNamesMenuItem.Enabled = hasSelection && !deletingRecording && !copyingGameData;
        UpdateSpeakerRoleEditingAction();
        UpdateAccelerationActions();
        UpdateWorkspaceHistoryActions();
    }

    private bool CanDeleteRecording() =>
        !deletingRecording
        && commsAnalysisCts == null
        && !copyingGameData
        && !editingSpeakerRoles
        && !openingReplay
        && transcribingGamePath == null
        && !startingSession
        && !recordingSession
        && !stoppingSession
        && !savingSession
        && currentSessionData == null;

    private void openFileLocationButton_Click(object? sender, EventArgs e)
    {
        if (deletingRecording || GetSelectedRecording()?.Tag is not string folder)
            return;
        try
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("The recording folder could not be found.");
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add(Path.GetFullPath(folder));
            Process.Start(start);
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or System.ComponentModel.Win32Exception
            )
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Could not open file location",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private async void deleteGameButton_Click(object? sender, EventArgs e)
    {
        if (!CanDeleteRecording() || GetSelectedRecording() is not ListViewItem selected)
            return;
        string gamePath = selected.Name;
        int selectedIndex = selected.Index;
        try
        {
            var plan = RecordingHistoryFiles.GetDeletePlan(gamePath);
            DialogResult answer = MessageBox.Show(
                this,
                $"Delete {Path.GetFileName(gamePath).Replace('_', ' ')} and all of its saved files?\n\n"
                    + "This removes its game data, audio, video, saved replay, transcripts, speaker roles, and game-specific analysis.\n\n"
                    + $"Recording folder: {plan.RecordingFolder}\n\nThis cannot be undone.",
                "Delete recording?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2
            );
            if (answer != DialogResult.Yes || !CanDeleteRecording())
                return;

            deletingRecording = true;
            historyScrollView.Enabled = false;
            UpdateFormControls();
            ++previewRequestId;
            ++commsRequestId;
            DisposeHistoryImages();
            videoReviewWindow?.Close();
            SetReviewMessage("Deleting recording…");
            await Task.Run(() => RecordingHistoryFiles.DeleteGame(gamePath));
            if (IsDisposed || Disposing)
                return;
            AppendToLog($"Deleted recording {Path.GetFileName(gamePath)}.");
            SetTranscriptionStatus("Recording deleted.", SuccessText);
            await RefreshRecordingsListAsync(plan.RecordingsRoot);
            if (!IsDisposed && !Disposing && historyScrollView.Items.Count > 0)
                historyScrollView
                    .Items[Math.Min(selectedIndex, historyScrollView.Items.Count - 1)]
                    .Selected = true;
            else if (!IsDisposed && !Disposing)
            {
                gameTitleLabel.Text = "Select a recording";
                gameMetadataLabel.Text = "";
                SetReviewMessage("Select a recording to play its video.");
                await RefreshCommsAsync(null);
                RefreshSelectedDraftIcons();
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!IsDisposed && !Disposing)
            {
                AppendToLog($"Could not delete {Path.GetFileName(gamePath)}: {ex.Message}");
                MessageBox.Show(
                    this,
                    $"Could not delete all of this game's files.\n\n{ex.Message}",
                    "Could not delete recording",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                string? recordings = Directory
                    .GetParent(Path.GetDirectoryName(gamePath)!)
                    ?.FullName;
                if (recordings != null)
                    await RefreshRecordingsListAsync(recordings);
            }
        }
        finally
        {
            deletingRecording = false;
            if (!IsDisposed && !Disposing)
            {
                historyScrollView.Enabled = true;
                UpdateFormControls();
            }
        }
    }

    private async void historyScrollView_SelectedIndexChanged(object? sender, EventArgs e)
    {
        videoReview?.Pause();
        int requestId = ++previewRequestId;
        ListViewItem? selected = GetSelectedRecording();
        UpdateHistoryActions();
        SetReviewMessage(selected == null ? "Select a recording to play its video." : "Loading video…");

        gameTitleLabel.Text =
            selected == null
                ? "Select a recording"
                : Path.GetFileName(selected.Name).Replace('_', ' ');
        gameMetadataLabel.Text =
            selected == null ? "" : $"{selected.Text}  ·  {selected.SubItems[1].Text}";
        UpdateSelectedTeamNames();

        // Start the media load first so a fast transcript read belongs to the new game.
        Task playback = selected is null || videoReview is null ? Task.CompletedTask : videoReview.LoadRecordingAsync(selected.Name);
        _ = RefreshCommsAsync(selected);
        _ = RefreshMatchHistoryAsync(selected?.Name, requestId, playback);
        RefreshSelectedDraftIcons();
        if (selected == null)
            return;

        try
        {
            await playback;
        }
        catch (Exception ex)
        {
            if (requestId == previewRequestId && !IsDisposed && !Disposing)
            {
                SetReviewMessage("Could not open this video.");
                AppendToLog($"Could not open video for {gameTitleLabel.Text}: {ex.Message}");
            }
        }
    }

    private async Task RefreshCommsAsync(ListViewItem? selected)
    {
        int requestId = ++commsRequestId;
        commsLoading?.SetBusy(selected is not null, "Loading comms…");
        ClearCommsShares();
        commsRowStarts.Clear();
        if (selected is not null) videoReview?.SetComms(selected.Name, [], "Loading comms…");
        commsRoleContext = null;
        UpdateSpeakerRoleEditingAction();
        commsGrid.Visible = false;
        commsGrid.Rows.Clear();
        commsStatusLabel.Visible = true;
        commsStatusLabel.ForeColor = MutedText;

        if (selected == null)
        {
            commsStatusLabel.Text = "Select a recording to view its comms.";
            return;
        }

        string gamePath = selected.Name;
        if (string.Equals(transcribingGamePath, gamePath, StringComparison.OrdinalIgnoreCase))
        {
            commsLoading?.SetBusy(true, "Transcribing comms…");
            commsStatusLabel.Text = "Transcribing comms…";
            videoReview?.SetComms(gamePath, [], "Transcribing comms…");
            return;
        }

        commsStatusLabel.Text = "Loading comms…";
        try
        {
            TranscriptionResult? transcript = await ReadTranscriptAsync(gamePath);
            if (requestId != commsRequestId || IsDisposed || Disposing)
                return;

            IReadOnlyDictionary<string, string>? roles = null;
            if (transcript != null && !string.IsNullOrWhiteSpace(transcript.JsonPath))
                roles = await TranscribeAudio.ReadSpeakerRoleLabelsAsync(transcript.JsonPath);
            if (requestId != commsRequestId || IsDisposed || Disposing)
                return;
            if (transcript != null && !string.IsNullOrWhiteSpace(transcript.JsonPath))
                commsRoleContext = new CommsRoleContext(
                    gamePath,
                    transcript,
                    roles ?? new Dictionary<string, string>()
                );
            UpdateSpeakerRoleEditingAction();
            List<(string Time, string Speaker, string Text, string? SpeakerId)> entries =
                BuildCommsEntries(transcript, roles);
            await UpdateVideoCommsAsync(gamePath, transcript, roles, requestId);
            if (requestId != commsRequestId || IsDisposed || Disposing) return;
            commsRowStarts = transcript is null ? new List<double>() :
                TranscriptPostProcessor.GroupForDisplay(transcript.Words ?? []).Select(u => u.Start).ToList();
            if (entries.Count == 0)
            {
                commsStatusLabel.Text =
                    transcript == null
                        ? "No transcript yet. Select Transcribe to generate one."
                        : "No speech was found in this transcript.";
                return;
            }

            var rows = new DataGridViewRow[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var (time, speaker, text, speakerId) = entries[i];
                rows[i] = new DataGridViewRow();
                rows[i].CreateCells(commsGrid, time, speaker, text);
                rows[i].Tag = speakerId;
                rows[i].Cells[commsTimeColumn.Index].ToolTipText = i < commsRowStarts.Count
                    ? "Click to jump to this comm in the video." : "No timestamp is available for this line.";
                rows[i].Cells[commsSpeakerColumn.Index].ToolTipText =
                    TranscribeAudio.IsEditableSpeaker(speakerId)
                        ? $"{speakerId} · Click to edit speaker roles for this game."
                        : speaker.Contains("overlap", StringComparison.OrdinalIgnoreCase) || speaker.StartsWith("Multiple speakers", StringComparison.Ordinal)
                            ? "Simultaneous speakers detected. Names come from speaker timings; words cannot be attributed to either voice. Use Edit speaker roles to correct either role."
                            : "This speech has no stable speaker ID to edit.";
            }

            commsGrid.Rows.AddRange(rows);
            // Keep paired labels readable in the existing compact role column.
            commsSpeakerColumn.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            commsStatusLabel.Visible = false;
            commsGrid.Visible = true;
            commsGrid.ClearSelection();
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            )
        {
            if (requestId != commsRequestId || IsDisposed || Disposing)
                return;

            commsStatusLabel.Text = "Could not open this transcript.";
            videoReview?.SetComms(gamePath, [], "Could not open this transcript.");
            commsStatusLabel.ForeColor = ErrorText;
            AppendToLog($"Could not read comms for {Path.GetFileName(gamePath)}: {ex.Message}");
        }
        finally
        {
            if (!IsDisposed && !Disposing && requestId == commsRequestId) commsLoading?.SetBusy(false);
        }
    }

    private static string GetTranscriptOutputPath(string gamePath)
    {
        // Game files live under <root>/Recordings/<game folder>.
        var gameFolder = new DirectoryInfo(Path.GetDirectoryName(gamePath)!);
        return Path.Combine(
            gameFolder.Parent!.Parent!.FullName,
            "Transcriptions",
            gameFolder.Name,
            $"{Path.GetFileName(gamePath)}_transcript.json"
        );
    }

    private static async Task<TranscriptionResult?> ReadTranscriptAsync(string gamePath)
    {
        string? path = await TranscribeAudio
            .FindExistingTranscriptAsync(gamePath, GetTranscriptOutputPath(gamePath))
            .ConfigureAwait(false);
        if (path == null)
            return null;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            var transcript =
                await System
                    .Text.Json.JsonSerializer.DeserializeAsync<TranscriptionResult>(stream)
                    .ConfigureAwait(false)
                ?? throw new System.Text.Json.JsonException("Transcript JSON is empty.");
            transcript.JsonPath = path;
            await Task.Run(() => TranscribeAudio.PrepareOverlapDisplay(transcript, gamePath))
                .ConfigureAwait(false);
            return transcript;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static List<(
        string Time,
        string Speaker,
        string Text,
        string? SpeakerId
    )> BuildCommsEntries(
        TranscriptionResult? transcript,
        IReadOnlyDictionary<string, string>? roles = null
    )
    {
        var entries = new List<(string Time, string Speaker, string Text, string? SpeakerId)>();
        if (transcript == null)
            return entries;
        // Display coherent utterances rather than a new row for each uncertain word.
        // Mixed IDs remain explicit; this does not rewrite the diarization or role data.
        foreach (var utterance in TranscriptPostProcessor.GroupForDisplay(transcript.Words ?? []))
        {
            string time =
                $"{Math.Floor(utterance.Start / 60):00}:{Math.Floor(utterance.Start % 60):00}";
            string label;
            if (utterance.OverlappingSpeech)
                label = TranscriptOverlap.Label(utterance.Speakers, roles);
            else if (utterance.Speaker is not null)
                label = TranscribeAudio.FormatSpeakerLabel(utterance.Speaker, roles);
            else
            {
                var alternatives = utterance
                    .Speakers.Select(s => TranscribeAudio.FormatSpeakerLabel(s, roles))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                label =
                    alternatives.Length == 0
                        ? "Unknown"
                        : string.Join(" / ", alternatives) + " (uncertain)";
            }
            entries.Add((time, label, utterance.Text, utterance.Speaker));
        }
        if (entries.Count == 0 && !string.IsNullOrWhiteSpace(transcript.Text))
            foreach (
                var chunk in transcript
                    .Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Chunk(30)
            )
                entries.Add(("—", "Unknown", string.Join(" ", chunk), null));
        return entries;
    }

    private void SetReviewMessage(string message)
    {
        videoReview?.Clear(message);
        previewPlaceholder.Text = message;
        previewPlaceholder.Visible = videoReview is null;
    }

    private (string Key, PictureBox[] Boxes)[] GetDraftIconRows() =>
        new[]
        {
            (
                "allyPicks",
                new[] { b1ChampIcon, b2ChampIcon, b3ChampIcon, b4ChampIcon, b5ChampIcon }
            ),
            (
                "enemyPicks",
                new[] { r1ChampIcon, r2ChampIcon, r3ChampIcon, r4ChampIcon, r5ChampIcon }
            ),
        };

    private void DisposeHistoryImages()
    {
        if (thumbnailPictureBox != null)
        {
            Image? previous = thumbnailPictureBox.Image;
            thumbnailPictureBox.Image = null;
            previous?.Dispose();
        }

        foreach (var (_, boxes) in GetDraftIconRows())
        {
            foreach (PictureBox box in boxes)
            {
                if (box == null)
                    continue;

                Image? previous = box.Image;
                box.Image = null;
                previous?.Dispose();
            }
        }
    }

    private void RefreshSelectedDraftIcons()
    {
        try
        {
            UpdateSelectedDraftIcons();
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or System.Text.Json.JsonException
                        or ArgumentException
                        or FormatException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or System.Runtime.InteropServices.ExternalException
            )
        {
            AppendToLog($"Could not display draft picks: {ex.Message}");
        }
    }

    private void UpdateSelectedDraftIcons()
    {
        var teams = GetDraftIconRows();

        foreach (var team in teams)
        {
            foreach (PictureBox box in team.Boxes)
            {
                Image? previous = box.Image;
                box.Image = null;
                previous?.Dispose();
                box.AccessibleDescription = null;
                historyToolTip.SetToolTip(box, null);
            }
        }

        if (GetSelectedRecording() is not { } selected || string.IsNullOrWhiteSpace(selected.Name))
            return;
        // Use the same exact game prefix as transcription and recovery. A
        // folder can contain several drafts; its first match may be another game.
        string draftPath = selected.Name + "_DraftData.json";
        if (!File.Exists(draftPath))
        {
            Debug.WriteLine($"No draft JSON found: {draftPath}");
            return;
        }

        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "League Data Recorder",
            "DataDragon"
        );

        string? cacheFolder = null;
        Version? latestVersion = null;

        if (Directory.Exists(cacheRoot))
        {
            foreach (string folder in Directory.EnumerateDirectories(cacheRoot))
            {
                if (
                    Version.TryParse(Path.GetFileName(folder), out var version)
                    && (latestVersion == null || version > latestVersion)
                    && File.Exists(Path.Combine(folder, "champion.json"))
                )
                {
                    latestVersion = version;
                    cacheFolder = folder;
                }
            }
        }

        var champions = cacheFolder is null ? new Dictionary<int, ChampionInfo>() :
            LoadChampionCatalog(Path.Combine(cacheFolder, "champion.json"));
        string[] iconFolders = Directory.Exists(cacheRoot)
            ? Directory.EnumerateDirectories(cacheRoot)
                .Select(path => (Path: path, Version: Version.TryParse(Path.GetFileName(path), out var v) ? v : null))
                .Where(entry => entry.Version is not null)
                .OrderByDescending(entry => entry.Version)
                .Select(entry => Path.Combine(entry.Path, "icons")).ToArray()
            : [];

        using var draft = JsonDocument.Parse(File.ReadAllText(draftPath));

        foreach (var team in teams)
        {
            if (
                !draft.RootElement.TryGetProperty(team.Key, out var picks)
                || picks.ValueKind != JsonValueKind.Array
            )
                continue;

            int count = Math.Min(team.Boxes.Length, picks.GetArrayLength());

            for (int i = 0; i < count; i++)
            {
                JsonElement pick = picks[i];

                if (
                    pick.ValueKind != JsonValueKind.Number
                    || !pick.TryGetInt32(out int championId)
                    || championId <= 0
                )
                    continue;
                champions.TryGetValue(championId, out var champion);
                string label = champion?.Name ?? AnalysisChampionDisplayName("Champion " + championId);
                string? iconPath = champion is null ? null : iconFolders
                    .Select(folder => Path.Combine(folder, champion.IconFileName)).FirstOrDefault(File.Exists);
                PictureBox box = team.Boxes[i];
                if (iconPath is not null)
                {
                    using var image = Image.FromFile(iconPath);
                    box.Image = new Bitmap(image);
                }
                else
                {
                    // Recovery succeeded even if the local image cache is
                    // incomplete. Show an identified slot rather than a blank.
                    var placeholder = new Bitmap(64, 64);
                    using var graphics = Graphics.FromImage(placeholder);
                    graphics.Clear(AppTheme.Raised);
                    using var font = new Font(Font.FontFamily, 11, FontStyle.Bold);
                    TextRenderer.DrawText(graphics, champion?.Id is { Length: > 0 } key ? key[..Math.Min(4, key.Length)] : championId.ToString(),
                        font, new Rectangle(0, 0, 64, 64), AppTheme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    box.Image = placeholder;
                    Debug.WriteLine($"Draft champion {championId} recovered; no cached portrait found.");
                }
                box.AccessibleDescription = label;
                historyToolTip.SetToolTip(box, label + (iconPath is null ? " (portrait unavailable locally)" : ""));
            }
        }
    }

    private async void tabControl1_Selected(object? sender, TabControlEventArgs e)
    {
        if (e.TabPage != historyPageTab)
            return;

        try
        {
            string recordingsFolder = string.IsNullOrWhiteSpace(folderTextBox.Text)
                ? ""
                : GetRecordingDir("Recordings");
            await RefreshRecordingsListAsync(recordingsFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            historyListStatus.Text = "Could not load recordings.";
            AppendToLog($"Could not refresh recordings: {ex.Message}");
        }
    }

    private void historyScrollView_ClientSizeChanged(object? sender, EventArgs e)
    {
        int width =
            historyScrollView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6;
        Size tileSize = historyScrollView.TileSize;

        if (width > 0 && width != tileSize.Width)
        {
            historyScrollView.TileSize = new Size(width, tileSize.Height);
        }
    }

    private async void transcribeButton_Click(object? sender, EventArgs e)
    {
        ListViewItem? selected = GetSelectedRecording();
        if (
            deletingRecording
            || editingSpeakerRoles
            || testingAcceleration
            || checkingAcceleration
            || transcribingGamePath != null
            || selected?.Tag is not string
        )
            return;

        string gamePath = selected.Name;
        transcribingGamePath = gamePath;
        UpdateHistoryActions();
        string match = Path.GetFileName(gamePath).Replace('_', ' ');

        try
        {
            SetTranscriptionStatus($"{match} · Checking transcript…", MutedText);
            transcriptionProgressBar.Visible = true;
            transcriptionProgressBar.MarqueeAnimationSpeed = 28;
            _ = RefreshCommsAsync(selected);

            IProgress<string> status = new Progress<string>(message =>
            {
                if (transcribingGamePath == gamePath && !IsDisposed && !Disposing)
                {
                    SetTranscriptionStatus($"{match} · {message}", MutedText);
                    AppendToLog($"{match}: {message}");
                }
            });

            string transcriptOutputPath = GetTranscriptOutputPath(gamePath);
            var transcriptionOptions = CreateUiTranscriptionOptions(gamePath);
            SetTranscriptionStatus($"{match} · Checking saved enemy picks…", MutedText);
            await Task.Run(() => RecordedDraftRecovery.TryRecoverAsync(gamePath, status.Report));
            if (IsDisposed || Disposing) return;
            if (string.Equals(GetSelectedRecording()?.Name, gamePath, StringComparison.OrdinalIgnoreCase))
                RefreshSelectedDraftIcons();
            // Keep reuse/cleanup/role scoring off the UI thread as well as full transcription.
            RecordingTranscriptionResult result = await Task.Run(() =>
                TranscribeAudio.ProcessRecordingAsync(
                    gamePath,
                    transcriptOutputPath,
                    transcriptionOptions,
                    log: status.Report
                )
            );

            if (!IsDisposed && !Disposing)
            {
                if (!result.TranscriptGenerated)
                {
                    ShowReusedTranscriptionDevices();
                }
                else if (result.Transcript.Execution is { } execution)
                {
                    if (result.Transcript.DiarizationExecution is { } diarizationExecution)
                        ShowAccelerationExecution(diarizationExecution);
                    ShowAccelerationExecution(execution);
                }
                else
                {
                    whisperJobLabel.Text = "No speech detected · Whisper did not run";
                    if (result.Transcript.DiarizationExecution is { } diarizationExecution)
                        ShowAccelerationExecution(diarizationExecution);
                    accelerationDetailLabel.Text =
                        "No speech was detected; no Whisper inference was required.";
                }
                string transcriptAction = result.TranscriptGenerated
                    ? "Transcript generated"
                    : "Transcript reused";
                string rolesAction = result.SpeakerRolesGenerated
                    ? "speaker roles generated"
                    : "existing speaker roles kept";
                string toneAction = result.Emotion?.Error is not null ? "Comms Tone unavailable (see log / model setup)"
                    : result.Emotion?.Generated == true ? "Comms Tone generated" : "Comms Tone current";
                if (result.SpeakerRoleError == null)
                    SetTranscriptionStatus(
                        $"{match} · {transcriptAction}, post-processing saved; {rolesAction}; {toneAction}.",
                        result.Emotion?.Error is null ? SuccessText : MutedText
                    );
                else
                    SetTranscriptionStatus(
                        $"{match} · Post-processing saved. Could not generate speaker roles.",
                        ErrorText
                    );
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                AppendToLog($"Could not process transcript for {match}: {ex.Message}");
                SetTranscriptionStatus($"{match} · Transcript processing failed.", ErrorText);
                MessageBox.Show(this, ex.Message, "Transcript processing failed");
            }
        }
        finally
        {
            transcribingGamePath = null;
            if (!IsDisposed && !Disposing)
            {
                transcriptionProgressBar.MarqueeAnimationSpeed = 0;
                transcriptionProgressBar.Visible = false;
                UpdateHistoryActions();
                ListViewItem? current = GetSelectedRecording();
                if (string.Equals(current?.Name, gamePath, StringComparison.OrdinalIgnoreCase))
                    await RefreshCommsAsync(current);
            }
        }
    }

    private void SetTranscriptionStatus(string message, Color color)
    {
        transcriptionStatusLabel.Text = message;
        transcriptionStatusLabel.ForeColor = color;
        historyToolTip.SetToolTip(transcriptionStatusLabel, message);
    }

    private async void openReplayButton_Click(object? sender, EventArgs e)
    {
        if (
            deletingRecording
            || openingReplay
            || GetSelectedRecording()?.Tag is not string selectedPath
        )
        {
            return;
        }

        string? gameFolder = Directory.Exists(selectedPath)
            ? selectedPath
            : Path.GetDirectoryName(selectedPath);

        if (gameFolder == null || !Directory.Exists(gameFolder))
        {
            MessageBox.Show(this, "The recording folder could not be found.");
            return;
        }

        // Your folders are named Game_<gameId>_<sessionId>.
        string[] parts = Path.GetFileName(gameFolder).Split('_', 3);
        if (parts.Length < 2 || !long.TryParse(parts[1], out long gameId))
        {
            MessageBox.Show(this, "Could not determine the game ID.");
            return;
        }

        openingReplay = true;
        UpdateHistoryActions();
        try
        {
            string? replayPath = Directory
                .EnumerateFiles(gameFolder, $"*-{gameId}.rofl")
                .FirstOrDefault();

            if (replayPath == null)
            {
                MessageBox.Show(this, "No saved replay was found for this game.");
                return;
            }

            string gamePath = Path.Combine(gameFolder, $"Game_{gameId}");
            if (replayAudio?.TryFocus(gamePath) == true) return;
            var replayOptions = await PrepareReplayAudioAsync(gamePath);
            if (IsDisposed || Disposing) return;
            int[] previousProcesses = ReplayAudioSession.ExistingGameProcesses();
            replayAudio?.Dispose();
            videoReview?.Pause();
            await _leagueClientApi.WatchSavedReplayAsync(gameId, replayPath);
            if (!IsDisposed && !Disposing && replayOptions is not null)
                StartReplayAudio(gamePath, replayOptions, previousProcesses);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the replay.\n\n{ex.Message}");
        }
        finally
        {
            openingReplay = false;
            if (!IsDisposed && !Disposing)
                UpdateHistoryActions();
        }
    }

    #endregion

    #region Speaker role editing

    private bool editingSpeakerRoles;

    private sealed record CommsRoleContext(
        string GamePath,
        TranscriptionResult Transcript,
        IReadOnlyDictionary<string, string> Labels
    );

    private CommsRoleContext? commsRoleContext;

    private bool CanEditSpeakerRoles() =>
        !editingSpeakerRoles
        && !deletingRecording
        && !startingSession
        && !stoppingSession
        && !savingSession
        && !allowClose
        && transcribingGamePath == null
        && commsRoleContext is { } view
        && string.Equals(
            GetSelectedRecording()?.Name,
            view.GamePath,
            StringComparison.OrdinalIgnoreCase
        )
        && (
            (view.Transcript.Speakers ?? []).Any(TranscribeAudio.IsEditableSpeaker)
            || (view.Transcript.Words ?? []).Any(w =>
                w is not null && TranscribeAudio.IsEditableSpeaker(w.Speaker)
            )
        );

    private void UpdateSpeakerRoleEditingAction() =>
        editSpeakerRolesButton.Enabled = CanEditSpeakerRoles();

    private async void editSpeakerRolesButton_Click(object? sender, EventArgs e) =>
        await EditSpeakerRolesAsync(null);

    private async void commsGrid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex == commsTimeColumn.Index)
        {
            SeekToCommsRow(e.RowIndex);
            return;
        }
        if (
            e.RowIndex >= 0
            && e.ColumnIndex == commsSpeakerColumn.Index
            && commsGrid.Rows[e.RowIndex].Tag is string speaker
            && TranscribeAudio.IsEditableSpeaker(speaker)
        )
            await EditSpeakerRolesAsync(speaker);
    }

    private async void commsGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && commsGrid.CurrentCell?.ColumnIndex == commsTimeColumn.Index)
        {
            e.Handled = e.SuppressKeyPress = true;
            SeekToCommsRow(commsGrid.CurrentCell.RowIndex);
            return;
        }
        if (
            e.KeyCode == Keys.Enter
            && commsGrid.CurrentCell?.ColumnIndex == commsSpeakerColumn.Index
            && commsGrid.CurrentRow?.Tag is string speaker
            && TranscribeAudio.IsEditableSpeaker(speaker)
        )
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            await EditSpeakerRolesAsync(speaker);
        }
    }

    private void commsGrid_CellMouseEnter(object? sender, DataGridViewCellEventArgs e) =>
        commsGrid.Cursor =
            (e.ColumnIndex == commsTimeColumn.Index && CanSeekToCommsRow(e.RowIndex)) ||
            (
                e.RowIndex >= 0
                && e.ColumnIndex == commsSpeakerColumn.Index
                && CanEditSpeakerRoles()
                && TranscribeAudio.IsEditableSpeaker(commsGrid.Rows[e.RowIndex].Tag as string)
            )
                ? Cursors.Hand
                : Cursors.Default;

    private void commsGrid_CellMouseLeave(object? sender, DataGridViewCellEventArgs e) =>
        commsGrid.Cursor = Cursors.Default;

    private async Task EditSpeakerRolesAsync(string? selectedSpeaker)
    {
        if (!CanEditSpeakerRoles() || commsRoleContext is not { } view)
            return;
        int firstVisibleRow = commsGrid.FirstDisplayedScrollingRowIndex;
        int selectedRow = commsGrid.CurrentCell?.RowIndex ?? -1;
        editingSpeakerRoles = true;
        historyScrollView.Enabled = false;
        commsGrid.Cursor = Cursors.Default;
        UpdateFormControls();
        string gameName = Path.GetFileName(view.GamePath).Replace('_', ' ');
        bool saved = false;
        try
        {
            // Read the current sidecar again rather than editing a potentially stale preview snapshot.
            var labels = await TranscribeAudio.ReadSpeakerRoleLabelsAsync(view.Transcript.JsonPath);
            if (IsDisposed || Disposing)
                return;
            using var dialog = new SpeakerRoleEditorDialog(
                gameName,
                TranscribeAudio.BuildSpeakerRoleEditEntries(view.Transcript, labels),
                selectedSpeaker
            );
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            SetTranscriptionStatus($"{gameName} · Saving speaker roles…", MutedText);
            if (dialog.UseAutomaticRoles)
                await Task.Run(() =>
                    TranscribeAudio.AssignSpeakerRolesAsync(view.Transcript.JsonPath)
                );
            else
                await TranscribeAudio.SaveSpeakerRolesAsync(
                    view.Transcript.JsonPath,
                    dialog.Assignments
                );
            saved = true;
            if (!IsDisposed && !Disposing)
            {
                string message = dialog.UseAutomaticRoles
                    ? "Automatic speaker roles restored."
                    : "Manual speaker roles saved for this game.";
                SetTranscriptionStatus($"{gameName} · {message}", SuccessText);
                AppendToLog($"{gameName}: {message}");
            }
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or System.Text.Json.JsonException
                        or ArgumentException
            )
        {
            if (!IsDisposed && !Disposing)
            {
                SetTranscriptionStatus($"{gameName} · Could not save speaker roles.", ErrorText);
                AppendToLog($"Could not save speaker roles for {gameName}: {ex.Message}");
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Speaker roles could not be saved",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
        finally
        {
            editingSpeakerRoles = false;
            if (!IsDisposed && !Disposing)
            {
                historyScrollView.Enabled = true;
                UpdateFormControls();
                if (
                    saved
                    && string.Equals(
                        GetSelectedRecording()?.Name,
                        view.GamePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    await RefreshCommsAsync(GetSelectedRecording());
                    if (
                        !IsDisposed
                        && !Disposing
                        && string.Equals(
                            GetSelectedRecording()?.Name,
                            view.GamePath,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && commsGrid.Visible
                        && commsGrid.Rows.Count > 0
                    )
                    {
                        if (firstVisibleRow >= 0)
                            commsGrid.FirstDisplayedScrollingRowIndex = Math.Min(
                                firstVisibleRow,
                                commsGrid.Rows.Count - 1
                            );
                        if (selectedRow >= 0)
                            commsGrid.CurrentCell = commsGrid
                                .Rows[Math.Min(selectedRow, commsGrid.Rows.Count - 1)]
                                .Cells[commsSpeakerColumn.Index];
                    }
                }
            }
        }
    }

    #endregion

    #region Device setup and status

    private TranscriptionSettings transcriptionSettings = new();
    private readonly Dictionary<string, NumericUpDown> transcriptionSettingsInputs = new();
    private Control? transcriptionSettingsEditor;
    private Label? transcriptionSettingsStatus;
    private CheckBox vadEnabledInput = null!, customDiarizationInput = null!, retryChunksInput = null!, emotionEnabledInput = null!;
    private bool loadingTranscriptionSettings;

    private void InitializeTranscriptionSettings()
    {
        transcriptionSettings = TranscriptionSettingsPreferences.Load();
        var settingsTabs = WorkspaceView();
        var speechPage = WorkspacePage("Speech");
        var devicePage = WorkspacePage("Devices");
        settingsTabs.TabPages.AddRange([speechPage, devicePage]);

        devicesPageTab.Controls.Remove(devicesLayout);
        devicesLayout.Controls.Remove(devicesTitle);
        devicesLayout.RowStyles[0].Height = 0;
        devicePage.Controls.Add(devicesLayout);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty,
            BackColor = AppTheme.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(devicesTitle, 0, 0);
        root.Controls.Add(SectionNavigation(settingsTabs, (speechPage, "Speech"), (devicePage, "Devices")), 0, 1);
        root.Controls.Add(settingsTabs, 0, 2);
        devicesPageTab.Controls.Add(root);

        var scroll = new DarkSurface { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppTheme.Background };
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 3, Padding = new Padding(2), Margin = Padding.Empty,
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++) editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        transcriptionSettingsEditor = editor;
        scroll.Controls.Add(editor);
        speechPage.Controls.Add(scroll);

        TableLayoutPanel Card(string title)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 0, Padding = new Padding(16), Margin = new Padding(0, 0, 10, 10),
                BackColor = AppTheme.Surface,
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            Wide(card, new Label
            {
                Text = title, Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                AutoSize = true, Margin = new Padding(0, 0, 0, 12), ForeColor = AppTheme.Text,
            });
            return card;
        }
        void Wide(TableLayoutPanel card, Control control)
        {
            int row = card.RowCount++;
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(control, 0, row);
            card.SetColumnSpan(control, 2);
        }
        void Number(TableLayoutPanel card, string key, string label, decimal min, decimal max, int decimals = 0)
        {
            int row = card.RowCount++;
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            card.Controls.Add(new Label
            {
                Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = AppTheme.Text, AutoEllipsis = true, Margin = new Padding(0, 0, 8, 0),
            }, 0, row);
            var input = new NumericUpDown
            {
                Name = "transcriptionSettings_" + key, AccessibleName = label,
                Minimum = min, Maximum = max, DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1 : 0.01m,
                Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3),
                BackColor = AppTheme.Inset, ForeColor = AppTheme.Text,
            };
            transcriptionSettingsInputs.Add(key, input);
            card.Controls.Add(input, 1, row);
        }
        CheckBox Toggle(TableLayoutPanel card, string label)
        {
            var input = new CheckBox
            {
                Text = label, AccessibleName = label, AutoSize = true,
                Margin = new Padding(0, 4, 0, 8), ForeColor = AppTheme.Text,
            };
            Wide(card, input);
            return input;
        }
        void Note(TableLayoutPanel card, string text)
        {
            var label = new Label
            {
                Text = text, AutoSize = true, Dock = DockStyle.Top,
                Margin = new Padding(0, 5, 0, 10), ForeColor = AppTheme.Muted,
            };
            card.SizeChanged += (_, _) => label.MaximumSize = new Size(Math.Max(100, card.ClientSize.Width - 36), 0);
            Wide(card, label);
        }
        var asr = Card("Transcription · Whisper");
        Number(asr, "BeamSize", "Beam size", 1, 20);
        Number(asr, "ChunkSeconds", "Chunk duration (seconds)", 1, 120);
        Number(asr, "OverlapSeconds", "Context on each side (seconds)", 0, 13);
        vadEnabledInput = Toggle(asr, "Enable speech detection (VAD)");
        Number(asr, "VadThreshold", "Speech threshold", 0.01m, 0.99m, 2);
        Number(asr, "VadMinSpeech", "Minimum speech (ms)", 0, 60000);
        Number(asr, "VadMinSilence", "Minimum silence (ms)", 1, 60000);
        Number(asr, "VadPadding", "Padding on each side (ms)", 0, 60000);
        retryChunksInput = Toggle(asr, "Retry repetitive / collapsed chunks");
        Note(asr, "VAD skips windows with no detected speech. DTW limits each audio window to 28 seconds including context.");
        editor.Controls.Add(asr, 0, 0);

        var diar = Card("Diarization · NeMo");
        customDiarizationInput = Toggle(diar, "Use custom segmentation settings");
        Number(diar, "DiarOnset", "Speech start threshold", 0.01m, 0.99m, 2);
        Number(diar, "DiarOffset", "Speech end threshold", 0.01m, 0.99m, 2);
        Number(diar, "DiarMinOn", "Minimum speech (seconds)", 0, 60, 2);
        Number(diar, "DiarMinOff", "Fill silence gaps below (seconds)", 0, 60, 2);
        Number(diar, "DiarPadOnset", "Start padding (seconds)", 0, 60, 2);
        Number(diar, "DiarPadOffset", "End padding (seconds)", 0, 60, 2);
        Note(diar, "Speaker count: automatic. The current Nemotron-3 model supports up to 8 speakers; a fixed count is unavailable.");
        Note(diar, "Custom segmentation requires a NeMo build that supports all six controls. Turn it off to use runtime defaults.");
        editor.Controls.Add(diar, 1, 0);

        var emotion = Card("Emotion analysis · Team Outlook");
        emotionEnabledInput = Toggle(emotion, "Enable emotion analysis");
        Number(emotion, "EmotionWindow", "Audio context window (seconds)", 5, 60);
        Note(emotion, "Default: 20 seconds. Analyzes the combined comms audio across speaker turns and overlap, with a new graph point every 5 seconds. Longer windows provide more context and smooth brief changes.");
        Note(emotion, "Changes save automatically. Select Transcribe to refresh existing games; changing this window reruns emotion analysis while keeping current transcription and diarization results. Uses the packaged audEERING ONNX model on CPU.");
        editor.Controls.Add(emotion, 0, 1);
        editor.SetColumnSpan(emotion, 2);

        transcriptionSettingsStatus = new Label
        {
            Text = "Valid changes save automatically and apply to the next transcription.",
            AutoSize = true, Dock = DockStyle.Top, ForeColor = AppTheme.Muted,
            Margin = new Padding(0, 0, 10, 16),
        };
        editor.SizeChanged += (_, _) => transcriptionSettingsStatus.MaximumSize = new Size(Math.Max(100, editor.Width - 20), 0);
        editor.Controls.Add(transcriptionSettingsStatus, 0, 2);
        editor.SetColumnSpan(transcriptionSettingsStatus, 2);

        loadingTranscriptionSettings = true;
        SetTranscriptionInputs(transcriptionSettings);
        SetDiarizationInputs(transcriptionSettings.Diarization);
        emotionEnabledInput.Checked = transcriptionSettings.Emotion.Enabled;
        SetTranscriptionSettingNumber("EmotionWindow", transcriptionSettings.Emotion.WindowSeconds);
        loadingTranscriptionSettings = false;
        foreach (var input in transcriptionSettingsInputs.Values) input.ValueChanged += (_, _) => SaveTranscriptionSettings();
        foreach (var input in new[] { vadEnabledInput, customDiarizationInput, retryChunksInput, emotionEnabledInput })
            input.CheckedChanged += (_, _) => SaveTranscriptionSettings();
        UpdateTranscriptionSettingsInputs();
    }

    private void SetTranscriptionSettingNumber(string key, double value)
    {
        var input = transcriptionSettingsInputs[key];
        input.Value = Math.Clamp((decimal)value, input.Minimum, input.Maximum);
    }

    private void SetTranscriptionInputs(TranscriptionSettings settings)
    {
        SetTranscriptionSettingNumber("BeamSize", settings.BeamSize);
        SetTranscriptionSettingNumber("ChunkSeconds", settings.ChunkSeconds);
        SetTranscriptionSettingNumber("OverlapSeconds", settings.OverlapSeconds);
        vadEnabledInput.Checked = settings.EnableVad;
        retryChunksInput.Checked = settings.RetryRepetitiveChunks;
        SetTranscriptionSettingNumber("VadThreshold", settings.Vad.Threshold);
        SetTranscriptionSettingNumber("VadMinSpeech", settings.Vad.MinSpeechDurationMs);
        SetTranscriptionSettingNumber("VadMinSilence", settings.Vad.MinSilenceDurationMs);
        SetTranscriptionSettingNumber("VadPadding", settings.Vad.SpeechPaddingMs);
    }

    private void SetDiarizationInputs(DiarizationSegmentationOptions settings)
    {
        customDiarizationInput.Checked = settings.Enabled;
        SetTranscriptionSettingNumber("DiarOnset", settings.Onset);
        SetTranscriptionSettingNumber("DiarOffset", settings.Offset);
        SetTranscriptionSettingNumber("DiarMinOn", settings.MinDurationOnSeconds);
        SetTranscriptionSettingNumber("DiarMinOff", settings.MinDurationOffSeconds);
        SetTranscriptionSettingNumber("DiarPadOnset", settings.PadOnsetSeconds);
        SetTranscriptionSettingNumber("DiarPadOffset", settings.PadOffsetSeconds);
    }

    private TranscriptionSettings ReadTranscriptionSettings()
    {
        double Number(string key) => (double)transcriptionSettingsInputs[key].Value;
        return new()
        {
            BeamSize = (int)Number("BeamSize"), ChunkSeconds = (int)Number("ChunkSeconds"),
            OverlapSeconds = (int)Number("OverlapSeconds"), EnableVad = vadEnabledInput.Checked,
            Vad = new()
            {
                Threshold = Number("VadThreshold"), MinSpeechDurationMs = (int)Number("VadMinSpeech"),
                MinSilenceDurationMs = (int)Number("VadMinSilence"), SpeechPaddingMs = (int)Number("VadPadding"),
            },
            Diarization = new()
            {
                Enabled = customDiarizationInput.Checked, Onset = Number("DiarOnset"), Offset = Number("DiarOffset"),
                MinDurationOnSeconds = Number("DiarMinOn"), MinDurationOffSeconds = Number("DiarMinOff"),
                PadOnsetSeconds = Number("DiarPadOnset"), PadOffsetSeconds = Number("DiarPadOffset"),
            },
            Emotion = transcriptionSettings.Emotion with
            {
                Enabled = emotionEnabledInput.Checked, WindowSeconds = (int)Number("EmotionWindow"),
            },
            RetryRepetitiveChunks = retryChunksInput.Checked,
        };
    }

    private void UpdateTranscriptionSettingsInputs()
    {
        foreach (var (key, input) in transcriptionSettingsInputs)
            input.Enabled = key.StartsWith("Vad", StringComparison.Ordinal) ? vadEnabledInput.Checked
                : key.StartsWith("Emotion", StringComparison.Ordinal) ? emotionEnabledInput.Checked
                : !key.StartsWith("Diar", StringComparison.Ordinal) || customDiarizationInput.Checked;
    }

    private void SaveTranscriptionSettings()
    {
        if (loadingTranscriptionSettings || transcriptionSettingsStatus is null) return;
        UpdateTranscriptionSettingsInputs();
        try
        {
            var settings = ReadTranscriptionSettings();
            TranscribeAudio.ValidateInferenceOptions(settings.ToOptions());
            transcriptionSettings = settings;
            TranscriptionSettingsPreferences.Save(settings);
            transcriptionSettingsStatus.Text = "Settings saved. Select Transcribe to apply them, including to existing games.";
            transcriptionSettingsStatus.ForeColor = SuccessText;
        }
        catch (ArgumentException ex)
        {
            transcriptionSettingsStatus.Text = "Settings are invalid: " + ex.Message;
            transcriptionSettingsStatus.ForeColor = ErrorText;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            transcriptionSettingsStatus.Text = "Settings apply for this session, but could not be saved: " + ex.Message;
            transcriptionSettingsStatus.ForeColor = ErrorText;
        }
    }

    private TranscriptionCapabilities? accelerationCapabilities;
    private NemoCapabilities? nemoCapabilities;
    private TranscriptionExecutionInfo? lastAccelerationExecution,
        lastNemoExecution,
        lastWhisperTest,
        lastNemoTest;
    private bool checkingAcceleration,
        testingAcceleration;

    private TranscriptionDeviceMode SelectedDeviceMode =>
        transcriptionDeviceCombo.SelectedIndex == 1
            ? TranscriptionDeviceMode.CpuOnly
            : TranscriptionDeviceMode.Auto;
    private TranscriptionDeviceMode SelectedNemoMode =>
        nemoDeviceCombo.SelectedIndex == 1
            ? TranscriptionDeviceMode.CpuOnly
            : TranscriptionDeviceMode.Auto;

    private TranscriptionOptions DeviceOptions() =>
        transcriptionSettings.ToOptions() with { DeviceMode = SelectedDeviceMode, NemoDeviceMode = SelectedNemoMode };

    private void InitializeAccelerationPanel()
    {
        InitializeTranscriptionSettings();
        var preferences = TranscriptionDevicePreferences.Load();
        transcriptionDeviceCombo.SelectedIndex = preferences.Mode == TranscriptionDeviceMode.CpuOnly ? 1 : 0;
        nemoDeviceCombo.SelectedIndex = preferences.NemoMode == TranscriptionDeviceMode.CpuOnly ? 1 : 0;
        TranscriptionAcceleration.ConfigureRuntime(preferences.Mode);
        async void Changed(object? sender, EventArgs e)
        {
            try
            {
                new TranscriptionDevicePreferences
                {
                    Mode = SelectedDeviceMode,
                    NemoMode = SelectedNemoMode,
                }.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppendToLog("Device preferences could not be saved: " + ex.Message);
            }
            TranscriptionAcceleration.ConfigureRuntime(SelectedDeviceMode);
            // Tests and jobs remain historical facts, with their requested mode shown.
            ShowAccelerationForecast();
            await RefreshAccelerationStatusAsync();
        }
        transcriptionDeviceCombo.SelectedIndexChanged += Changed;
        nemoDeviceCombo.SelectedIndexChanged += Changed;
        checkGpuButton.Click += async (_, _) => await RefreshAccelerationStatusAsync();
        gpuSetupButton.Click += (_, _) => ShowGpuSetup();
    }

    private void UpdateAccelerationActions()
    {
        if (transcriptionDeviceCombo is null)
            return;
        bool enabled =
            transcribingGamePath is null && !checkingAcceleration && !testingAcceleration;
        transcriptionDeviceCombo.Enabled = enabled;
        nemoDeviceCombo.Enabled = enabled;
        checkGpuButton.Enabled = enabled;
        gpuSetupButton.Enabled = enabled;
        if (transcriptionSettingsEditor is not null) transcriptionSettingsEditor.Enabled = enabled;
    }

    private async Task RefreshAccelerationStatusAsync()
    {
        if (checkingAcceleration || testingAcceleration)
            return;
        checkingAcceleration = true;
        UpdateHistoryActions();
        accelerationStatusLabel.Text = "Checking availability…";
        nemoStatusLabel.Text = "Checking availability…";
        try
        {
            var options = DeviceOptions();
            var whisper = TranscriptionAcceleration.CheckAsync();
            var nemo = NemoAcceleration.CheckAsync(options, refresh: true);
            await Task.WhenAll(whisper, nemo);
            if (IsDisposed || Disposing)
                return;
            accelerationCapabilities = await whisper;
            nemoCapabilities = await nemo;
            ShowAccelerationForecast();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                accelerationStatusLabel.Text = "Availability check incomplete";
                nemoStatusLabel.Text = "Availability check incomplete";
                accelerationDetailLabel.Text = "Could not check device availability: " + ex.Message;
            }
        }
        finally
        {
            checkingAcceleration = false;
            if (!IsDisposed && !Disposing)
                UpdateHistoryActions();
        }
    }

    private void ShowAccelerationForecast()
    {
        if (accelerationCapabilities is not null)
        {
            accelerationStatusLabel.Text = TranscriptionAcceleration.Forecast(
                accelerationCapabilities,
                SelectedDeviceMode,
                TranscriptionAcceleration.LoadedRuntime
            );
            accelerationStatusLabel.ForeColor =
                accelerationCapabilities.GpuExpected
                && SelectedDeviceMode == TranscriptionDeviceMode.Auto
                    ? SuccessText
                    : MutedText;
        }
        if (nemoCapabilities is not null)
        {
            nemoStatusLabel.Text = nemoCapabilities.Forecast(SelectedNemoMode);
            nemoStatusLabel.ForeColor =
                !nemoCapabilities.Exists || nemoCapabilities.DiarizationCompiled == false
                    ? ErrorText
                : nemoCapabilities.GpuExpected && SelectedNemoMode == TranscriptionDeviceMode.Auto
                    ? SuccessText
                : MutedText;
        }
    }

    private void ShowAccelerationExecution(TranscriptionExecutionInfo info, bool modelTest = false)
    {
        if (IsDisposed || Disposing)
            return;
        bool nemo = info.Stage.Equals("NeMo", StringComparison.OrdinalIgnoreCase);
        bool test = modelTest || info.Source == "model_test";
        if (test)
        {
            info = info with { Source = "model_test" };
            if (nemo)
                lastNemoTest = info;
            else
                lastWhisperTest = info;
        }
        else if (nemo)
            lastNemoExecution = info;
        else
            lastAccelerationExecution = info;
        var label = test
            ? nemo
                ? nemoTestLabel
                : whisperTestLabel
            : nemo
                ? nemoJobLabel
                : whisperJobLabel;
        bool gpu = info.Confirmed && info.Device is "cuda" or "gpu";
        string outcome =
            gpu ? "GPU confirmed · " + info.Backend
            : info.Confirmed && info.Device == "cpu" ? "CPU confirmed"
            : info.Device == "not_run" ? "Existing speaker timings reused"
            : "Backend not reported · view diagnostics";
        bool restart =
            !nemo
            && test
            && gpu
            && TranscriptionAcceleration.LoadedRuntime
                is Whisper.net.LibraryLoader.RuntimeLibrary.Cpu
                    or Whisper.net.LibraryLoader.RuntimeLibrary.CpuNoAvx;
        if (restart)
            outcome += " · restart app to enable";
        label.Text =
            outcome
            + "  ·  "
            + info.RequestedMode
            + "  ·  "
            + info.ObservedUtc.ToLocalTime().ToString("t");
        label.ForeColor = gpu ? SuccessText : MutedText;
        historyToolTip.SetToolTip(label, info.Detail);
        accelerationDetailLabel.Text = restart
            ? "Whisper's isolated GPU test passed. Restart the app with Auto selected to replace its already-loaded CPU runtime."
            : (test ? "Model test · " : "Transcription · ") + info.Stage + " · " + info.Detail;
        accelerationDetailLabel.ForeColor = MutedText;
        ShowAccelerationForecast();
    }

    private TranscriptionOptions CreateUiTranscriptionOptions(string gamePath)
    {
        // Validate visible values too: an invalid intermediate edit must never run older settings.
        var settings = ReadTranscriptionSettings();
        TranscribeAudio.ValidateInferenceOptions(settings.ToOptions());
        transcriptionSettings = settings;
        whisperJobLabel.Text = "Checking whether this job needs inference…";
        nemoJobLabel.Text = "Checking whether this job needs inference…";
        return DeviceOptions() with
        {
            ExecutionProgress = new Progress<TranscriptionExecutionInfo>(info =>
            {
                if (transcribingGamePath == gamePath && !IsDisposed && !Disposing)
                    ShowAccelerationExecution(info);
            }),
        };
    }

    private void ShowReusedTranscriptionDevices()
    {
        whisperJobLabel.Text = "Saved transcript reused · Whisper skipped";
        nemoJobLabel.Text = "Saved speaker timings reused · NeMo skipped";
        whisperJobLabel.ForeColor = nemoJobLabel.ForeColor = MutedText;
    }

    private void ShowGpuSetup()
    {
        using var dialog = new Form
        {
            Text = "Whisper and NeMo device setup",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(870, 740),
            MinimumSize = new Size(680, 540),
            BackColor = AppTheme.Background,
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI", 9),
            ShowInTaskbar = false,
        };
        dialog.HandleCreated += (_, _) => AppTheme.ApplyNative(dialog);
        using var cancellation = new CancellationTokenSource();
        dialog.FormClosing += (_, _) => cancellation.Cancel();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text =
                "Whisper and NeMo need independent GPU-capable runtimes. Tests use separate processes and temporary files.",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = Color.White,
        };
        var details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.Text,
            Text = BuildAccelerationGuide(),
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        Button Add(string text, Action action)
        {
            var b = new DarkButton
            {
                Text = text,
                AutoSize = true,
                Height = 30,
                BackColor = AppTheme.Raised,
                ForeColor = AppTheme.Text,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 8, 6),
            };
            b.Click += (_, _) => action();
            buttons.Controls.Add(b);
            return b;
        }
        var whisper = Add("Test Whisper", () => { });
        var nemo = Add("Test NeMo", () => { });
        var both = Add("Test both", () => { });
        foreach (var b in new[] { whisper, nemo, both })
            b.Enabled =
                transcribingGamePath is null && !testingAcceleration && !checkingAcceleration;
        async Task Test(bool runWhisper, bool runNemo)
        {
            if (testingAcceleration || transcribingGamePath is not null || checkingAcceleration)
                return;
            testingAcceleration = true;
            UpdateHistoryActions();
            foreach (var b in new[] { whisper, nemo, both })
                b.Enabled = false;
            var errors = new StringBuilder();
            var options = DeviceOptions();
            async Task One(string name, Func<Task<TranscriptionExecutionInfo>> execute)
            {
                if (cancellation.IsCancellationRequested)
                    return;
                details.Text =
                    "Testing "
                    + name
                    + ". Model initialization can use memory and take a moment.\r\n\r\n"
                    + BuildAccelerationGuide();
                try
                {
                    var result = await execute();
                    if (!dialog.IsDisposed && !dialog.Disposing)
                        ShowAccelerationExecution(result, true);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    errors.AppendLine(name + " test failed: " + ex.Message);
                    if (!IsDisposed && !Disposing && !dialog.IsDisposed)
                    {
                        var label = name == "NeMo" ? nemoTestLabel : whisperTestLabel;
                        label.Text = "Test failed · " + ex.Message;
                        label.ForeColor = ErrorText;
                    }
                }
            }
            if (runWhisper)
                await One(
                    "Whisper",
                    () => TranscriptionAcceleration.TestModelAsync(options, cancellation.Token)
                );
            if (runNemo)
                await One(
                    "NeMo",
                    () => NemoAcceleration.TestModelAsync(options, cancellation.Token)
                );
            testingAcceleration = false;
            if (!IsDisposed && !Disposing)
                UpdateHistoryActions();
            if (!dialog.IsDisposed && !dialog.Disposing)
            {
                details.Text = errors + Environment.NewLine + BuildAccelerationGuide();
                foreach (var b in new[] { whisper, nemo, both })
                    b.Enabled = transcribingGamePath is null && !checkingAcceleration;
            }
        }
        whisper.Click += async (_, _) => await Test(true, false);
        nemo.Click += async (_, _) => await Test(false, true);
        both.Click += async (_, _) => await Test(true, true);
        Add(
            "NVIDIA driver",
            () => OpenAccelerationLink("https://www.nvidia.com/Download/index.aspx")
        );
        Add(
            "CUDA setup",
            () => OpenAccelerationLink("https://developer.nvidia.com/cuda-toolkit-archive")
        );
        Add(
            "NeMo runtimes",
            () => OpenAccelerationLink("https://github.com/NVIDIA/NeMo-Speech.cpp/releases")
        );
        Add(
            "NeMo build guide",
            () =>
                OpenAccelerationLink(
                    "https://github.com/NVIDIA/NeMo-Speech.cpp/blob/main/docs/build.md"
                )
        );
        Add(
            "Whisper requirements",
            () =>
                OpenAccelerationLink(
                    "https://github.com/sandrohanea/whisper.net#whispernetruntimecuda12"
                )
        );
        Add(
            "Copy report",
            () =>
            {
                try
                {
                    Clipboard.SetText(details.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, ex.Message, "Could not copy report");
                }
            }
        );
        Add("Close", () => dialog.Close());
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(details, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout);
        AppTheme.Apply(dialog);
        dialog.ShowDialog(this);
    }

    private void OpenAccelerationLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open setup link");
        }
    }

    private string BuildAccelerationGuide()
    {
        var s = new StringBuilder();
        s.AppendLine("WHISPER CHECK");
        s.AppendLine(accelerationCapabilities?.Describe() ?? "Check has not completed.");
        s.AppendLine(
            "Whisper preference: "
                + SelectedDeviceMode
                + "; loaded runtime: "
                + (TranscriptionAcceleration.LoadedRuntime?.ToString() ?? "not loaded")
        );
        s.AppendLine();
        s.AppendLine("NEMO CHECK");
        s.AppendLine(nemoCapabilities?.Describe() ?? "Check has not completed.");
        s.AppendLine("NeMo preference: " + SelectedNemoMode);
        s.AppendLine();
        s.AppendLine("WHISPER SETUP");
        s.AppendLine(
            "Select Auto. Install/update your NVIDIA driver and the matching CUDA Toolkit (12.4.1+ in the CUDA 12 family, or 13.0.1+ in CUDA 13). CUDA runtime and cuBLAS DLLs must be visible to the app, normally through Toolkit bin on PATH. Restore matching Whisper.net 1.9.1 CPU/CUDA/CUDA12 Windows packages and ship the entire x64 publish folder. Restart the app and Visual Studio after PATH changes. If CPU was already loaded, restart with Auto selected. Test Whisper initializes the model and decodes a two-second silent sample in a separate process to verify the inference backend."
        );
        s.AppendLine();
        s.AppendLine("NEMO SETUP");
        s.AppendLine(
            "Installing CUDA or enabling Whisper does not turn a CPU-only NeMo EXE into a GPU build. Use NeMo runtimes to obtain the Windows x86_64 CUDA archive, or follow the official NeMo build guide for a Windows CUDA build. Close the app before replacing runtime/nemo, preserve the complete bin/lib/DLL layout, then rebuild/publish. A build must include diarization and a CUDA backend. Run bin/nemo-speech.exe doctor --json to verify its features and visible devices. NeMo may require a different CUDA family from Whisper: follow the selected archive's requirements. Hardware readiness is checked on the target PC."
        );
        s.AppendLine(
            "Choose NeMo Auto to prefer an available GPU; CPU only explicitly requests CPU when the executable supports it. The app reads diarize --help and uses --device or legacy --gpu only when advertised. An older executable without doctor can still run, but unsupported controls or missing backend logs are reported as unverified. Upgrade the runtime for full checks."
        );
        s.AppendLine(
            "Test NeMo loads the existing diarization model and processes two seconds of temporary silence. Empty speech output is valid; the backend initialization is the test. It does not replace recordings/transcripts/RTTM. Auto can retry a failed GPU attempt on CPU; the final backend and initial error remain in the report."
        );
        s.AppendLine();
        s.AppendLine("SHARED NOTES");
        s.AppendLine(
            "This CUDA setup needs a supported NVIDIA GPU. If you have only AMD/Intel hardware, Whisper's configured CUDA path cannot accelerate it; NeMo doctor may report another compiled backend such as Vulkan. CPU work for audio, VAD, DTW and cleanup remains normal. Free GPU memory can affect model loading. Refresh availability checks readiness only; use Test both for actual model backend selection. Test both runs sequentially to avoid loading two models into GPU memory at once. Closing this dialog stops an active test."
        );
        s.AppendLine(
            "Whisper and NeMo preferences are independent. NeMo is a new process per run and does not inherit Whisper's one-time runtime lock. A saved transcript or reused RTTM does not run the corresponding model; those cases are labelled reused."
        );
        foreach (
            var execution in new[]
            {
                lastWhisperTest,
                lastNemoTest,
                lastAccelerationExecution,
                lastNemoExecution,
            }
        )
            if (execution is not null)
            {
                s.AppendLine();
                s.AppendLine(
                    execution.Stage.ToUpperInvariant()
                        + " "
                        + execution.Source.ToUpperInvariant()
                        + " "
                        + execution.ObservedUtc.ToLocalTime().ToString("g")
                );
                s.AppendLine(
                    $"Device={execution.Device}; backend={execution.Backend}; confirmed={execution.Confirmed}; {execution.Runtime}"
                );
                s.AppendLine(execution.Detail);
                foreach (var line in execution.Diagnostics)
                    s.AppendLine(line);
            }
        return s.ToString();
    }

    #endregion
}

#endregion

#region Speaker role dialog

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

#endregion

#region Recording file operations

internal sealed record RecordingDeletePlan(
    string RecordingFolder,
    string TranscriptionFolder,
    string AnalysisFolder,
    string RecordingsRoot
)
{
    internal string[] Folders => [AnalysisFolder, TranscriptionFolder, RecordingFolder];
}

internal static class RecordingHistoryFiles
{
    internal static RecordingDeletePlan GetDeletePlan(string gamePath)
    {
        gamePath = Path.GetFullPath(gamePath);
        string prefix = Path.GetFileName(gamePath);
        var folder = new DirectoryInfo(Path.GetDirectoryName(gamePath)!);
        var recordings = folder.Parent;
        if (
            !prefix.StartsWith("Game_", StringComparison.Ordinal)
            || !long.TryParse(prefix[5..], out long gameId)
            || gameId <= 0
            || !folder.Name.StartsWith(prefix + "_", StringComparison.Ordinal)
            || folder.Name.Length <= prefix.Length + 1
            || recordings?.Name.Equals("Recordings", StringComparison.OrdinalIgnoreCase) != true
            || recordings.Parent is null
        )
            throw new InvalidDataException(
                "The selected path is not a game folder under Recordings."
            );

        string root = recordings.Parent.FullName;
        return new RecordingDeletePlan(
            folder.FullName,
            Path.Combine(root, "Transcriptions", folder.Name),
            Path.Combine(root, "Analysis", folder.Name),
            recordings.FullName
        );
    }

    // Called only after the form has confirmed and blocked competing operations.
    internal static void DeleteGame(string gamePath)
    {
        var plan = GetDeletePlan(gamePath);
        // Check all targets before deleting anything. Do not follow a junction/symlink
        // into another recording or into files outside this game's folders.
        foreach (string target in plan.Folders)
        {
            RejectLink(new DirectoryInfo(Path.GetDirectoryName(target)!));
            InspectTree(new DirectoryInfo(target));
        }
        foreach (string target in plan.Folders)
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
    }

    private static void InspectTree(DirectoryInfo folder)
    {
        RejectLink(folder);
        if (!folder.Exists)
            return;
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            RejectLink(entry);
            if (entry is DirectoryInfo child)
                InspectTree(child);
        }
    }

    private static void RejectLink(FileSystemInfo entry)
    {
        if (
            entry.LinkTarget is not null
            || (entry.Exists && (entry.Attributes & System.IO.FileAttributes.ReparsePoint) != 0)
        )
            throw new IOException(
                $"Deletion stopped because this game path contains a linked file or folder: {entry.FullName}"
            );
    }
}

#endregion

#region Shared dark controls

internal static class AppTheme
{
    internal static readonly Color Background = Color.FromArgb(18, 21, 27);
    internal static readonly Color Surface = Color.FromArgb(25, 29, 37);
    internal static readonly Color Inset = Color.FromArgb(20, 24, 31);
    internal static readonly Color Raised = Color.FromArgb(34, 40, 51);
    internal static readonly Color Border = Color.FromArgb(49, 57, 71);
    internal static readonly Color Hover = Color.FromArgb(43, 51, 65);
    internal static readonly Color Selection = Color.FromArgb(36, 56, 82);
    internal static readonly Color Primary = Color.FromArgb(54, 101, 170);
    internal static readonly Color PrimaryHover = Color.FromArgb(66, 118, 194);
    internal static readonly Color Text = Color.FromArgb(227, 232, 241);
    internal static readonly Color Muted = Color.FromArgb(157, 169, 188);
    internal static readonly Color Accent = Color.FromArgb(146, 184, 239);
    internal static readonly Color Success = Color.FromArgb(123, 204, 167);
    internal static readonly Color Error = Color.FromArgb(235, 147, 155);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> styled = new();

    // This also covers runtime-created dialogs and DataGridView editing controls.
    // Hooks are installed once; no repaint timer or replacement scroll model is used.
    internal static void Apply(Control root)
    {
        if (root.IsDisposed || root.Disposing ||
            System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
            return;
        if (!styled.TryGetValue(root, out _))
        {
            styled.Add(root, new object());
            if (root is Form form)
            {
                form.HandleCreated += (_, _) => ApplyNative(form);
                if (form.IsHandleCreated) ApplyNative(form);
            }
            else if (root is TextBoxBase or ListView or ComboBox or ScrollBar ||
                     root is ScrollableControl { AutoScroll: true })
            {
                root.HandleCreated += (_, _) => ThemeScrollbars(root);
                if (root.IsHandleCreated) ThemeScrollbars(root);
            }
            if (root is TextBoxBase text)
            {
                text.ForeColor = Text;
                if (text is not RichTextBox) text.BackColor = Inset;
            }
            if (root is ComboBox combo)
            {
                combo.BackColor = Inset;
                combo.ForeColor = Text;
                combo.FlatStyle = FlatStyle.Flat;
            }
            if (root is DataGridView grid)
            {
                grid.BackgroundColor = grid.DefaultCellStyle.BackColor.IsEmpty ? Surface : grid.DefaultCellStyle.BackColor;
                grid.GridColor = Border;
                grid.BorderStyle = BorderStyle.None;
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.RowHeadersDefaultCellStyle.BackColor = Raised;
                grid.RowHeadersDefaultCellStyle.ForeColor = Muted;
                grid.EditingControlShowing += (_, e) => Apply(e.Control);
            }
            if (root is Button button)
            {
                button.FlatAppearance.MouseOverBackColor = Hover;
                button.FlatAppearance.MouseDownBackColor = Selection;
                button.FlatAppearance.BorderColor = Border;
                button.UseVisualStyleBackColor = false;
            }
            root.ControlAdded += (_, e) => { if (e.Control is { } child) Apply(child); };
        }
        foreach (Control child in root.Controls) Apply(child);
    }

    internal static void StyleToolTip(ToolTip tooltip)
    {
        tooltip.OwnerDraw = true;
        tooltip.BackColor = Raised;
        tooltip.ForeColor = Text;
        tooltip.Popup += (_, e) => e.ToolTipSize = new Size(e.ToolTipSize.Width + 12, e.ToolTipSize.Height + 8);
        tooltip.Draw += (_, e) =>
        {
            using (var brush = new SolidBrush(Raised)) e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font,
                Rectangle.Inflate(e.Bounds, -6, -4), Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        };
    }

    internal static void ApplyNative(Form form)
    {
        if (!OperatingSystem.IsWindows() || SystemInformation.HighContrast)
            return;
        int enabled = 1;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int border = ColorTranslator.ToWin32(Border);
            int caption = ColorTranslator.ToWin32(Background);
            int text = ColorTranslator.ToWin32(Text);
            _ = DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
            _ = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        }
    }

    internal static void ThemeScrollbars(Control control)
    {
        // Let WinForms choose each native control's theme when its app-wide
        // dark mode is active (combos and lists use different theme classes).
#pragma warning disable WFO5001
        if (Application.IsDarkModeEnabled) return;
#pragma warning restore WFO5001
        if (OperatingSystem.IsWindows() && !SystemInformation.HighContrast && control.IsHandleCreated)
            _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int size
    );

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string? id);
}

public sealed class WorkspaceTabs : TabControl
{
    private bool runtimeNavigation;

    public WorkspaceTabs() { }

    protected override CreateParams CreateParams
    {
        get
        {
#pragma warning disable WFO5001
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
#pragma warning restore WFO5001
            return base.CreateParams;
        }
    }

    internal void UseRuntimeNavigation()
    {
        runtimeNavigation = true;
        Appearance = TabAppearance.FlatButtons;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(1, 1);
        Multiline = true;
        TabStop = false;
        PerformLayout();
    }

    protected override void WndProc(ref Message message)
    {
        // Native tabs remain visible in Visual Studio for selecting each page.
        if (message.Msg == 0x1328 && runtimeNavigation && !DesignMode)
        {
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
    }
}

public sealed class DarkSurface : Panel
{
    public DarkSurface()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
#pragma warning disable WFO5001
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
#pragma warning restore WFO5001
            return base.CreateParams;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode && AutoScroll) AppTheme.ThemeScrollbars(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width > 0 && Height > 0)
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}

public sealed class DarkRecordingList : ListView
{
    public DarkRecordingList()
    {
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode) AppTheme.ThemeScrollbars(this);
    }
}

public sealed class DarkTranscriptGrid : DataGridView
{
    public DarkTranscriptGrid()
    {
        DoubleBuffered = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
#pragma warning disable WFO5001
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
#pragma warning restore WFO5001
            return base.CreateParams;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode) AppTheme.ThemeScrollbars(this);
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    internal DarkMenuRenderer()
        : base(new DarkMenuColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(AppTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? AppTheme.Muted : e.Item.ForeColor;
        base.OnRenderItemText(e);
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => AppTheme.Raised;
        public override Color ImageMarginGradientBegin => AppTheme.Raised;
        public override Color ImageMarginGradientMiddle => AppTheme.Raised;
        public override Color ImageMarginGradientEnd => AppTheme.Raised;
        public override Color MenuItemSelected => AppTheme.Selection;
        public override Color MenuItemSelectedGradientBegin => AppTheme.Selection;
        public override Color MenuItemSelectedGradientEnd => AppTheme.Selection;
        public override Color MenuItemBorder => AppTheme.Border;
        public override Color MenuBorder => AppTheme.Border;
        public override Color SeparatorDark => AppTheme.Border;
        public override Color SeparatorLight => AppTheme.Raised;
    }
}

#endregion
