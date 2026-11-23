using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League_Data_Recorder;

#region Sessions and recordings

internal class SessionData
{
    public string sessionId;

    public DateTime startTime;
    public DateTime endTime;
    public int gameCount;

    [JsonIgnore]
    public List<GameRecord> gameRecords = new List<GameRecord>();

    public string allyTeam;
    public string enemyTeam;
    public string gameType = GameTypes.Unspecified;

    public SessionData(string allyTeam, string enemyTeam)
    {
        sessionId = Guid.NewGuid().ToString();

        startTime = DateTime.Now;
        endTime = DateTime.MaxValue;
        gameCount = 0;

        gameRecords = new List<GameRecord>();

        this.allyTeam = allyTeam;
        this.enemyTeam = enemyTeam;
    }

    public void AddGame(
        GameData gameData,
        ChampSelectData champSelectData,
        PostGameData postGameData
    )
    {
        gameCount += 1;

        gameRecords.Add(new GameRecord(gameData, champSelectData, postGameData));
    }

    public void CompleteSession()
    {
        endTime = DateTime.Now;
    }
}

internal class GameRecord
{
    public ChampSelectData champSelectData;
    public GameData gameData;
    public PostGameData postGameData;

    public GameRecord(GameData gameData, ChampSelectData champSelectData, PostGameData postGameData)
    {
        this.gameData = gameData;
        this.champSelectData = champSelectData;
        this.postGameData = postGameData;
    }
}

#endregion

#region Draft data

internal class ChampSelectData
{
    public DateTime startTime;
    public DateTime endTime;

    public List<int> allyBans = new List<int>();
    public List<int> allyPicks = new List<int>();
    public List<int> enemyBans = new List<int>();
    public List<int> enemyPicks = new List<int>();

    public ChampSelectData()
    {
        startTime = DateTime.Now;
        endTime = DateTime.MaxValue;

        allyBans = new List<int>();
        allyPicks = new List<int>();
        enemyBans = new List<int>();
        enemyPicks = new List<int>();
    }

    public void AddDraftAction(string type, int championId, bool isAlly)
    {
        if (isAlly)
        {
            if (type.Equals("pick"))
            {
                allyPicks.Add(championId);
            }
            else
            {
                allyBans.Add(championId);
            }
        }
        else
        {
            if (type.Equals("pick"))
            {
                enemyPicks.Add(championId);
            }
            else
            {
                enemyBans.Add(championId);
            }
        }
    }
}

#endregion

#region Live game data

internal class GameData
{
    public GameMediaTiming mediaTiming = new GameMediaTiming();

    public long? gameId;

    public DateTime startTime;
    public DateTime endTime;

    public List<string> frames = new List<string>();

    public GameData()
    {
        startTime = DateTime.Now;
        endTime = DateTime.MaxValue;

        frames = new List<string>();

        gameId = null;

        mediaTiming = new GameMediaTiming();
    }
}

#endregion

#region Post-game data

class PostGameData
{
    public string postGameData;

    public PostGameData()
    {
        postGameData = "";
    }
}

#endregion

#region Live API response models

public class LiveGameResponse
{
    public LiveGameInfo? GameData { get; set; }
    public List<LivePlayer> AllPlayers { get; set; } = new();
    public LiveEvents? Events { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtraFields { get; set; }
}

public class LiveGameInfo
{
    public double GameTime { get; set; }
    public string? GameMode { get; set; }
}

public class LivePlayer
{
    public string? ChampionName { get; set; }
    public string? Team { get; set; }
    public int Level { get; set; }

    public LiveScores? Scores { get; set; }
    public List<LiveItem> Items { get; set; } = new();
}

public class LiveScores
{
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int CreepScore { get; set; }
    public double WardScore { get; set; }
}

public class LiveItem
{
    [JsonProperty("itemID")]
    public int ItemId { get; set; }

    public int Count { get; set; }
}

public class LiveEvents
{
    public List<JObject> Events { get; set; } = new();
}

#endregion

#region Media timing

internal static class MediaClock
{
    // On Windows this uses the same performance counter as WASAPI QPC.
    public static double NowSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

internal sealed class GameMediaTiming
{
    public double? GameStartClockSeconds { get; set; }
    public double? GameEndClockSeconds { get; set; }

