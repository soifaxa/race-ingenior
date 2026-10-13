namespace RaceIngenior.Radio;

public sealed record TtsVoiceOption(string Id, string DisplayName, string Language, bool IsRecommended);

public interface ITtsService
{
    int Rate { get; }
    void SetRate(int rate);
    string? SelectedVoiceId { get; }
    IReadOnlyList<TtsVoiceOption> AvailableVoices { get; }
    /// <summary>Null / empty = auto (Gemini neural when key present, else best Windows).</summary>
    void SetVoice(string? voiceId);
    /// <summary>Rebuild voice list (e.g. after Gemini API key save/clear).</summary>
    void RefreshAvailableVoices();
    Task SpeakAsync(string text, CancellationToken ct = default);
}
