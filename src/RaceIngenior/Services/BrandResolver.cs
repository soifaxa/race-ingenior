namespace RaceIngenior.Services;

/// <summary>
/// Maps LMU vehicle identity strings to manufacturer brand keys (Assets/Brands/*.svg stems).
/// Prefer model package / <c>vehicleFilename</c> over opaque REST <c>carId</c> hashes;
/// GT3 team liveries are a last-resort map when identity is hash-only.
/// </summary>
public static class BrandResolver
{
    // Longest needles first so "Aston Martin" wins over "Martin", etc.
    private static readonly (string Needle, string Key)[] Brands =
    [
        ("Isotta Fraschini", "Isotta Fraschini"),
        ("IsottaFraschini", "Isotta Fraschini"),
        ("Isotta_Tipo", "Isotta Fraschini"),
        ("Aston Martin", "Aston Martin"),
        ("AstonMartin", "Aston Martin"),
        ("Aston_Martin", "Aston Martin"),
        ("Vantage_AMR", "Aston Martin"),
        ("VantageAMR", "Aston Martin"),
        ("Mercedes-AMG", "Mercedes-AMG"),
        ("Mercedes AMG", "Mercedes-AMG"),
        ("MercedesAMG", "Mercedes-AMG"),
        ("Mercedes_AMG", "Mercedes-AMG"),
        ("AMGGT3", "Mercedes-AMG"),
        ("Mercedes", "Mercedes-AMG"),
        ("Lamborghini", "Lamborghini"),
        ("Huracan", "Lamborghini"),
        ("Glickenhaus", "Glickenhaus"),
        ("SGC_007", "Glickenhaus"),
        ("Cadillac", "Cadillac"),
        ("Chevrolet", "Chevrolet"),
        ("Corvette", "Corvette"),
        ("Z06GT3", "Corvette"),
        ("Ferrari", "Ferrari"),
        ("296GT3", "Ferrari"),
        ("Porsche", "Porsche"),
        ("911GT3", "Porsche"),
        ("911_GT3", "Porsche"),
        ("911RSR", "Porsche"),
        ("Peugeot", "Peugeot"),
        ("Duqueine", "Duqueine"),
        ("Genesis", "Genesis"),
        ("McLaren", "McLaren"),
        ("720sGT3", "McLaren"),
        ("720sGChallenge", "McLaren"),
        ("720S", "McLaren"),
        ("Vanwall", "Vanwall"),
        ("Vandervell", "Vanwall"),
        ("Ginetta", "Ginetta"),
        ("Alpine", "Alpine"),
        ("Toyota", "Toyota"),
        ("Ligier", "Ligier"),
        ("Lexus", "Lexus"),
        ("LexusRCF", "Lexus"),
        ("RCF_GT3", "Lexus"),
        ("Oreca", "Oreca"),
        ("ADESS", "ADESS"),
        ("BMW_M4", "BMW"),
        ("M4_LMGT3", "BMW"),
        ("BMW", "BMW"),
        ("Ford_Mustang", "Ford"),
        ("Mustang_GT3", "Ford"),
        ("Mustang", "Ford"),
        ("Ford", "Ford"),
    ];

    /// <summary>
    /// LMU GT3 / Hypercar liveries whose <c>vehicleName</c> is team-only while REST <c>carId</c> is a content hash.
    /// </summary>
    private static readonly (string Needle, string Key)[] Liveries =
    [
        ("Vista AF Corse", "Ferrari"),
        ("AF Corse", "Ferrari"),
        ("Spirit of Race", "Ferrari"),
        ("Rinaldi", "Ferrari"),
        ("Kessel", "Ferrari"),
        ("Manthey", "Porsche"),
        ("AO Racing", "Porsche"),
        ("Dinamic", "Porsche"),
        ("Pure Rxcing", "Porsche"),
        ("Proton Competition", "Ford"),
        ("Proton", "Ford"),
        ("Racing Team Turkey", "Corvette"),
        ("13 Autosport", "Corvette"),
        ("TF Sport", "Corvette"),
        ("TFSP", "Corvette"),
        ("13AU", "Corvette"),
        ("Akkodis", "Lexus"),
        ("Heart of Racing", "Aston Martin"),
        ("D'Station", "Aston Martin"),
        ("DStation", "Aston Martin"),
        ("Iron Lynx", "Lamborghini"),
        ("Iron Dames", "Lamborghini"),
        ("GetSpeed", "Mercedes-AMG"),
        ("Team WRT", "BMW"),
        ("WRT", "BMW"),
        ("BMW M Team", "BMW"),
        ("Inception", "McLaren"),
        ("United Autosports", "McLaren"),
        ("Garage 59", "McLaren"),
        ("Toyota Gazoo", "Toyota"),
        ("Peugeot TotalEnergies", "Peugeot"),
        ("Cadillac Hertz", "Cadillac"),
        ("Porsche Penske", "Porsche"),
        ("Alpine Endurance", "Alpine"),
        ("BMW M Team WRT", "BMW"),
        ("Lamborghini Iron Lynx", "Lamborghini"),
        ("Isotta Fraschini", "Isotta Fraschini"),
    ];

    /// <summary>
    /// Resolve brand from LMU identity fields.
    /// Order: single-make class → model/filename (skip opaque hashes) → brand needles → team livery map.
    /// </summary>
    public static string ResolveKey(
        string? vehFilename,
        string? vehicleModel = null,
        string? vehicleName = null,
        string? vehicleClass = null)
    {
        var fromClass = ResolveFromClass(vehicleClass);
        if (!string.IsNullOrEmpty(fromClass))
            return fromClass;

        foreach (var candidate in Candidates(vehFilename, vehicleModel, vehicleName))
        {
            var key = MatchTable(candidate, Brands);
            if (!string.IsNullOrEmpty(key))
                return key;
        }

        // Team/livery names only (GT3) — after model tokens failed.
        if (!string.IsNullOrWhiteSpace(vehicleName))
        {
            var key = MatchTable(vehicleName.Trim(), Liveries);
            if (!string.IsNullOrEmpty(key))
                return key;
        }

        return "";
    }

