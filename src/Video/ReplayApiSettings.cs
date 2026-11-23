using System.Text;
using System.Text.RegularExpressions;

namespace League_Data_Recorder;

// League reads this flag when its game process starts. Change only the flag,
// before Open Replay, and retain the original config for manual recovery.
internal static class ReplayApiSettings
{
    internal static bool Enable(string installationDirectory)
    {
        string path = Path.Combine(installationDirectory, "Config", "game.cfg");
        byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : [];
        using var reader = new StreamReader(new MemoryStream(original), new UTF8Encoding(false, true), true);
        string text = reader.ReadToEnd();
        Encoding encoding = reader.CurrentEncoding;
        string updated = EnableInText(text);
        if (updated == text) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, updated, encoding);
            // Do not silently replace a config that League changed while we read it.
            if (File.Exists(path))
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
                    throw new IOException("League's game.cfg changed while enabling the replay overlay. Try Open Replay again.");
                string backup = path + ".league-recorder.bak";
                if (!File.Exists(backup))
                {
                    using var output = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    output.Write(original);
                }
            }
            File.Move(temp, path, true);
            return true;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static string EnableInText(string text)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool inGeneral = false, flagFound = false;
        int? firstHeaderEnd = null;
        bool headerHasNewline = false;
        var edits = new List<(int Start, int Length, string Text)>();
        foreach (Match line in Regex.Matches(text, @"(?m)^(?<body>[^\r\n]*)(?<eol>\r\n|\n|$)"))
        {
            if (line.Length == 0) continue;
            string body = line.Groups["body"].Value;
            var section = Regex.Match(body, @"^[ \t]*\[(?<name>[^\]]+)\][ \t]*(?:[;#].*)?$");
            if (section.Success)
            {
                inGeneral = section.Groups["name"].Value.Trim().Equals("General", StringComparison.OrdinalIgnoreCase);
                if (inGeneral && firstHeaderEnd is null)
                {
                    firstHeaderEnd = line.Index + line.Length;
                    headerHasNewline = line.Groups["eol"].Length > 0;
                }
                continue;
            }
            if (!inGeneral) continue;
            var entry = Regex.Match(body,
                @"^(?<prefix>[ \t]*EnableReplayApi[ \t]*=[ \t]*)(?<value>[^;#\r\n]*?)(?<suffix>[ \t]*(?:[;#].*)?)$",
                RegexOptions.IgnoreCase);
            if (!entry.Success) continue;
            flagFound = true;
            if (entry.Groups["value"].Value.Trim() != "1")
                edits.Add((line.Index, body.Length, entry.Groups["prefix"].Value + "1" + entry.Groups["suffix"].Value));
        }
        if (!flagFound)
        {
            if (firstHeaderEnd is int position)
                edits.Add((position, 0, (headerHasNewline ? "" : newline) + "EnableReplayApi=1" + newline));
            else
                edits.Add((text.Length, 0, (text.Length == 0 || text.EndsWith('\n') ? "" : newline) +
                    "[General]" + newline + "EnableReplayApi=1" + newline));
        }
        var result = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        return result.ToString();
    }
}
