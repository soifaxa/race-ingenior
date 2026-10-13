using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;
using RaceIngenior.Services;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace RaceIngenior.Radio;

/// <summary>Offline WinRT / OneCore TTS (fallback when Gemini neural is unavailable).</summary>
public sealed class WindowsTtsService : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly ILogger<WindowsTtsService> _logger;
    private readonly string _voiceHint;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<TtsVoiceOption> _voices = [];
    private int _rate;
    private string? _selectedVoiceId;
    private MediaPlayer? _player;

    public int Rate => _rate;
    public string? SelectedVoiceId => _selectedVoiceId;
    public IReadOnlyList<TtsVoiceOption> AvailableVoices => _voices;

    public WindowsTtsService(
        IOptions<AppSettings> settings,
        UserPreferencesStore prefs,
        ILogger<WindowsTtsService> logger)
    {
        _logger = logger;
        _voiceHint = settings.Value.PreferredFrenchVoiceContains ?? "French";
        SetRate(prefs.Current.TtsRate);
        RefreshVoiceCatalog();
        SetVoice(prefs.Current.TtsVoiceId);
    }

    public void SetRate(int rate)
    {
        _rate = Math.Clamp(rate, -10, 10);
        ApplySpeakingOptions();
    }

    public void SetVoice(string? voiceId)
    {
        // Ignore Gemini neural ids — keep Windows auto for fallback.
        if (!string.IsNullOrWhiteSpace(voiceId)
            && voiceId.StartsWith(GeminiTtsClient.VoiceIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _selectedVoiceId = null;
            ApplySelectedVoice();
            return;
        }

        _selectedVoiceId = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim();
        ApplySelectedVoice();
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            ApplySpeakingOptions();
            var spoken = TtsTextPrep.Prepare(text);
            var ssml = BuildSsml(spoken, _synth.Voice.Language);

            using var stream = await _synth.SynthesizeSsmlToStreamAsync(ssml).AsTask(ct);
            await PlayStreamAsync(stream, ct);
        }
        catch (OperationCanceledException)
        {
            StopPlayback();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WinRT SSML TTS failed; retrying plain text");
            try
            {
                using var stream = await _synth.SynthesizeTextToStreamAsync(TtsTextPrep.Prepare(text)).AsTask(ct);
                await PlayStreamAsync(stream, ct);
            }
            catch (OperationCanceledException)
            {
                StopPlayback();
                throw;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "TTS playback failed");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        StopPlayback();
        _synth.Dispose();
        _gate.Dispose();
    }

    private void RefreshVoiceCatalog()
    {
        _voices.Clear();
        _voices.Add(new TtsVoiceOption("", "Auto (best French)", "fr", true));

        foreach (var voice in SpeechSynthesizer.AllVoices
                     .Where(IsFrenchVoice)
                     .OrderByDescending(ScoreVoice)
                     .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _voices.Add(new TtsVoiceOption(
                voice.Id,
                voice.DisplayName,
                voice.Language,
                ScoreVoice(voice) >= 80));
        }
    }

    private void ApplySelectedVoice()
    {
        VoiceInformation? voice = null;
        if (!string.IsNullOrWhiteSpace(_selectedVoiceId))
            voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == _selectedVoiceId);

        voice ??= PickBestFrenchVoice();

        if (voice is null)
        {
            _logger.LogWarning("No French TTS voice available; using system default ({Voice})",
                _synth.Voice.DisplayName);
            return;
        }

        _synth.Voice = voice;
        ApplySpeakingOptions();
        _logger.LogInformation(
            "Windows TTS voice: {Voice} ({Lang}), rate {Rate}",
            voice.DisplayName, voice.Language, _rate);
    }

    private VoiceInformation? PickBestFrenchVoice()
    {
        var french = SpeechSynthesizer.AllVoices.Where(IsFrenchVoice).ToList();
        if (french.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(_voiceHint))
        {
            var hinted = french
                .Where(v =>
                    v.DisplayName.Contains(_voiceHint, StringComparison.OrdinalIgnoreCase)
                    || v.Description.Contains(_voiceHint, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ScoreVoice)
                .FirstOrDefault();
            if (hinted is not null && ScoreVoice(hinted) >= 40)
                return hinted;
        }

        return french.OrderByDescending(ScoreVoice).ThenBy(v => v.DisplayName).First();
    }

    private void ApplySpeakingOptions()
    {
        _synth.Options.SpeakingRate = Math.Clamp(1.0 + (_rate * 0.05), 0.55, 1.55);
        _synth.Options.AudioVolume = 1.0;
        _synth.Options.AudioPitch = 1.0;
    }

    private async Task PlayStreamAsync(SpeechSynthesisStream stream, CancellationToken ct)
    {
        StopPlayback();

        var player = new MediaPlayer();
        _player = player;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnEnded(MediaPlayer sender, object args) => tcs.TrySetResult();
        void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
            tcs.TrySetException(new InvalidOperationException(args.ErrorMessage ?? "MediaPlayer failed"));

        player.MediaEnded += OnEnded;
        player.MediaFailed += OnFailed;
        player.SetStreamSource(stream);
        player.Play();

        await using var reg = ct.Register(() =>
        {
            try { player.Pause(); } catch { /* ignore */ }
            tcs.TrySetCanceled(ct);
        });

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            player.MediaEnded -= OnEnded;
            player.MediaFailed -= OnFailed;
            if (ReferenceEquals(_player, player))
                _player = null;
            player.Dispose();
        }
    }

    private void StopPlayback()
    {
        var player = _player;
        _player = null;
        if (player is null)
            return;

        try { player.Pause(); } catch { /* ignore */ }
        try { player.Dispose(); } catch { /* ignore */ }
    }

    private static bool IsFrenchVoice(VoiceInformation voice) =>
        voice.Language.StartsWith("fr", StringComparison.OrdinalIgnoreCase);

    private static int ScoreVoice(VoiceInformation voice)
    {
        var name = voice.DisplayName + " " + voice.Description;
        var score = 0;

        if (Contains(name, "Natural")) score += 100;
        if (Contains(name, "Online")) score += 40;
        if (Contains(name, "Neural")) score += 90;
        if (Contains(name, "Julie")) score += 70;
        if (Contains(name, "Denise")) score += 85;
        if (Contains(name, "Henri")) score += 80;
        if (Contains(name, "Remy") || Contains(name, "Rémy")) score += 80;
        if (Contains(name, "Paul")) score += 55;
        if (Contains(name, "Hortense")) score += 20;
        if (Contains(name, "Desktop")) score -= 15;
        if (voice.Gender == VoiceGender.Female) score += 5;

        return score;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string BuildSsml(string text, string language)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "fr-FR" : language;
        var escaped = new XText(text).ToString();
        var sb = new StringBuilder(escaped.Length + 160);
        sb.Append("<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='")
          .Append(lang)
          .Append("'>")
          .Append("<voice xml:lang='")
          .Append(lang)
          .Append("'>")
          .Append(escaped)
          .Append("</voice></speak>");
        return sb.ToString();
    }
}
