namespace League_Data_Recorder;

// Shared by the recorded-video player and the live League replay overlay.
internal sealed record VideoCommsCue(
    int Id, double Start, double End, string Label, string Speaker, string Role,
    string Champion, string? Icon, string Text, IReadOnlyList<VideoCommsWord> Words,
    bool OverlappingSpeech = false, IReadOnlyList<VideoCommsParticipant>? Participants = null);

internal sealed record VideoCommsParticipant(string Speaker, string Label, string Role, string Champion, string? Icon);

internal sealed record VideoCommsWord(double Start, double End, string Text, string? TimingSource = null);

// These use transcript audio coordinates, like captions and transcript seeks.
internal sealed record VideoObjectiveCue(string Id, double Start, double End, string Label, string Role, string Speaker,
    string Champion = "", string? Icon = null, string SpeakerId = "");
