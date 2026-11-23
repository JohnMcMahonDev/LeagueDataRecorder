using System;
using System.IO;

namespace League_Data_Recorder;

internal static class RecordingFileCleanup
{
    // Native encoder shutdown can leave an MP4 locked. Cleanup is best effort;
    // a temporary file must never prevent game JSON/session data from saving.
    internal static void TryDelete(string? path, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"Temporary media left in place: {path}. {ex.Message}");
        }
    }
}