    // Elapsed recording-clock duration, not League's accelerated/paused game clock.
    public double? GameDurationSeconds { get; set; }
    public double? GameClockDurationSeconds { get; set; }
    public string? GameEndSource { get; set; }
    public bool GameTimingIsApproximate { get; set; } = true;
    public double? FirstLiveRequestHalfRoundTripSeconds { get; set; }
    public double? AudioFileStartClockSeconds { get; set; }
    public double? VideoFileStartClockSeconds { get; set; }
    public bool VideoTimingIsApproximate { get; set; } = true;
    public string? VideoTimingSource { get; set; }
    public string? VideoTimingError { get; set; }
    public double? VideoFirstFrameReadyClockSeconds { get; set; }
    public double? VideoFirstFrameEndSeconds { get; set; }
    public double? VideoDurationSeconds { get; set; }
    public int? VideoFrameCount { get; set; }
    public double? VideoMaximumFrameCallbackGapSeconds { get; set; }
    public string? AudioCaptureError { get; set; }
    public bool AudioTrimmed { get; set; }
    public bool VideoTrimmed { get; set; }
    public string? AudioTrimError { get; set; }
    public string? VideoTrimError { get; set; }
    // Explicit origin of the current WAV; raw capture time above never changes.
    public double? AlignedAudioFileStartClockSeconds { get; set; }
    public double? AudioAlignmentOffsetSeconds { get; set; }
    public string? AudioAlignmentOriginalPath { get; set; }
    public string? AudioAlignmentMethod { get; set; }

    private double? previousGameTime;
    private double? previousSampleClock;
    private bool observedLiveEnd;

    public void ObserveLiveFrame(string rawJson, double requestStarted, double requestFinished)
    {
        JObject json = JObject.Parse(rawJson);
        double? gameTime = (double?)json["gameData"]?["gameTime"];
        if (gameTime is null || !double.IsFinite(gameTime.Value) || gameTime < 0)
            return;

        double sampleClock = (requestStarted + requestFinished) / 2.0;
        if (GameStartClockSeconds == null)
        {
            // Requires recording from the beginning, before any fast-forward/pause.
            GameStartClockSeconds = sampleClock - gameTime.Value;
            FirstLiveRequestHalfRoundTripSeconds = (requestFinished - requestStarted) / 2.0;
        }

        if (!observedLiveEnd && json["events"]?["Events"] is JArray events)
        {
            foreach (JToken ev in events)
            {
                if ((string?)ev["EventName"] != "GameEnd")
                    continue;
                double? endTime = (double?)ev["EventTime"];
                if (endTime is not > 0 || !double.IsFinite(endTime.Value))
                    continue;
                GameClockDurationSeconds = endTime;
                double endClock = sampleClock;
                string source = "Live GameEnd first observed (approximate)";
                if (
                    previousGameTime is double before
                    && previousSampleClock is double clock
                    && gameTime.Value > before
                    && endTime.Value >= before
                    && endTime.Value <= gameTime.Value
                )
                {
                    double fraction = (endTime.Value - before) / (gameTime.Value - before);
                    endClock = clock + fraction * (sampleClock - clock);
                    source = "Live GameEnd interpolated between polls (approximate)";
                }
                SetEnd(endClock, source);
                observedLiveEnd = true;
                break;
            }
        }
        previousGameTime = gameTime;
        previousSampleClock = sampleClock;
    }

    public void MarkGameEnded(double detectedEndClockSeconds)
    {
        if (GameEndClockSeconds == null)
            SetEnd(detectedEndClockSeconds, "Post-game phase detection (approximate)");
    }

    private void SetEnd(double endClock, string source)
    {
        if (
            GameStartClockSeconds is not double start
            || !double.IsFinite(endClock)
            || endClock <= start
        )
            return;
        GameEndClockSeconds = endClock;
        GameDurationSeconds = endClock - start;
        GameEndSource = source;
    }

    public void ObservePostGame(string rawJson)
    {
        if (GameClockDurationSeconds != null || string.IsNullOrWhiteSpace(rawJson))
            return;
        try
        {
            double? length = (double?)JObject.Parse(rawJson)["gameLength"];
            if (length is > 0 && double.IsFinite(length.Value))
                GameClockDurationSeconds = length;
        }
        catch (JsonException) { }
        catch (FormatException) { }
        catch (InvalidCastException) { }
    }
}

#endregion
