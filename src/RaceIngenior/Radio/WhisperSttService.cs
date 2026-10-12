using System.IO;
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
    private readonly AppSettings _settings;
    private readonly ILogger<WhisperSttService> _logger;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private readonly SemaphoreSlim _initGate = new(1, 1);

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

            if (!File.Exists(path))
            {
                _logger.LogInformation("Downloading Whisper tiny model to {Path}", path);
                await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Tiny, cancellationToken: ct);
                await using var file = File.OpenWrite(path);
                await modelStream.CopyToAsync(file, ct);
            }

            _factory = WhisperFactory.FromPath(path);
            _processor = _factory.CreateBuilder()
                .WithLanguage("fr")
                .Build();

            _logger.LogInformation("Whisper ready ({Path})", path);
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        await EnsureModelAsync(ct);
        if (_processor is null)
            return "";

        await using var ms = new MemoryStream(wavBytes);
        var text = new System.Text.StringBuilder();
        await foreach (var segment in _processor.ProcessAsync(ms, ct))
            text.Append(segment.Text);

        return text.ToString().Trim();
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
            await _processor.DisposeAsync();
        _factory?.Dispose();
        _initGate.Dispose();
    }

    /// <summary>
    /// Resolves a relative Whisper path outside bin/: prefers an existing file under
    /// LocalAppData, repo Models/, or project folder; otherwise installs under LocalAppData.
    /// </summary>
    internal static string ResolveModelPath(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            configured = "Models/ggml-tiny.bin";

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

        // bin/Debug/net8.0-windows → repo root (Models/ggml-tiny.bin from install script)
        var repoGuess = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (Directory.Exists(repoGuess))
            yield return repoGuess;

        // Project dir (src/RaceIngenior) — optional local Models/
        var projectGuess = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        if (Directory.Exists(projectGuess))
            yield return projectGuess;
    }

    private static string LocalAppRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaceIngenior");
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
                BufferMilliseconds = 50
            };

            // Write WAV header + PCM into memory
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
