using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using RaceIngenior.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace RaceIngenior.Radio;

public interface ISttService
{
    Task EnsureModelAsync(CancellationToken ct = default);
    Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default);
}

public sealed class WhisperSttService : ISttService, IAsyncDisposable
{
    /// <summary>Biases decoding toward WEC / race-radio French vocabulary.</summary>
    private const string RaceRadioPrompt =
        "Radio course WEC en français. Copie. Box. Pneus. Carburant. Tour. Écart. " +
        "Safety car. Full course yellow. Freins. Température. Usure. Pluie. " +
        "Hypercar. LMP2. LMGT3. Pit. Push. Save. Out lap. In lap.";

    private readonly AppSettings _settings;
    private readonly ILogger<WhisperSttService> _logger;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _transcribeGate = new(1, 1);

    public WhisperSttService(IOptions<AppSettings> settings, ILogger<WhisperSttService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task EnsureModelAsync(CancellationToken ct = default)
    {
        if (_processor is not null)
            return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_processor is not null)
                return;

            var path = ResolveModelPath(_settings.WhisperModelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var ggmlType = InferGgmlType(path);

            if (!File.Exists(path))
            {
                _logger.LogInformation("Downloading Whisper {Type} model to {Path}", ggmlType, path);
                await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType, cancellationToken: ct);
                await using var file = File.OpenWrite(path);
                await modelStream.CopyToAsync(file, ct);
            }

            _factory = WhisperFactory.FromPath(path);
            var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
            _processor = _factory.CreateBuilder()
                .WithLanguage("fr")
                .WithPrompt(RaceRadioPrompt)
                .WithTemperature(0.0f)
                .WithSingleSegment()
                .WithNoContext()
                .WithThreads(threads)
                .Build();

            _logger.LogInformation("Whisper ready ({Type}, {Threads} threads) · {Path}", ggmlType, threads, path);
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        await EnsureModelAsync(ct);
        if (_processor is null || wavBytes.Length < 44)
            return "";

        var enhanced = PttAudioPrep.EnhanceForWhisper(wavBytes, out var durationMs);
        if (enhanced.Length < 44 || durationMs < 280)
        {
            _logger.LogDebug("PTT audio too short after trim ({Ms} ms) — skipped", durationMs);
            return "";
        }

        await _transcribeGate.WaitAsync(ct);
        try
        {
            await using var ms = new MemoryStream(enhanced);
            var text = new StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(ms, ct))
                text.Append(segment.Text);

            return CleanTranscript(text.ToString());
        }
        finally
        {
            _transcribeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
            await _processor.DisposeAsync();
        _factory?.Dispose();
        _initGate.Dispose();
        _transcribeGate.Dispose();
    }

    private static string CleanTranscript(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0)
            return "";

        // Whisper sometimes echoes prompt fragments or hallucinates on noise.
        foreach (var junk in new[]
                 {
                     "Sous-titres réalisés", "Merci d'avoir regardé", "Thanks for watching",
                     "Amara.org", "www.", "♪", "[Silence]", "(silence)"
                 })
        {
            if (t.Contains(junk, StringComparison.OrdinalIgnoreCase))
                return "";
        }

        return t;
    }

    internal static GgmlType InferGgmlType(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.Contains("tiny")) return GgmlType.Tiny;
        if (name.Contains("small")) return GgmlType.Small;
        if (name.Contains("medium")) return GgmlType.Medium;
        if (name.Contains("large-v3") || name.Contains("large_v3")) return GgmlType.LargeV3;
        if (name.Contains("large")) return GgmlType.LargeV3;
        return GgmlType.Base;
    }

    /// <summary>
    /// Resolves a relative Whisper path outside bin/: prefers an existing file under
    /// LocalAppData, repo Models/, or project folder; otherwise installs under LocalAppData.
    /// </summary>
    internal static string ResolveModelPath(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            configured = "Models/ggml-base.bin";

        configured = configured.Trim().Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(configured))
            return Path.GetFullPath(configured);

        foreach (var root in CandidateRoots())
        {
            var candidate = Path.GetFullPath(Path.Combine(root, configured));
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.GetFullPath(Path.Combine(LocalAppRoot(), configured));
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return LocalAppRoot();

        var repoGuess = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (Directory.Exists(repoGuess))
            yield return repoGuess;

        var projectGuess = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        if (Directory.Exists(projectGuess))
            yield return projectGuess;
    }

    private static string LocalAppRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaceIngenior");
}

