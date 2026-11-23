namespace League_Data_Recorder;

public sealed record EmotionInferenceOptions
{
    public bool Enabled { get; init; } = true;
    public string ModelPath { get; init; } = "models/emotion/audeering/model.onnx";
    public bool ForceRegenerate { get; init; }
    public int CpuThreads { get; init; }
    public int WindowSeconds { get; init; } = 20;

    internal void Validate()
    {
        if (WindowSeconds is < 5 or > 60)
            throw new ArgumentException("Emotion context window must be from 5 to 60 seconds.");
        if (CpuThreads < 0) throw new ArgumentException("Emotion CPU threads cannot be negative.");
    }
}

public sealed record EmotionAnalysisResult(string Path, bool Generated, string? Error);

// Valence is a dimensional regression output, not a class probability.
public sealed record EmotionWindow(double Start, double End, double SpeechSeconds, float Valence);

public sealed class CommsEmotionReport
{
    public const int CurrentVersion = 3;
    public int WindowSeconds { get; init; }
    public const int StepSeconds = 5;
    public int Version { get; init; } = CurrentVersion;
    public string Engine { get; init; } = "audEERING wav2vec2 / ONNX Runtime CPU";
    public string Model { get; init; } = "w2v2-L-robust-12.6bc4a7fd-1.1.0";
    public string ModelSha256 { get; init; } = "";
    public string AudioStamp { get; init; } = "";
    public string SpeechFingerprint { get; init; } = "";
    public double AudioTimelineShiftSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public bool Complete { get; init; }
    public List<EmotionWindow> Windows { get; init; } = [];
}

