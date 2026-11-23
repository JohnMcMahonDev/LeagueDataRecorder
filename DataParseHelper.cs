using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace League_Data_Recorder
{
    internal static class DataParseHelper
    {
        public static string ParseGameData(GameData gameData)
        {
            // Final JSON structure.
            JObject result = new JObject
            {
                ["gameInfo"] = new JObject(),
                ["players"] = new JArray(),
                ["events"] = new JArray(),
                ["activePlayerGold"] = new JArray(),
                ["teamGold"] = new JArray()
            };

            JObject gameInfo = (JObject)result["gameInfo"]!;
            JArray players = (JArray)result["players"]!;
            JArray events = (JArray)result["events"]!;
            JArray activePlayerGold = (JArray)result["activePlayerGold"]!;

            // Track players and their previous states.
            Dictionary<string, JObject> playerObjects = new();
            Dictionary<string, JObject> previousStates = new();

            // Prevent duplicate events across frames.
            HashSet<int> recordedEventIds = new();

            // Capture exact cumulative team totals only when every roster member
            // supplies totalGold. Ordinary Live Client Data often does not.
            JArray teamGold = (JArray)result["teamGold"]!;
            static double? TotalGold(JToken player)
            {
                foreach (string path in new[] { "totalGold", "totalGoldEarned", "scores.totalGold", "scores.goldEarned" })
                    if (player.SelectToken(path) is JValue value && value.Type is (JTokenType.Integer or JTokenType.Float))
                    {
                        double gold = value.Value<double>();
                        if (double.IsFinite(gold) && gold >= 0) return gold;
                    }
                return null;
            }
            // Track local player's gold.
            double? previousGold = null;

            // Helper: Clone a value from a JSON object.
            static JToken? Get(JToken? source, string path)
            {
                return source?.SelectToken(path)?.DeepClone();
            }

            foreach (string frame in gameData.frames)
            {
                JObject root;

                try
                {
                    root = JObject.Parse(frame);
                }
                catch (JsonException)
                {
                    // Skip invalid frames instead of losing the entire game.
                    continue;
                }

                JToken? currentGameData = root["gameData"];
                double time = currentGameData?["gameTime"]?.Value<double>() ?? 0;

                // -------------------------
                // GAME INFORMATION
                // -------------------------

                if (gameInfo.Count == 0)
                {
                    gameInfo["gameMode"] = Get(currentGameData, "gameMode");
                    gameInfo["mapName"] = Get(currentGameData, "mapName");
                    gameInfo["mapNumber"] = Get(currentGameData, "mapNumber");
                    gameInfo["mapTerrain"] = Get(currentGameData, "mapTerrain");
                }

                gameInfo["duration"] = time;

                // -------------------------
                // PLAYERS
                // -------------------------

                if (root["allPlayers"] is JArray allPlayers)
                {
                    foreach (JObject player in allPlayers.OfType<JObject>())
                    {
                        string team =
                            player.Value<string>("team") ?? "UNKNOWN";

                        string champion =
                            player.Value<string>("championName") ?? "UNKNOWN";

                        string riotId =
                            player.Value<string>("riotId")
                            ?? player.Value<string>("summonerName")
                            ?? "";

                        // Stable identity within this game.
                        string playerKey = !string.IsNullOrWhiteSpace(riotId)
                            ? team + "_" + riotId
                            : team + "_" + champion;

                        // Create a player entry on first observation.
                        if (!playerObjects.TryGetValue(
                            playerKey, out JObject? playerData))
                        {
                            playerData = new JObject
                            {
                                ["riotId"] = riotId,
                                ["championName"] = champion,
                                ["team"] = team,
                                ["position"] = Get(player, "position"),
                                ["isBot"] = Get(player, "isBot"),

                                ["summonerSpells"] =
                                    Get(player, "summonerSpells"),

                                ["runes"] = Get(player, "runes"),

                                ["timeline"] = new JArray()
                            };

                            playerObjects.Add(playerKey, playerData);
                            players.Add(playerData);
                        }

                        // Build the player's current state.
                        JObject state = new JObject
                        {
                            ["level"] = Get(player, "level"),
                            ["totalGold"] = TotalGold(player),

                            ["kills"] = Get(player, "scores.kills"),
                            ["deaths"] = Get(player, "scores.deaths"),
                            ["assists"] = Get(player, "scores.assists"),

                            ["cs"] = Get(player, "scores.creepScore"),
                            ["wardScore"] = Get(player, "scores.wardScore"),

                            ["isDead"] = Get(player, "isDead")
                        };

                        // -------------------------
                        // ITEMS
                        // -------------------------

                        JArray items = new JArray();

                        if (player["items"] is JArray playerItems)
                        {
                            foreach (JObject item in playerItems
                                .OfType<JObject>()
                                .OrderBy(x => x.Value<int?>("slot") ?? 0))
                            {
                                items.Add(new JObject
                                {
                                    ["itemID"] = Get(item, "itemID"),
                                    ["count"] = Get(item, "count"),
                                    ["slot"] = Get(item, "slot")
                                });
                            }
                        }
                        state["items"] = items;

                        // Add time only after comparing states,
                        // otherwise every frame would count as changed.
                        JObject snapshot = (JObject)state.DeepClone();
                        snapshot["time"] = time;

                        ((JArray)playerData["timeline"]!).Add(snapshot);

                        previousStates[playerKey] =
                            (JObject)state.DeepClone();
                    }
                }

                // -------------------------
                // GAME EVENTS
                // -------------------------

                if (root["events"]?["Events"] is JArray gameEvents)
                {
                    foreach (JObject gameEvent in
                        gameEvents.OfType<JObject>())
                    {
                        int? eventId = gameEvent.Value<int?>("EventID");

                        if (!eventId.HasValue)
                            continue;

                        if (!recordedEventIds.Add(eventId.Value))
                            continue;

                        // Preserve the entire event because different
                        // event types have different properties.
                        events.Add(gameEvent.DeepClone());
                    }
                }

                if (root["allPlayers"] is JArray goldPlayers)
                {
                    var blue = goldPlayers.Where(p => p.Value<string>("team") is "ORDER" or "BLUE" or "100").ToArray();
                    var red = goldPlayers.Where(p => p.Value<string>("team") is "CHAOS" or "RED" or "200").ToArray();
                    if (blue.Length > 0 && blue.Length == red.Length && blue.Concat(red).All(p => TotalGold(p).HasValue))
                        teamGold.Add(new JObject { ["time"] = time, ["blueGold"] = blue.Sum(p => TotalGold(p)!.Value),
                            ["redGold"] = red.Sum(p => TotalGold(p)!.Value) });
                }
                // -------------------------
                // ACTIVE PLAYER GOLD
                // -------------------------

                JToken? goldToken = root["activePlayer"]?["currentGold"];

                if (goldToken != null &&
                    goldToken.Type != JTokenType.Null)
                {
                    double gold = goldToken.Value<double>();

                    if (!previousGold.HasValue ||
                        Math.Abs(previousGold.Value - gold) > 0.01)
                    {
                        activePlayerGold.Add(new JObject
                        {
                            ["time"] = time,
                            ["gold"] = gold
                        });

                        previousGold = gold;
                    }
                }
            }

            return result.ToString(Formatting.None);
        }


        public static string ParsePostGameData(PostGameData postGameData, string? allyTeamName = null, string? enemyTeamName = null)
        {
            // Convert the existing data object into JSON.
            // If PostGameData is already a model of the API response,
            // no additional deserialization classes are needed.
            JObject root = JObject.Parse(postGameData.postGameData);

            JObject result = new JObject
            {
                ["gameInfo"] = new JObject(),
                ["localPlayer"] = new JObject(),
                ["timeline"] = root["timeline"]?.DeepClone(),
                ["teams"] = new JArray()
            };

            // --------------------------------
            // GAME INFORMATION
            // --------------------------------

            JObject gameInfo = (JObject)result["gameInfo"]!;

            string[] gameFields =
            {
                "gameId",
                "gameLength",
                "gameMode",
                "gameType",
                "queueType",
                "ranked",
                "endOfGameTimestamp",
                "gameEndedInEarlySurrender",
                "teamEarlySurrendered",
                "invalid",
                "gameMutators"
            };

            foreach (string field in gameFields)
            {
                if (root[field] != null)
                    gameInfo[field] = root[field]!.DeepClone();
            }

            // --------------------------------
            // LOCAL PLAYER
            // --------------------------------

            if (root["localPlayer"] is JObject localPlayer)
            {
                result["localPlayer"] = ParsePostGamePlayer(localPlayer);
            }

            // --------------------------------
            // TEAMS
            // --------------------------------

            JArray teams = (JArray)result["teams"]!;

            if (root["teams"] is JArray sourceTeams)
            {
                foreach (JObject team in sourceTeams.OfType<JObject>())
                {
                    JObject parsedTeam = new JObject();

                    string[] teamFields =
                    {
                    "teamId",
                    "name",
                    "isWinningTeam",
                    "isPlayerTeam",
                    "isBottomTeam",
                    "championBans",
                    "stats"
                };

                    foreach (string field in teamFields)
                    {
                        if (team[field] != null)
                            parsedTeam[field] = team[field]!.DeepClone();
                    }

                    // Preserve team names per game, including before the session is stopped.
                    // A session file remains the fallback for previously recorded games.
                    int? localTeamId = root["localPlayer"]?["teamId"]?.Value<int?>();
                    bool? isAlly = team["isPlayerTeam"]?.Value<bool?>();
                    if (!isAlly.HasValue && localTeamId.HasValue)
                        isAlly = team["teamId"]?.Value<int?>() == localTeamId;
                    string? configuredName = isAlly == true ? allyTeamName : isAlly == false ? enemyTeamName : null;
                    if (!string.IsNullOrWhiteSpace(configuredName))
                        parsedTeam["name"] = configuredName.Trim();

                    // --------------------------------
                    // PLAYERS
                    // --------------------------------

                    JArray players = new JArray();

                    if (team["players"] is JArray sourcePlayers)
                    {
                        foreach (JObject player in
                            sourcePlayers.OfType<JObject>())
                        {
                            players.Add(ParsePostGamePlayer(player));
                        }
                    }

                    parsedTeam["players"] = players;
                    teams.Add(parsedTeam);
                }
            }

            return result.ToString(Formatting.None);
        }

        private static JObject ParsePostGamePlayer(JObject player)
        {
            JObject result = new JObject();

            string[] fields =
            {
                // Identification
                "puuid",
                "participantId",
                "timeline",
                "summonerName",
                "riotId",
                "riotIdGameName",
                "riotIdTagLine",

                // Champion and team
                "championId",
                "championName",
                "teamId",
                "detectedTeamPosition",
                "selectedPosition",

                // Summoner spells
                "spell1Id",
                "spell2Id",

                // Items
                "items",

                // Additional information
                "leaver",
                "botPlayer",
                "isLocalPlayer",

                // Complete player statistics
                "stats"
            };

            foreach (string field in fields)
            {
                if (player[field] != null)
                    result[field] = player[field]!.DeepClone();
            }

            return result;
        }
    }
}
