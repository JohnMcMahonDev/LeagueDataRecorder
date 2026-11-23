using System.Diagnostics;
using System.Management;
using System.Text.Json;
using LeagueTelemetry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League_Data_Recorder;

#region Recording and session lifecycle

public partial class Form1 : Form
{
    private readonly LeagueApi _leagueApi = new LeagueApi();
    private readonly LeagueClientApi _leagueClientApi = new LeagueClientApi();

    private readonly RecordApplicationAudio _recordApplicationAudio = new RecordApplicationAudio();
    private readonly RecordApplicationVideo _recordApplicationVideo = new RecordApplicationVideo();

    private bool recordingSession = false;
    private SessionData? currentSessionData = null;
    private ChampSelectData? currentChampSelectData = null;
    private GameData? currentGameData = null;
    private PostGameData? currentPostGameData = null;

    private Task? recordingTask;
    private CancellationTokenSource? recordingCTS = new CancellationTokenSource();

    private enum PhaseState
    {
        Idle,
        ChampionSelect,
        Game,
        PostGame,
    }

    private PhaseState phaseState = PhaseState.Idle;

    private HashSet<int> recordedDraftActions = new HashSet<int>();

    private float cancelTimer = 0;

    private bool startingSession;
    private bool stoppingSession;

    private bool videoAttempted;
    private string? currentVideoPath;

    private bool savingSession;
    private CancellationTokenSource? startupCTS;
    private Task? startupTask;

    private bool allowClose;

    private string? transcribingGamePath;
    private bool openingReplay;
    private bool deletingRecording;
    private int previewRequestId;
    private int commsRequestId;

    private static readonly Color MutedText = AppTheme.Muted;
    private static readonly Color ErrorText = AppTheme.Error;
    private static readonly Color SuccessText = AppTheme.Success;

    internal sealed record ChampionInfo(string Id, string Name, string IconFileName);

    public Form1()
    {
        InitializeComponent();
        if (IsDesignerHosted) return;
        InitializeRuntimeAppearance();
        InitializeAnalysisPanel();
        InitializeAccelerationPanel();
        InitializeConsolidatedWorkspace();
        InitializeVideoReview();
        InitializeReplayAudio();
        InitializeGameSharing();
        AppTheme.Apply(this);
    }

    private bool IsDesignerHosted => DesignMode ||
        System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime ||
        Site?.DesignMode == true;

    private async void Form1_Load(object? sender, EventArgs e)
    {
        if (IsDesignerHosted) return;
        ApplySettings();
        UpdateFormControls();
        UpdateGameID();
        UpdateGamesRecorded();
        await RefreshAccelerationStatusAsync();

        try
        {
            await UpdateChampionData();
            if (!IsDisposed && !Disposing)
                RefreshSelectedDraftIcons();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
                AppendToLog($"Could not update champion icons: {ex.Message}");
        }
    }

    private void ApplySettings()
    {
        LoadAppPreferences();
        if (string.IsNullOrWhiteSpace(folderTextBox.Text) && recordingWorkspace != null)
            recordingWorkspace.SelectedTab = recordingSetupPage;
    }

    private async Task UpdateChampionData()
    {
        string catalogPath = await LeagueClientApi.CacheLatestChampionDataAsync();
        string icons = await LeagueClientApi.CacheChampionIconsAsync(catalogPath);
        if (!IsDisposed && !Disposing) SetAnalysisChampionIconSource(catalogPath, icons);
    }

    private static Dictionary<int, ChampionInfo> LoadChampionCatalog(string jsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var champions = new Dictionary<int, ChampionInfo>();

        foreach (var entry in document.RootElement.GetProperty("data").EnumerateObject())
        {
            var champion = entry.Value;
            int championId = int.Parse(champion.GetProperty("key").GetString()!);

            champions[championId] = new ChampionInfo(
                champion.GetProperty("id").GetString()!,
                champion.GetProperty("name").GetString()!,
                champion.GetProperty("image").GetProperty("full").GetString()!
            );
        }

        return champions;
    }

    private async void startRecordingButton_Click(object? sender, EventArgs e)
    {
        // Must be outside try/finally.
        if (startingSession || stoppingSession || savingSession || recordingSession)
        {
            return;
        }

        startingSession = true;
        UpdateFormControls();
        startupCTS = new CancellationTokenSource();

        try
        {
            startupTask = StartSessionCoreAsync(startupCTS.Token);
            await startupTask;
        }
        catch (OperationCanceledException) when (startupCTS.IsCancellationRequested)
        {
            AppendToLog("Session startup cancelled.");
        }
        catch (Exception ex)
        {
            AppendToLog($"Could not start session: {ex.Message}");
        }
        finally
        {
            startupTask = null;

            startupCTS.Dispose();
            startupCTS = null;

            startingSession = false;
            UpdateFormControls();
        }
    }

    private async Task StartSessionCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (recordingSession || stoppingSession)
            return;

        string? phase = await _leagueClientApi.GetGameflowPhaseAsync();

        cancellationToken.ThrowIfCancellationRequested();

        if (phase == null)
        {
            AppendToLog("League isn't open.");
            return;
        }

        phase = phase.Trim().Trim('"');

        if (
            phase
            is "ChampSelect"
                or "GameStart"
                or "InProgress"
                or "Reconnect"
                or "WaitingForStats"
                or "PreEndOfGame"
                or "EndOfGame"
        )
        {
            AppendToLog("Cannot begin recording during a game.");
            return;
        }

