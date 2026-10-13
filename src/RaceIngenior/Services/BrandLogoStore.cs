using System.IO;
using System.Windows.Media;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace RaceIngenior.Services;

/// <summary>Loads manufacturer SVG logos from Assets/Brands (copied next to the exe).</summary>
public sealed class BrandLogoStore
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _brandsDir;
    private readonly object _gate = new();

    public BrandLogoStore()
    {
        _brandsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Brands");
    }

    public ImageSource? GetLogoForVehicle(string? vehicleName, string? vehicleClass = null) =>
        GetLogoForIdentity(vehFilename: null, vehicleModel: null, vehicleName: vehicleName, vehicleClass: vehicleClass);

    public ImageSource? GetLogoForIdentity(
        string? vehFilename,
        string? vehicleModel = null,
        string? vehicleName = null,
        string? vehicleClass = null)
    {
        var key = BrandResolver.ResolveKey(vehFilename, vehicleModel, vehicleName, vehicleClass);
        // Prefer no glyph over Default when identity is unknown (team-only GT3 names, etc.).
        return string.IsNullOrEmpty(key) ? null : GetLogo(key);
    }

    public ImageSource? GetLogo(string? brandKey)
    {
        if (string.IsNullOrWhiteSpace(brandKey))
            return null;

        lock (_gate)
        {
            if (_cache.TryGetValue(brandKey, out var cached))
                return cached;

            var path = Path.Combine(_brandsDir, brandKey + ".svg");
            var image = LoadSvg(path);
            if (image is null && !brandKey.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                if (!_cache.TryGetValue("Default", out image))
                {
                    image = LoadSvg(Path.Combine(_brandsDir, "Default.svg"));
                    _cache["Default"] = image;
                }
            }

            _cache[brandKey] = image;
            return image;
        }
    }

    private static ImageSource? LoadSvg(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var settings = new WpfDrawingSettings
            {
                IncludeRuntime = false,
                TextAsGeometry = true
            };
            var reader = new FileSvgReader(settings);
            var drawing = reader.Read(path);
            if (drawing is null)
                return null;

            var image = new DrawingImage(drawing);
            if (image.CanFreeze)
                image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
