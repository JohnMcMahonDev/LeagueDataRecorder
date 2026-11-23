
namespace League_Data_Recorder;

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
