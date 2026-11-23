using System.Globalization;
using System.Text;

namespace League_Data_Recorder;

internal sealed record GameSheetClipboardData(string Text, int PlayerCount, string GameId);

// Unicode tab-separated text pastes into a rectangular Sheets range. Keep this
// formatter independent of WinForms so data handling can be verified separately.
internal static class GameSheetExport
{
    internal static GameSheetClipboardData Build(AnalysisDataset dataset)
    {
        var games = dataset.Games.OrderBy(g => g.TeamId).ToList();
        if (games.Select(g => g.GameId).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidOperationException("Choose a recording with usable post-game player statistics.");
        var text = new StringBuilder();
        text.AppendLine(string.Join('\t', new[]
        {
            "Game ID", "Date", "Game type", "Team", "Opponent", "Side", "Result", "Player", "Role", "Champion",
            "Kills", "Deaths", "Assists", "KDA", "CS", "CS/min", "Damage to champions", "Damage/min",
            "Damage taken", "Damage taken/min", "Gold", "Gold/min", "Vision score", "Vision/min", "Duration (seconds)",
            "Team Soul", "Team Baron", "Team Herald", "Team Grubs", "Team Elder",
        }));
        int count = 0;
        foreach (var game in games)
        foreach (var p in game.Players.OrderBy(p => RoleIndex(p.Role)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var cells = new List<string>
            {
                Cell(game.GameId), game.PlayedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
                Cell(game.GameType), Cell(game.TeamName), Cell(game.OpponentName),
                game.TeamId == 100 ? "Blue" : game.TeamId == 200 ? "Red" : "Team " + game.TeamId,
                game.Won == true ? "Win" : game.Won == false ? "Loss" : "Unknown",
                Cell(p.Name), Cell(p.Role), Cell(p.Champion), Number(p.Kills), Number(p.Deaths), Number(p.Assists),
                Number(Divide(p.Kills + p.Assists, p.Deaths)), Number(p.Cs), Number(Divide(p.Cs, game.Minutes)),
                Number(p.Damage), Number(Divide(p.Damage, game.Minutes)), Number(p.DamageTaken),
                Number(Divide(p.DamageTaken, game.Minutes)), Number(p.Gold), Number(Divide(p.Gold, game.Minutes)),
                Number(p.Vision), Number(Divide(p.Vision, game.Minutes)), Number(game.Minutes * 60),
            };
            foreach (string objective in GameAnalysis.ObjectiveNames)
            {
                bool? value = game.Objectives.GetValueOrDefault(objective);
                cells.Add(value.HasValue ? value.Value ? "1" : "0" : "");
            }
            text.AppendLine(string.Join('\t', cells));
            count++;
        }
        if (count == 0) throw new InvalidOperationException("This recording has no usable post-game player statistics.");
        return new GameSheetClipboardData(text.ToString(), count, games[0].GameId);
    }

    private static string Cell(string value)
    {
        value = value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        // Team/player names are text, even if they resemble a spreadsheet formula.
        string trimmed = value.TrimStart();
        return trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
    }

    private static string Number(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "";
    private static double? Divide(double? value, double? denominator) => denominator > 0 ? value / denominator : null;
    private static int RoleIndex(string role) => role switch
    { "Top" => 0, "Jungle" => 1, "Mid" => 2, "ADC" => 3, "Support" => 4, _ => 5 };
}
