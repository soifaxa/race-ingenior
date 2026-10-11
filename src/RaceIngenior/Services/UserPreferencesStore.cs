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
    public bool OverlayClickThrough { get; set; } = true;
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

    public void SaveOverlay(bool visible, double left, double top, bool clickThrough)
    {
        Current.OverlayVisible = visible;
        Current.OverlayLeft = left;
        Current.OverlayTop = top;
        Current.OverlayClickThrough = clickThrough;
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
                Current = loaded;
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
