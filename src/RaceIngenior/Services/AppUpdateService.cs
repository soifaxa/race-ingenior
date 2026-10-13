using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;
using Velopack;
using Velopack.Sources;

namespace RaceIngenior.Services;

public sealed class AppUpdateService
{
    private readonly ILogger<AppUpdateService> _logger;
    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public AppUpdateService(IOptions<AppSettings> settings, ILogger<AppUpdateService> logger)
    {
        _logger = logger;
        var repoUrl = string.IsNullOrWhiteSpace(settings.Value.UpdateGithubRepoUrl)
            ? "https://github.com/soifaxa/race-ingenior"
            : settings.Value.UpdateGithubRepoUrl.Trim();

        // Public repo: no token. prerelease: false → stable Releases only.
        var source = new GithubSource(repoUrl, accessToken: null, prerelease: false);
        _manager = new UpdateManager(source);
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersionDisplay =>
        _manager.IsInstalled && _manager.CurrentVersion is not null
            ? _manager.CurrentVersion.ToString()
            : "(dev build — not installed)";

    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Checks GitHub Releases for a newer package. Returns null when up to date or not installed.</summary>
    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled)
        {
            return new AppUpdateCheckResult(
                IsInstalled: false,
                UpdateAvailable: false,
                CurrentVersion: null,
                AvailableVersion: null,
                Message: "Updates are available only from an installed build (Setup.exe). This is a local/dev run.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (_pending is null)
            {
                return new AppUpdateCheckResult(
                    IsInstalled: true,
                    UpdateAvailable: false,
                    CurrentVersion: _manager.CurrentVersion?.ToString(),
                    AvailableVersion: null,
                    Message: $"You are up to date ({_manager.CurrentVersion}).");
            }

            var available = _pending.TargetFullRelease.Version.ToString();
            _logger.LogInformation("Update available: {Version}", available);
            return new AppUpdateCheckResult(
                IsInstalled: true,
                UpdateAvailable: true,
                CurrentVersion: _manager.CurrentVersion?.ToString(),
                AvailableVersion: available,
                Message: $"Update available: {available} (current {_manager.CurrentVersion}).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return new AppUpdateCheckResult(
                IsInstalled: true,
                UpdateAvailable: false,
                CurrentVersion: _manager.CurrentVersion?.ToString(),
                AvailableVersion: null,
                Message: $"Update check failed: {ex.Message}");
        }
    }

    /// <summary>Downloads the pending update then exits, applies, and restarts.</summary>
    public async Task ApplyPendingUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_pending is null)
            throw new InvalidOperationException("No pending update. Call CheckForUpdatesAsync first.");

        cancellationToken.ThrowIfCancellationRequested();
        await _manager.DownloadUpdatesAsync(_pending, progress is null ? null : progress.Report)
            .ConfigureAwait(false);
        _manager.ApplyUpdatesAndRestart(_pending);
    }
}

public sealed record AppUpdateCheckResult(
    bool IsInstalled,
    bool UpdateAvailable,
    string? CurrentVersion,
    string? AvailableVersion,
    string Message);
