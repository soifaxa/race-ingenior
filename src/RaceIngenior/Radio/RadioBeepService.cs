using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RaceIngenior.Services;

namespace RaceIngenior.Radio;

/// <summary>Short radio-style tones for PTT open/close (played before mic starts / after mic stops).</summary>
public sealed class RadioBeepService
{
    private readonly object _gate = new();
    private WaveOutEvent? _output;

    public bool Enabled { get; set; }

    public RadioBeepService(UserPreferencesStore prefs)
    {
        Enabled = prefs.Current.PttBeepEnabled ?? true;
    }

    public Task PlayOpenAsync(CancellationToken ct = default) =>
        PlayToneAsync(frequencyHz: 1050, durationMs: 75, volume: 0.28, ct);

    public Task PlayCloseAsync(CancellationToken ct = default) =>
        PlayToneAsync(frequencyHz: 780, durationMs: 55, volume: 0.22, ct);

    private async Task PlayToneAsync(double frequencyHz, int durationMs, double volume, CancellationToken ct)
    {
        if (!Enabled || durationMs <= 0)
            return;

        var sine = new SignalGenerator(44100, 1)
        {
            Gain = Math.Clamp(volume, 0.05, 0.5),
            Frequency = frequencyHz,
            Type = SignalGeneratorType.Sin
        };
        var truncated = sine.Take(TimeSpan.FromMilliseconds(durationMs));

        WaveOutEvent? output = null;
        try
        {
            lock (_gate)
            {
                StopLocked();
                output = new WaveOutEvent { DesiredLatency = 50 };
                _output = output;
                output.Init(truncated);
                output.Play();
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(durationMs + 80);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                PlaybackState state;
                lock (_gate)
                    state = output.PlaybackState;
                if (state != PlaybackState.Playing)
                    break;
                await Task.Delay(15, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_output, output))
                    StopLocked();
                else
                    TryDispose(output);
            }
        }
    }

    private void StopLocked()
    {
        var o = _output;
        _output = null;
        TryDispose(o);
    }

    private static void TryDispose(WaveOutEvent? output)
    {
        if (output is null)
            return;
        try { output.Stop(); } catch { /* ignore */ }
        try { output.Dispose(); } catch { /* ignore */ }
    }
}