        if (currentSessionData != null)
        {
            AppendToLog("Previous session has not been saved. Click Stop to retry saving.");
            return;
        }

        if (!ValidateRecordingDir())
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        ResetGameState();
        phaseState = PhaseState.Idle;
        recordedDraftActions.Clear();

        currentSessionData = new SessionData(allyTeamTextBox.Text, enemyTeamTextBox.Text)
        { gameType = GameTypes.Normalize(sessionGameTypeCombo.Text) };

        recordingCTS?.Dispose();
        recordingCTS = new CancellationTokenSource();

        recordingSession = true;

        UpdateStatusLabel();
        UpdateGamesRecorded();

        AppendToLog("Beginning recording.");

        recordingTask = BeginRecording(recordingCTS.Token);
    }

    private async void stopRecordingButton_Click(object? sender, EventArgs e)
    {
        if (stoppingSession || savingSession)
            return;

        if (
            recordingSession
            && MessageBox.Show(
                this,
                "Stop the current recording session?",
                "Stop Recording",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2
            ) != DialogResult.OK
        )
        {
            return;
        }

        try
        {
            await StopSession();
        }
        catch (Exception ex)
        {
            AppendToLog($"Could not stop session: {ex.Message}");
        }
    }

    private async Task StopSession()
    {
        if (stoppingSession || savingSession)
            return;

        // A session can be stopped before its first completed game, and startup
        // can be cancelled before a SessionData instance has been created.
        if (!startingSession && startupTask == null && !recordingSession &&
            recordingTask == null && currentSessionData == null)
            return;

        stoppingSession = true;
        UpdateFormControls();

        try
        {
            // Prevent startup from committing after Stop was clicked.
            startupCTS?.Cancel();

            Task? pendingStartup = startupTask;

            if (pendingStartup != null)
            {
                try
                {
                    await pendingStartup;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    AppendToLog($"Startup ended with an error: {ex.Message}");
                }
            }

            recordingCTS?.Cancel();

            Task? pendingRecording = recordingTask;

            if (pendingRecording != null)
                await pendingRecording;

            recordingSession = false;

            if (currentSessionData == null)
            {
                AppendToLog("Session stopped.");
                return;
            }

            if (currentSessionData.endTime == DateTime.MaxValue)
                currentSessionData.CompleteSession();

            savingSession = true;
            UpdateFormControls();
            AppendToLog("Recording stopped. Saving.");

            bool hasGames = currentSessionData.gameCount > 0;
            await SaveSessionData();

            // Only clear after saving succeeds.
            currentSessionData = null;

            UpdateStatusLabel();
            UpdateGamesRecorded();

            AppendToLog(hasGames ? "Session saved." : "Session ended without games; nothing to save.");
        }
        finally
        {
            recordingTask = null;

            recordingCTS?.Dispose();
            recordingCTS = null;

            savingSession = false;
            stoppingSession = false;
            UpdateFormControls();
        }
    }

    private async Task BeginRecording(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(
                TimeSpan.FromMilliseconds((double)pollingIntervalInput.Value)
            );

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Poll();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when stopping.
        }
        catch (Exception ex)
        {
            AppendToLog($"Polling failed: {ex}");
        }
        finally
        {
            await CleanupSessionAsync();
        }
    }

    private async Task CleanupSessionAsync()
    {
        try
        {
            // Attempt both recorders even if one fails.
            try
            {
                await TryAudioOperationAsync(CancelRecordingAudio, "Could not cancel audio");
            }
            catch (Exception ex)
            {
                AppendToLog($"Could not finalize audio: {ex.Message}");
            }

            await CancelRecordingVideo();

            DeleteCurrentGameMedia();
            ResetGameState();
            phaseState = PhaseState.Idle;
        }
        finally
        {
            if (currentSessionData != null && currentSessionData.endTime == DateTime.MaxValue)
            {
                currentSessionData.CompleteSession();
            }

            recordingSession = false;
            UpdateFormControls();

            UpdateStatusLabel();
            UpdateGamesRecorded();
        }
    }

    private static IntPtr GetLeagueGameWindow()
    {
        foreach (Process process in Process.GetProcessesByName("League of Legends"))
        {
            using (process)
            {
                try
                {
                    process.Refresh();

                    IntPtr handle = process.MainWindowHandle;

                    if (handle != IntPtr.Zero)
                        return handle;
                }
                catch (InvalidOperationException)
                {
                    // Process exited while being inspected.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Process could not be inspected.
                }
            }
        }

        return IntPtr.Zero;
    }

    private async Task TryAudioOperationAsync(Func<Task> operation, string description)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            AppendToLog($"{description}: {ex.Message}");
        }
    }

    private async Task StartRecordingAudio()
    {
        string recordingDir = GetRecordingDir();
        if (string.IsNullOrEmpty(recordingDir))
        {
            AppendToLog("No valid session directory found.");
            return;
        }

        if (recordAudioCheckbox.Checked)
        {
            int? discordId = await Task.Run(FindDiscordRootProcessId);

            if (discordId == null)
            {
                AppendToLog("Discord not found.");
                return;
            }

            string audioPath = Path.Combine(recordingDir, "Current_Game_Audio.wav");
            await _recordApplicationAudio.StartAsyncAudioRecording(
                IntPtr.Zero,
                (int)discordId,
                audioPath
            );

            AppendToLog("Started Recording Audio");
        }
    }

    private void StartRecordingVideo()
    {
        if (!recordVideoCheckbox.Checked || videoAttempted || currentGameData == null)
        {
            return;
        }

        IntPtr gameWindow = GetLeagueGameWindow();

        // GameStart can occur before the window is ready.
        // Leave videoAttempted false so the next poll retries.
        if (gameWindow == IntPtr.Zero)
            return;

        videoAttempted = true;

        try
        {
            string recordingDir = GetRecordingDir();
            if (string.IsNullOrEmpty(recordingDir))
            {
                throw new DirectoryNotFoundException();
            }

            // Never reuse a path that an earlier failed encoder might still own.
            string videoPath = Path.Combine(recordingDir, $"Current_Game_Video_{Guid.NewGuid():N}.mp4");
            currentVideoPath = videoPath;

            _recordApplicationVideo.StartVideoRecording(gameWindow, videoPath);

            _ = ObserveVideoResultAsync(_recordApplicationVideo.Completion!);

            AppendToLog("Video capture requested (software H.264 encoding).");
        }
        catch (Exception ex)
        {
            AppendToLog($"Could not start video: {ex.Message}");
            // Keep the attempt recorded: a persistent encoder error must not
            // create a new recorder on every poll. Audio/game capture continues.
        }
    }

    private async Task ObserveVideoResultAsync(Task<(string? FilePath, string? Error)> completion)
    {
        var result = await completion;

        if (!IsDisposed && !Disposing && result.Error != null)
            AppendToLog($"Video recording failed: {result.Error}");
    }

    private async Task FinishRecordingAudio()
    {
        await _recordApplicationAudio.StopAsyncAudioRecording();

        string recordingDir = GetRecordingDir();
        if (string.IsNullOrEmpty(recordingDir))
        {
            AppendToLog("No valid recording directory found.");
            return;
        }

        string audioPath = Path.Combine(recordingDir, "Current_Game_Audio.wav");

        if (currentGameData == null || currentGameData.gameId == null)
        {
            return;
        }

        string gamePath = GetGamePath((long)currentGameData.gameId);

        if (File.Exists(audioPath))
        {
            File.Move(audioPath, $"{gamePath}_Audio.wav");
        }
    }

    private async Task FinishRecordingVideo()
    {
        string? completedVideoPath = null;
        try
        {
            string? videoPath = await _recordApplicationVideo.StopVideoRecordingAsync();

            if (string.IsNullOrWhiteSpace(videoPath))
                return;

            completedVideoPath = videoPath;
            // A completed video is no longer disposable temporary capture data.
            // If moving it fails (or no game ID exists), preserve it at this path
            // instead of letting ResetGameState delete it.
            if (string.Equals(currentVideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
                currentVideoPath = null;

            if (currentGameData?.gameId is not long gameId)
            {
                AppendToLog($"Video preserved without a game ID: {videoPath}");
                return;
            }

            string destination = $"{GetGamePath(gameId)}_Video.mp4";

            File.Move(videoPath, destination);
            completedVideoPath = destination;

            AppendToLog($"Video saved: {destination}");
            await _recordApplicationVideo.ReadSavedVideoTimingAsync(destination);
        }
        catch (Exception ex)
        {
            if (completedVideoPath is not null && File.Exists(completedVideoPath))
                AppendToLog($"Completed video preserved at {completedVideoPath}. Could not finish saving it: {ex.Message}");
            else
                AppendToLog($"Could not finalize video: {ex.Message}");
        }
    }

    private async Task CancelRecordingAudio()
    {
        await _recordApplicationAudio.StopAsyncAudioRecording();

        string recordingDir = GetRecordingDir();
        if (string.IsNullOrEmpty(recordingDir))
        {
            AppendToLog("No valid recording directory found.");
            return;
        }

        string audioPath = Path.Combine(recordingDir, "Current_Game_Audio.wav");

        if (File.Exists(audioPath))
        {
            File.Delete(audioPath);
        }
    }

    private async Task CancelRecordingVideo()
    {
        try
        {
            string? videoPath = await _recordApplicationVideo.StopVideoRecordingAsync();

            if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                File.Delete(videoPath);
        }
        catch (Exception ex)
        {
            AppendToLog($"Could not cancel video: {ex.Message}");
        }
    }

    private void DeleteCurrentGameMedia()
    {
        if (currentGameData?.gameId is not long gameId)
            return;

        try
        {
            string gamePath = GetGamePath(gameId, createDirectory: false);

            foreach (string path in new[] { $"{gamePath}_Audio.wav", $"{gamePath}_Video.mp4" })
                if (File.Exists(path))
                    File.Delete(path);
        }
        catch (Exception ex)
        {
            AppendToLog($"Could not delete media: {ex.Message}");
        }
    }

    private async Task Poll()
    {
        double phaseRequestStarted = MediaClock.NowSeconds;
        string? phase = await _leagueClientApi.GetGameflowPhaseAsync();
        double phaseObservedClockSeconds = (phaseRequestStarted + MediaClock.NowSeconds) / 2.0;

        if (phase == null)
        {
            AppendToLog("Could not determine gameflow phase.");

            await CheckPhaseDesync();
            UpdateStatusLabel();
            return;
        }
        phase = phase.Trim().Trim('"');

        switch (phase)
        {
            case "ChampSelect":
                if (phaseState is PhaseState.Game or PhaseState.PostGame)
                {
                    await CancelGame();
                }

                if (phaseState != PhaseState.ChampionSelect)
                {
                    BeginChampionSelect();
                }

                string? draftData = await _leagueClientApi.GetChampionSelectSessionAsync();
                if (draftData == null)
                {
                    AppendToLog("Invalid draft data.");
                    break;
                }

                PollChampionSelect(draftData);

                break;
            case "GameStart":
                await TransitionToGame();

                break;
            case "InProgress":
                await TransitionToGame();
                StartRecordingVideo();

                // Retrieve the ID independently of live-data success.
                if (currentGameData != null && currentGameData.gameId == null)
                {
                    long? gameId = await _leagueClientApi.GetCurrentGameIdAsync();

                    if (gameId is > 0)
                    {
                        currentGameData.gameId = gameId;
                        UpdateGameID();
                    }
                }

                double liveRequestStarted = MediaClock.NowSeconds;
                string? gameData = await _leagueApi.GetAllGameDataAsync();
                double liveRequestFinished = MediaClock.NowSeconds;

                if (gameData != null)
                {
                    try
                    {
                        currentGameData?.mediaTiming.ObserveLiveFrame(
                            gameData,
                            liveRequestStarted,
                            liveRequestFinished
                        );
                    }
                    catch (Exception ex)
                    {
                        AppendToLog($"Could not read media timing: {ex.Message}");
                    }
                    PollGameData(gameData);
                }

                break;
            case "PreEndOfGame":
                await TransitionToPostGame(phaseObservedClockSeconds);

                break;
            case "WaitingForStats":
                await TransitionToPostGame(phaseObservedClockSeconds);

                break;
            case "EndOfGame":
                await TransitionToPostGame(phaseObservedClockSeconds);

                break;
            default:
                if (phaseState == PhaseState.ChampionSelect)
                {
                    CancelChampSelect();
                }

                await CheckPhaseDesync();

                break;
        }

        UpdateStatusLabel();
    }

    private async Task CheckPhaseDesync()
    {
        if (phaseState == PhaseState.Game || phaseState == PhaseState.PostGame)
        {
            int minimumCancelTime = (int)gameExpireTimeInput.Value;
            int pollingInterval = (int)pollingIntervalInput.Value;

            cancelTimer += pollingInterval / 1000f;

            AppendToLog(
                $"Game phase desync detected. Cancelling game "
                    + $"in {minimumCancelTime - cancelTimer} seconds."
            );

            if (cancelTimer >= minimumCancelTime)
            {
                await CancelGame();
            }
        }
    }

    private void PollChampionSelect(string rawData)
    {
        if (currentChampSelectData == null)
        {
            AppendToLog("No champ select data found.");
            return;
        }

        try
        {
            JObject json = JObject.Parse(rawData);

            JToken? actions = json["actions"];

            if (actions == null)
            {
                AppendToLog("No draft actions found.");
                return;
            }

            foreach (JArray actionGroup in actions)
            {
                foreach (JToken action in actionGroup)
                {
                    bool completed = (bool)action["completed"]!;

                    if (!completed)
                    {
                        continue;
                    }

                    int actionId = (int)action["id"]!;

                    int championId = (int)action["championId"]!;
                    if (championId <= 0)
                    {
                        continue;
                    }

                    bool isAlly = (bool)action["isAllyAction"]!;

                    string type = (string)action["type"]!;

                    if (recordedDraftActions.Add(actionId))
                    {
                        AppendToLog(
                            $"New draft action detected. {type}: {championId}, Ally: {isAlly}"
                        );
                        currentChampSelectData.AddDraftAction(type, championId, isAlly);
                    }
                }
            }
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            AppendToLog($"Malformed champion select JSON: {ex.Message}");
        }
    }

    private void PollGameData(string rawData)
    {
        if (currentGameData == null)
        {
            AppendToLog("No game data found.");
            return;
        }

        cancelTimer = 0;

        currentGameData.frames.Add(rawData);
    }

    private async Task PollPostGame()
    {
        if (phaseState == PhaseState.PostGame)
        {
            string? endOfGameData = await _leagueClientApi.GetPostGameStatsAsync();
            if (endOfGameData == null)
            {
                return;
            }

            if (currentPostGameData == null)
            {
                AppendToLog("No post game data found.");
                return;
            }

            cancelTimer = 0;

            if (string.IsNullOrEmpty(endOfGameData) == false)
            {
                currentPostGameData.postGameData = endOfGameData;

                await FinalizeGameAsync();
            }
        }
    }

    private async Task TransitionToGame()
    {
        if (phaseState == PhaseState.ChampionSelect)
        {
            EndChampionSelect();

            await BeginGame();
        }
    }

    private async Task TransitionToPostGame(double phaseObservedClockSeconds)
    {
        if (phaseState == PhaseState.Game)
        {
            currentGameData?.mediaTiming.MarkGameEnded(phaseObservedClockSeconds);
            Task mediaTask = EndGame();

            string? capturedStats = null;

            do
            {
                capturedStats = await _leagueClientApi.GetPostGameStatsAsync();

                if (!string.IsNullOrEmpty(capturedStats))
                    break;

                if (mediaTask.IsCompleted)
                    break;

                // Retry while media is still finishing.
                await Task.WhenAny(mediaTask, Task.Delay(1000));
            } while (!mediaTask.IsCompleted);

            // Keep current game state until media has finished.
            await mediaTask;

            BeginPostGame();

            if (!string.IsNullOrEmpty(capturedStats) && currentPostGameData != null)
            {
                currentPostGameData.postGameData = capturedStats;
                await FinalizeGameAsync();
                return;
            }
        }

        // Continue normal polling if stats weren't available yet.
        if (phaseState == PhaseState.PostGame)
            await PollPostGame();
    }

    private void BeginChampionSelect()
    {
        AppendToLog("Champion Select Beginning");
        phaseState = PhaseState.ChampionSelect;

        currentChampSelectData = new ChampSelectData();

        recordedDraftActions = new HashSet<int>();
    }

    private void EndChampionSelect()
    {
        if (currentChampSelectData == null)
        {
            AppendToLog("No valid champ select data.");
            return;
        }

        AppendToLog("Champion Select Ended");

        currentChampSelectData.endTime = DateTime.Now;
    }

    private void CancelChampSelect()
    {
        AppendToLog("Champ Select Cancelled");
        phaseState = PhaseState.Idle;

        ResetGameState();
    }

    private async Task BeginGame()
    {
        videoAttempted = false;

        AppendToLog("Game Beginning");

        currentGameData = new GameData();

        Task audioStart = TryAudioOperationAsync(StartRecordingAudio, "Could not start audio");
        StartRecordingVideo();
        await audioStart;

        phaseState = PhaseState.Game;

        cancelTimer = 0;
    }

    private async Task EndGame()
    {
        AppendToLog("Game Ended");

        cancelTimer = 0;

        // Request both stops now. Waiting for audio disposal before stopping video
        // used to append seconds of frozen/end-screen footage to the MP4.
        await Task.WhenAll(
            TryAudioOperationAsync(FinishRecordingAudio, "Could not finalize audio"),
            FinishRecordingVideo());

        if (currentGameData == null)
        {
            AppendToLog("No valid game data.");
            return;
        }

        currentGameData.mediaTiming.AudioFileStartClockSeconds =
            _recordApplicationAudio.FileStartClockSeconds;
        currentGameData.mediaTiming.VideoFileStartClockSeconds =
            videoAttempted ? _recordApplicationVideo.FileStartClockSeconds : null;
        if (videoAttempted)
        {
            var timing = currentGameData.mediaTiming;
            timing.VideoTimingSource = _recordApplicationVideo.TimingSource;
            timing.VideoTimingError = _recordApplicationVideo.TimingError;
            timing.VideoFirstFrameReadyClockSeconds = _recordApplicationVideo.FirstFrameReadyClockSeconds;
            timing.VideoFirstFrameEndSeconds = _recordApplicationVideo.FirstFrameEndSeconds;
            timing.VideoDurationSeconds = _recordApplicationVideo.DurationSeconds;
            timing.VideoFrameCount = _recordApplicationVideo.FrameCount;
            timing.VideoMaximumFrameCallbackGapSeconds = _recordApplicationVideo.MaximumFrameCallbackGapSeconds;
            if (timing.VideoTimingError != null)
                AppendToLog("Video capture/timing unavailable: " + timing.VideoTimingError);
            if (timing.VideoMaximumFrameCallbackGapSeconds > 0.5)
                AppendToLog($"Video capture had a {timing.VideoMaximumFrameCallbackGapSeconds:F2}s frame delivery gap (details in MediaTiming.json).");
        }
        currentGameData.mediaTiming.AudioCaptureError = _recordApplicationAudio.CaptureError;
        currentGameData.endTime = DateTime.Now;
    }

    private void BeginPostGame()
    {
        AppendToLog("Post Game Beginning");

        currentPostGameData = new PostGameData();

        phaseState = PhaseState.PostGame;

        cancelTimer = 0;
    }

    private async Task FinalizeGameAsync()
    {
        var session = currentSessionData;
        var gameData = currentGameData;
        var champSelectData = currentChampSelectData;
        var postGameData = currentPostGameData;

        if (
            session == null
            || gameData == null
            || gameData.gameId is not long gameId
            || champSelectData == null
            || postGameData == null
        )
        {
            AppendToLog("Could not save game because required data is missing.");
            await CancelGame();
            return;
        }

        session.AddGame(gameData, champSelectData, postGameData);
        phaseState = PhaseState.Idle;
        ResetGameState();
        UpdateGamesRecorded();

        try
        {
            await SaveGameDataAsync(gameId, gameData, champSelectData, postGameData);
        }
        catch (Exception ex)
        {
            AppendToLog($"Could not save game {gameId}: {ex.Message}");
        }
    }

    private async Task CancelGame()
    {
        AppendToLog("Game Cancelled");
        phaseState = PhaseState.Idle;

        await TryAudioOperationAsync(CancelRecordingAudio, "Could not cancel audio");

        await CancelRecordingVideo();

        DeleteCurrentGameMedia();
        ResetGameState();
    }

    private void ResetGameState()
    {
        currentChampSelectData = null;
        currentGameData = null;
        currentPostGameData = null;

        cancelTimer = 0;

        //Reset game id
        UpdateGameID();

        string recordingDir = GetRecordingDir();

        if (string.IsNullOrEmpty(recordingDir))
        {
            return;
        }

        RecordingFileCleanup.TryDelete(Path.Combine(recordingDir, "Current_Game_Audio.wav"), AppendToLog);
        RecordingFileCleanup.TryDelete(currentVideoPath, AppendToLog);
        currentVideoPath = null;
        // Also tolerate a locked file left by the previous application version.
        RecordingFileCleanup.TryDelete(Path.Combine(recordingDir, "Current_Game_Video.mp4"), AppendToLog);
    }

    private void UpdateStatusLabel()
    {
        if (stoppingSession || savingSession)
        {
            statusLabel.Text = savingSession ? "Saving session…" : "Finishing session…";
            statusLabel.ForeColor = Color.FromArgb(255, 208, 137);
            return;
        }

        if (recordingSession)
        {
            statusLabel.Text = phaseState switch
            {
                PhaseState.ChampionSelect => "Champion select",
                PhaseState.Game => "Match in progress",
                PhaseState.PostGame => "Saving match…",
                _ => "Waiting for draft",
            };
            statusLabel.ForeColor = SuccessText;
            return;
        }

        statusLabel.Text =
            startingSession ? "Connecting…"
            : currentSessionData != null ? "Session unsaved"
            : "Ready";
        statusLabel.ForeColor = Color.FromArgb(229, 235, 246);
    }

    private void UpdateGameID()
    {
        gameIdLabel.Text = currentGameData?.gameId?.ToString() ?? "—";
    }

    private void UpdateGamesRecorded()
    {
        gamesRecordedLabel.Text = (currentSessionData?.gameCount ?? 0).ToString();
    }

    private void AppendToLog(string message)
    {
        if (liveDataLog.InvokeRequired)
        {
            liveDataLog.Invoke(() => AppendToLog(message));
            return;
        }

        liveDataLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

        liveDataLog.ScrollToCaret();
    }

    private string GetRecordingDir(string subFolder = "")
    {
        return Path.Combine(folderTextBox.Text, subFolder);
    }

    private bool ValidateRecordingDir()
    {
        //Get valid folder directory
        string dir = folderTextBox.Text;
        if (Directory.Exists(dir) == false)
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog();

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                AppendToLog("No directory provided.");
                return false;
            }

            dir = dialog.SelectedPath;
            folderTextBox.Text = dir;
        }

        Directory.CreateDirectory(Path.Combine(dir, "Sessions"));
        Directory.CreateDirectory(Path.Combine(dir, "Recordings"));
        Directory.CreateDirectory(Path.Combine(dir, "Transcriptions"));
        Directory.CreateDirectory(Path.Combine(dir, "Analysis"));

        return true;
    }

    private string GetSessionPath()
    {
        if (currentSessionData == null)
        {
            return "";
        }

        return $"Session_{currentSessionData.sessionId}";
    }

    private string GetGamePath(long gameId, bool createDirectory = true)
    {
        var session =
            currentSessionData ?? throw new InvalidOperationException("No active session.");

        string gameDir = Path.Combine(
            folderTextBox.Text,
            "Recordings",
            $"Game_{gameId}_{session.sessionId}"
        );

        if (createDirectory)
            Directory.CreateDirectory(gameDir);

        return Path.Combine(gameDir, $"Game_{gameId}");
    }

    private static bool HasSavedGameData(string draftPath, string gamePath)
    {
        return File.Exists(draftPath)
            && File.Exists($"{gamePath}_ParsedGameData.json")
            && File.Exists($"{gamePath}_ParsedPostGameData.json");
    }

    private async Task SaveGameDataAsync(
        long gameId,
        GameData gameData,
        ChampSelectData champSelectData,
        PostGameData postGameData
    )
    {
        string gamePath = GetGamePath(gameId);
        string draftPath = $"{gamePath}_DraftData.json";

        if (HasSavedGameData(draftPath, gamePath))
        {
            await MatchTimelineStore.EnsureAsync(_leagueClientApi, gamePath, gameId, LogMatchHistory);
            return;
        }

        AppendToLog($"Saving game {gameId}.");
        BackfillEnemyPicks(gameData, champSelectData);

        if (saveRawDataCheckbox.Checked)
        {
            await File.WriteAllTextAsync(
                $"{gamePath}_GameData.json",
                JsonConvert.SerializeObject(gameData, Formatting.Indented)
            );
            await File.WriteAllTextAsync(
                $"{gamePath}_PostGameData.json",
                JsonConvert.SerializeObject(postGameData, Formatting.Indented)
            );
        }

        await File.WriteAllTextAsync(
            $"{gamePath}_ParsedGameData.json",
            DataParseHelper.ParseGameData(gameData)
        );
        await File.WriteAllTextAsync(
            $"{gamePath}_ParsedPostGameData.json",
            DataParseHelper.ParsePostGameData(postGameData, currentSessionData?.allyTeam, currentSessionData?.enemyTeam)
        );

        await MatchTimelineStore.EnsureAsync(_leagueClientApi, gamePath, gameId, LogMatchHistory);

        // Persist properties with the recording too, so moving a game keeps its type.
        if (RecordingTeamNames.ReadOverride(gamePath) is null && currentSessionData is { } session)
            new RecordingTeamNames { Ally = session.allyTeam, Enemy = session.enemyTeam,
                GameType = session.gameType }.Save(gamePath, requireNames: false);

        await TrimSavedGameMediaAsync(gamePath, gameData, postGameData);

        if (saveReplayCheckbox.Checked)
        {
            string gameDir = Path.GetDirectoryName(gamePath)!;
            string? replay = await _leagueClientApi.DownloadReplayAsync(gameId, gameDir);

            if (string.IsNullOrEmpty(replay))
                AppendToLog($"Could not download the replay for game {gameId}.");
        }

        // History uses the draft file to identify a completed save.
        string tempDraftPath = $"{draftPath}.tmp";
        await File.WriteAllTextAsync(
            tempDraftPath,
            JsonConvert.SerializeObject(champSelectData, Formatting.Indented)
        );
        File.Move(tempDraftPath, draftPath, overwrite: true);

        AppendToLog($"Game {gameId} saved.");
    }

    private async Task SaveSessionData()
    {
        if (currentSessionData == null)
        {
            AppendToLog("No active session.");
            return;
        }

        if (currentSessionData.gameCount <= 0) return;
        string sessionPath = GetSessionPath();

        string writePath = Path.Combine(GetRecordingDir("Sessions"), $"{sessionPath}_Data.json");

        await File.WriteAllTextAsync(
            writePath,
            JsonConvert.SerializeObject(currentSessionData, Formatting.Indented)
        );
    }

    private async Task TrimSavedGameMediaAsync(
        string gamePath,
        GameData gameData,
        PostGameData postGameData
    )
    {
        GameMediaTiming timing = gameData.mediaTiming;
        timing.ObservePostGame(postGameData.postGameData ?? "");

        string audioPath = $"{gamePath}_Audio.wav";
        string videoPath = $"{gamePath}_Video.mp4";

        if (File.Exists(audioPath) && File.Exists(videoPath) && !timing.AudioTrimmed)
        {
            try
            {
                if (timing.AudioCaptureError != null)
                    throw new InvalidOperationException(timing.AudioCaptureError);
                if (timing.VideoTimingError != null)
                    throw new InvalidOperationException(timing.VideoTimingError);
                if (timing.AudioFileStartClockSeconds is not double audioStart ||
                    timing.VideoFileStartClockSeconds is not double videoStart ||
                    !double.IsFinite(audioStart) || !double.IsFinite(videoStart))
                    throw new InvalidOperationException("Missing verified media capture timestamps.");

                // Read only headers; the video is never trimmed, shifted, or re-encoded.
                Mp4VideoTimeline video = await Task.Run(() => Mp4VideoTimeline.Read(videoPath));
                double offset = audioStart - videoStart;
                AudioAlignmentResult result = await MediaTrimmer.AlignAudioToVideoInPlaceAsync(
                    audioPath, offset, video.DurationSeconds);
                timing.AudioTrimmed = true;
                timing.AudioTrimError = null;
                timing.AlignedAudioFileStartClockSeconds = videoStart;
                timing.AudioAlignmentOffsetSeconds = offset;
                timing.AudioAlignmentOriginalPath = Path.GetFileName(result.OriginalAudioPath);
                timing.AudioAlignmentMethod = "PCM shift/pad to unchanged MP4; no resampling or speed adjustment";
                AppendToLog($"Audio matched to video ({video.DurationSeconds:F3}s; " +
                    $"{(offset >= 0 ? "delay" : "advance")} {Math.Abs(offset):F3}s). Original WAV kept.");
            }
            catch (Exception ex)
            {
                timing.AudioTrimError = ex.Message;
                AppendToLog($"Could not match audio to video: {ex.Message}");
            }
        }

        await File.WriteAllTextAsync(
            $"{gamePath}_MediaTiming.json",
            JsonConvert.SerializeObject(timing, Formatting.Indented)
        );
    }

    private void UpdateFormControls()
    {
        bool canEdit =
            !deletingRecording
            && !editingSpeakerRoles
            && !startingSession
            && !recordingSession
            && !stoppingSession
            && !savingSession
            && currentSessionData == null;

        folderTextBox.Enabled = canEdit;
        browseFolderButton.Enabled = canEdit;
        pollingIntervalInput.Enabled = canEdit;
        gameExpireTimeInput.Enabled = canEdit;
        allyTeamTextBox.Enabled = enemyTeamTextBox.Enabled = canEdit;
        sessionGameTypeCombo.Enabled = canEdit;
        recordVideoCheckbox.Enabled = recordAudioCheckbox.Enabled = canEdit;
        saveReplayCheckbox.Enabled = saveRawDataCheckbox.Enabled = canEdit;
        SetPrimaryActionEnabled(startRecordingButton, canEdit);
        stopRecordingButton.Enabled =
            !stoppingSession
            && !savingSession
            && (startingSession || recordingSession || currentSessionData != null);
        stopRecordingButton.Text =
            savingSession ? "Saving…"
            : stoppingSession ? "Stopping…"
            : startingSession && !recordingSession ? "Cancel Start"
            : !recordingSession && currentSessionData != null ? "Retry Save"
            : "Stop Recording";

        UpdateStatusLabel();
        UpdateHistoryActions();
        UpdateWorkspaceRecordingAction();
    }

    private static void SetPrimaryActionEnabled(Button button, bool enabled)
    {
        button.Enabled = enabled;
        button.BackColor = enabled ? AppTheme.Primary : AppTheme.Inset;
        button.ForeColor = enabled ? Color.White : MutedText;
        button.FlatAppearance.BorderColor = enabled ? AppTheme.Primary : AppTheme.Border;
    }

    private void browseFolderButton_Click(object? sender, EventArgs e)
    {
        //Get valid folder directory
        using FolderBrowserDialog dialog = new FolderBrowserDialog();

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            AppendToLog("No Directory Provided.");
            return;
        }

        folderTextBox.Text = dialog.SelectedPath;

        SaveAppPreferences();

        ValidateRecordingDir();
    }

    private static int? FindDiscordRootProcessId()
    {
        var parents = new Dictionary<int, int>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId FROM Win32_Process " + "WHERE Name = 'Discord.exe'"
        );

        using var results = searcher.Get();

        foreach (ManagementObject process in results)
        {
            using (process)
            {
                int id = checked((int)(uint)process["ProcessId"]);
                int parent = checked((int)(uint)process["ParentProcessId"]);

                parents[id] = parent;
            }
        }

        int[] roots = parents
            .Where(p => !parents.ContainsKey(p.Value))
            .Select(p => p.Key)
            .ToArray();

        return roots.Length switch
        {
            0 => null,
            1 => roots[0],
            _ => throw new InvalidOperationException("Multiple Discord process trees were found."),
        };
    }

    private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (IsDesignerHosted) return;
        SaveAppPreferences();
        if (allowClose)
            return;

        e.Cancel = true;

        // Keep the window open if Stop is already running.
        if (stoppingSession || editingSpeakerRoles)
            return;

        try
        {
            await StopSession();
            await StopBackgroundServicesAsync();

            allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save. The application will stay open.\n{ex.Message}");
        }
    }

    #region Draft capture helpers

    private static string? FindCachedChampionCatalog()
    {
        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "League Data Recorder",
            "DataDragon"
        );

        if (!Directory.Exists(cacheRoot))
            return null;

        return Directory
            .EnumerateDirectories(cacheRoot)
            .Where(folder =>
                Version.TryParse(Path.GetFileName(folder), out _)
                && File.Exists(Path.Combine(folder, "champion.json"))
            )
            .OrderByDescending(folder => Version.Parse(Path.GetFileName(folder)))
            .Select(folder => Path.Combine(folder, "champion.json"))
            .FirstOrDefault();
    }

    private void BackfillEnemyPicks(GameData gameData, ChampSelectData draft)
    {
        if (draft.enemyPicks.Count >= BlindPickDraft.TeamSize || gameData.frames.Count == 0)
            return;

        try
        {
            string? catalogPath = FindCachedChampionCatalog();
            if (catalogPath == null)
            {
                AppendToLog(
                    "Could not recover enemy picks because the champion catalog is unavailable."
                );
                return;
            }

            int added = BlindPickDraft.FillMissingEnemyPicks(
                draft.enemyPicks,
                gameData.frames,
                LoadChampionCatalog(catalogPath)
            );

            if (added > 0)
                AppendToLog($"Recovered {added} missing enemy picks from the in-game roster.");
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or System.Text.Json.JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
                        or OverflowException
                        or ArgumentNullException
            )
        {
            AppendToLog($"Could not recover enemy picks: {ex.Message}");
        }
    }
}

