using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RaceIngenior.Models;

namespace RaceIngenior.Services;

public sealed class UserPreferences
{
    public int PttJoystickOffset { get; set; }
    public string? PttDeviceNameContains { get; set; }
    public string? PttDeviceInstanceGuid { get; set; }
    public int TtsRate { get; set; } = 1;
    public bool OverlayVisible { get; set; }
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double OverlayScale { get; set; } = 1.0;
    public bool OverlayClickThrough { get; set; } = true;
    /// <summary>Bypass pit/pause/telemetry gates so overlays stay visible for layout.</summary>
    public bool OverlayForceShow { get; set; }
    public double? ConversationLeft { get; set; }
    public double? ConversationTop { get; set; }
    /// <summary>How long the radio exchange toast stays visible (5–10 s).</summary>
    public int ConversationOverlaySeconds { get; set; } = 8;
    public double? MainWindowLeft { get; set; }
    public double? MainWindowTop { get; set; }
    public double? MainWindowWidth { get; set; }
    public double? MainWindowHeight { get; set; }
    public bool MainWindowMaximized { get; set; } = true;
}

public sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ILogger<UserPreferencesStore> _logger;
    private readonly string _path;

    public UserPreferences Current { get; private set; } = new();

    public UserPreferencesStore(ILogger<UserPreferencesStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(AppContext.BaseDirectory, "Data", "user_settings.json");
        Load();
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.PttJoystickOffset = Current.PttJoystickOffset;
        if (!string.IsNullOrWhiteSpace(Current.PttDeviceNameContains))
            settings.PttDeviceNameContains = Current.PttDeviceNameContains;
    }

    public void SavePtt(int buttonOffset, string? deviceNameContains, Guid? deviceGuid)
    {
        Current.PttJoystickOffset = Math.Max(0, buttonOffset);
        Current.PttDeviceNameContains = string.IsNullOrWhiteSpace(deviceNameContains)
            ? null
            : deviceNameContains.Trim();
        Current.PttDeviceInstanceGuid = deviceGuid?.ToString("D");
        Persist();
    }

    public void SaveTtsRate(int rate)
    {
        Current.TtsRate = Math.Clamp(rate, -10, 10);
        Persist();
    }

    public void SaveOverlay(bool visible, double left, double top, double scale, bool clickThrough)
    {
        Current.OverlayVisible = visible;
        Current.OverlayLeft = left;
        Current.OverlayTop = top;
        Current.OverlayScale = Math.Clamp(scale, 0.6, 2.0);
        Current.OverlayClickThrough = clickThrough;
        Persist();
    }

    public void SaveConversationOverlay(double left, double top)
    {
        Current.ConversationLeft = left;
        Current.ConversationTop = top;
        Persist();
    }

    public void SaveConversationSeconds(int seconds)
    {
        Current.ConversationOverlaySeconds = Math.Clamp(seconds, 5, 10);
        Persist();
    }

    public void SaveOverlayForceShow(bool forceShow)
    {
        Current.OverlayForceShow = forceShow;
        Persist();
    }

    public void SaveMainWindow(double left, double top, double width, double height, bool maximized)
    {
        Current.MainWindowLeft = left;
        Current.MainWindowTop = top;
        Current.MainWindowWidth = width;
        Current.MainWindowHeight = height;
        Current.MainWindowMaximized = maximized;
        Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            var loaded = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path));
            if (loaded is not null)
            {
                loaded.OverlayScale = Math.Clamp(loaded.OverlayScale <= 0 ? 1.0 : loaded.OverlayScale, 0.6, 2.0);
                loaded.ConversationOverlaySeconds = Math.Clamp(
                    loaded.ConversationOverlaySeconds is < 5 or > 10 ? 8 : loaded.ConversationOverlaySeconds,
                    5, 10);
                Current = loaded;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load user preferences");
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save user preferences");
        }
    }
}