    /// <summary>Legacy helper: single blob (filename preferred if it looks like a .veh).</summary>
    public static string ResolveKey(string? text) => ResolveKey(text, null, text);

    public static string ResolveDisplay(
        string? vehFilename,
        string? vehicleModel = null,
        string? vehicleName = null,
        string? vehicleClass = null)
    {
        var key = ResolveKey(vehFilename, vehicleModel, vehicleName, vehicleClass);
        return string.IsNullOrEmpty(key) ? "" : key;
    }

    public static string ResolveDisplay(string? text) => ResolveDisplay(text, null, text);

    /// <summary>
    /// Glanceable car line: <c>Ferrari LMGT3</c>, <c>Aston Martin HYPER</c>, <c>Oreca LMP2</c>.
    /// </summary>
    public static string FormatClassAndBrand(
        string? vehFilename = null,
        string? vehicleModel = null,
        string? vehicleName = null,
        string? vehicleClass = null)
    {
        var brand = ResolveDisplay(vehFilename, vehicleModel, vehicleName, vehicleClass);
        return FormatClassAndBrandLabel(vehicleClass, brand);
    }

    /// <summary>Compose already-resolved brand + class label.</summary>
    public static string FormatClassAndBrandLabel(string? vehicleClass, string? brand)
    {
        var (_, label) = Classify(vehicleClass);
        var b = string.IsNullOrWhiteSpace(brand) ? "" : brand.Trim();
        if (b.Length == 0)
            return label is "—" or "" ? "—" : label;
        if (label is "—" or "")
            return b;
        return $"{b} {label}";
    }

    /// <summary>Class key + short UI label (HYPER / LMP2 / LMP3 / LMGT3).</summary>
    public static (string Key, string Label) Classify(string? vehicleClass)
    {
        var c = (vehicleClass ?? "").Trim().ToUpperInvariant();
        if (c.Length == 0)
            return ("OTHER", "—");

        if (c.Contains("HYPER", StringComparison.Ordinal)
            || c.Contains("LMH", StringComparison.Ordinal)
            || c.Contains("LMDH", StringComparison.Ordinal)
            || c.Contains("GTP", StringComparison.Ordinal))
            return ("HYPERCAR", "HYPER");

        if (c.Contains("LMP3", StringComparison.Ordinal))
            return ("LMP3", "LMP3");

        if (c.Contains("LMP2", StringComparison.Ordinal))
            return ("LMP2", "LMP2");

        if (c.Contains("LMGT3", StringComparison.Ordinal)
            || c.Contains("GT3", StringComparison.Ordinal)
            || c.Contains("GTE", StringComparison.Ordinal))
            return ("LMGT3", "LMGT3");

        var shortLabel = c.Length <= 8 ? c : c[..8];
        return ("OTHER", shortLabel);
    }

    /// <summary>True when REST identity is an opaque content hash (not a model/package name).</summary>
    public static bool IsOpaqueId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var s = text.Trim();
        if (s.EndsWith(".veh", StringComparison.OrdinalIgnoreCase))
            s = s[..^4];
        if (s.Length is < 32 or > 64)
            return false;
        foreach (var c in s)
        {
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Prefer a readable model/filename over an opaque REST <c>carId</c> hash.
    /// </summary>
    public static string? PreferIdentity(string? vehicleFilename, string? carId)
    {
        var file = NullIfEmpty(vehicleFilename);
        var id = NullIfEmpty(carId);

        foreach (var c in new[] { file, id })
        {
            if (c is null || IsOpaqueId(c))
                continue;
            if (!string.IsNullOrEmpty(ResolveKey(c)))
                return c;
        }

        foreach (var c in new[] { file, id })
        {
            if (c is not null && !IsOpaqueId(c))
                return c;
        }

        return file ?? id;
    }

    /// <summary>LMP2 / LMP3 are single-make: Oreca / Ligier.</summary>
    private static string ResolveFromClass(string? vehicleClass)
    {
        var c = (vehicleClass ?? "").Trim().ToUpperInvariant();
        if (c.Length == 0)
            return "";
        if (c.Contains("LMP3", StringComparison.Ordinal))
            return "Ligier";
        if (c.Contains("LMP2", StringComparison.Ordinal))
            return "Oreca";
        return "";
    }

    private static IEnumerable<string> Candidates(string? vehFilename, string? vehicleModel, string? vehicleName)
    {
        if (!string.IsNullOrWhiteSpace(vehFilename) && !IsOpaqueId(vehFilename))
            yield return NormalizeVehToken(vehFilename);
        if (!string.IsNullOrWhiteSpace(vehicleModel) && !IsOpaqueId(vehicleModel))
            yield return vehicleModel.Trim();
        if (!string.IsNullOrWhiteSpace(vehicleName))
            yield return vehicleName.Trim();
    }

    private static string NormalizeVehToken(string raw)
    {
        var s = raw.Trim();
        if (s.EndsWith(".veh", StringComparison.OrdinalIgnoreCase))
            s = s[..^4];
        // Paths → leaf name
        var slash = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
        if (slash >= 0 && slash < s.Length - 1)
            s = s[(slash + 1)..];
        return s;
    }

    private static string MatchTable(string text, (string Needle, string Key)[] table)
    {
        foreach (var (needle, key) in table)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        return "";
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