/// <summary>Trim silence + peak-normalize PTT WAV for cleaner Whisper input.</summary>
internal static class PttAudioPrep
{
    public static byte[] EnhanceForWhisper(byte[] wavBytes, out int durationMs)
    {
        durationMs = 0;
        try
        {
            using var input = new MemoryStream(wavBytes);
            using var reader = new WaveFileReader(input);
            var format = reader.WaveFormat;
            if (format.Channels != 1 || format.BitsPerSample != 16)
                return wavBytes;

            var sampleCount = (int)(reader.Length / 2);
            if (sampleCount < 160) // <10 ms @ 16 kHz
                return wavBytes;

            var samples = new short[sampleCount];
            var bytes = new byte[sampleCount * 2];
            var read = reader.Read(bytes, 0, bytes.Length);
            Buffer.BlockCopy(bytes, 0, samples, 0, read);

            // Absolute peak for AGC
            var peak = 1;
            for (var i = 0; i < samples.Length; i++)
            {
                var a = Math.Abs(samples[i]);
                if (a > peak) peak = a;
            }

            // Trim leading/trailing near-silence (threshold scales with peak)
            var threshold = Math.Max(400, peak / 40);
            var start = 0;
            while (start < samples.Length && Math.Abs(samples[start]) < threshold)
                start++;
            var end = samples.Length - 1;
            while (end > start && Math.Abs(samples[end]) < threshold)
                end--;

            // Keep a small pad so word onsets aren't clipped
            var pad = Math.Min(format.SampleRate / 20, 800); // ~50 ms
            start = Math.Max(0, start - pad);
            end = Math.Min(samples.Length - 1, end + pad);
            var len = end - start + 1;
            if (len < format.SampleRate / 8) // <125 ms of content
            {
                durationMs = (int)(len * 1000.0 / format.SampleRate);
                return wavBytes;
            }

            // Peak normalize toward ~80% full scale (helps quiet headset mics)
            var target = 26000.0;
            var gain = peak < 2000 ? 1.0 : Math.Min(target / peak, 6.0);
            var outSamples = new short[len];
            for (var i = 0; i < len; i++)
            {
                var v = samples[start + i] * gain;
                outSamples[i] = (short)Math.Clamp((int)Math.Round(v), short.MinValue, short.MaxValue);
            }

            durationMs = (int)(len * 1000.0 / format.SampleRate);

            using var output = new MemoryStream();
            using (var writer = new WaveFileWriter(output, format))
            {
                var outBytes = new byte[len * 2];
                Buffer.BlockCopy(outSamples, 0, outBytes, 0, outBytes.Length);
                writer.Write(outBytes, 0, outBytes.Length);
            }

            return output.ToArray();
        }
        catch
        {
            return wavBytes;
        }
    }
}

public sealed class MicRecorder
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _raw;
    private WaveFileWriter? _writer;
    private readonly object _sync = new();

    public bool IsRecording { get; private set; }

    public void Start()
    {
        lock (_sync)
        {
            if (IsRecording)
                return;

            _raw = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1),
                BufferMilliseconds = 40
            };

            _writer = new WaveFileWriter(new IgnoreDisposeStream(_raw), _waveIn.WaveFormat);
            _waveIn.DataAvailable += (_, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            _waveIn.StartRecording();
            IsRecording = true;
        }
    }

    public byte[] Stop()
    {
        lock (_sync)
        {
            if (!IsRecording)
                return Array.Empty<byte>();

            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;

            _writer?.Dispose();
            _writer = null;

            var bytes = _raw?.ToArray() ?? Array.Empty<byte>();
            _raw?.Dispose();
            _raw = null;
            IsRecording = false;
            return bytes;
        }
    }
}

/// <summary>Keeps the underlying MemoryStream open when WaveFileWriter disposes.</summary>
internal sealed class IgnoreDisposeStream : Stream
{
    private readonly Stream _inner;
    public IgnoreDisposeStream(Stream inner) => _inner = inner;
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
}