internal static class BlindPickDraft
{
    public const int TeamSize = 5;
    private const string ChampionNamePrefix = "game_character_displayname_";

    public static int FillMissingEnemyPicks(
        List<int> enemyPicks,
        IReadOnlyList<string> frames,
        IReadOnlyDictionary<int, Form1.ChampionInfo> champions
    )
    {
        if (enemyPicks.Count >= TeamSize)
            return 0;

        var championIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, champion) in champions)
        {
            championIds.TryAdd(champion.Id, id);
            championIds.TryAdd(champion.Name, id);
        }

        // Read the latest usable roster. The live roster includes both sides in Blind Pick.
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            List<int>? roster = ReadEnemyRoster(frames[i], championIds);
            if (roster == null || roster.Count <= enemyPicks.Count)
                continue;

            // Match each recorded pick once so existing draft order and mirror picks are preserved.
            if (!enemyPicks.All(roster.Remove))
                continue;

            enemyPicks.AddRange(roster);
            return roster.Count;
        }

        return 0;
    }

    private static List<int>? ReadEnemyRoster(string frame, Dictionary<string, int> championIds)
    {
        JObject data;
        try
        {
            data = JObject.Parse(frame);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }

        if (
            data["activePlayer"] is not JObject activePlayer
            || data["allPlayers"] is not JArray players
        )
            return null;

        string? localId = PlayerId(activePlayer);
        if (string.IsNullOrWhiteSpace(localId))
            return null;

        var playerObjects = players.OfType<JObject>().ToArray();
        var localTeams = playerObjects
            .Where(player =>
                string.Equals(PlayerId(player), localId, StringComparison.OrdinalIgnoreCase)
            )
            .Select(player => Text(player["team"]))
            .Distinct()
            .ToArray();
        if (localTeams.Length != 1)
            return null;

        string? enemyTeam = localTeams[0] switch
        {
            "ORDER" => "CHAOS",
            "CHAOS" => "ORDER",
            _ => null,
        };
        if (enemyTeam == null)
            return null;

        var roster = new List<int>(TeamSize);
        foreach (JObject player in playerObjects.Where(player => Text(player["team"]) == enemyTeam))
        {
            string? rawName = Text(player["rawChampionName"]);
            string championKey =
                rawName?.StartsWith(ChampionNamePrefix, StringComparison.OrdinalIgnoreCase) == true
                    ? rawName[ChampionNamePrefix.Length..]
                    : rawName ?? "";
            string displayName = Text(player["championName"]) ?? "";

            if (
                championIds.TryGetValue(championKey, out int id)
                || championIds.TryGetValue(displayName, out id)
            )
                roster.Add(id);
        }

        return roster.Count <= TeamSize ? roster : null;
    }

    private static string? PlayerId(JObject player) =>
        Text(player["riotId"]) ?? Text(player["summonerName"]);

    private static string? Text(JToken? token) =>
        token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)token)
            ? (string?)token
            : null;

    #endregion
}

#endregion
