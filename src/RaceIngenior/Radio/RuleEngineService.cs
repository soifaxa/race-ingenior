using Microsoft.Extensions.Options;
using RaceIngenior.Models;

namespace RaceIngenior.Radio;

/// <summary>
/// Local low-cost alerts (works offline, bypasses Gemini when urgent).
/// </summary>
public sealed class RuleEngineService
{
    private static readonly TimeSpan SameAlertCooldown = TimeSpan.FromMinutes(1);

    private readonly AppSettings _settings;
    private string? _lastAlert;
    private DateTimeOffset _lastAlertAt = DateTimeOffset.MinValue;
    private byte _lastGamePhase = 255;
    private sbyte _lastYellowState = -2;
    private byte _lastPrimaryFlag = 255;
    private readonly bool[] _lastSectorYellow = [false, false, false];
    private bool _wasInPitsOrGarage = true;
    private bool _warmupActive;
    private bool _coldExitWarned;
    private bool _warmReadyWarned;

    public RuleEngineService(IOptions<AppSettings> settings)
    {
        _settings = settings.Value;
    }

    public string? Evaluate(TelemetrySnapshot snap)
    {
        if (!snap.IsInLiveSession)
        {
            // Menus / before session / pause / session over: no callouts.
            _wasInPitsOrGarage = true;
            _warmupActive = false;
            _coldExitWarned = false;
            _warmReadyWarned = false;
            _lastGamePhase = snap.GamePhase;
            _lastYellowState = snap.YellowFlagState;
            _lastPrimaryFlag = snap.PrimaryFlag;
            SyncSectorYellow(snap.SectorFlags);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var inStands = snap.InPitsOrGarage;
        string? alert = null;

        // Pit exit → start cold-tire procedure when temps are low.
        if (_wasInPitsOrGarage && !inStands)
        {
            if (TryGetTireTemps(snap.Wheels, out var minExit, out _))
            {
                _warmupActive = minExit < _settings.TireWarmReadyC;
                _coldExitWarned = false;
                _warmReadyWarned = false;

                if (_warmupActive && minExit < _settings.TireColdBelowC)
                {
                    alert = "Pneus froids. N'attaque pas : ménage pneus et freinages.";
                    _coldExitWarned = true;
                }
            }
        }
        else if (inStands)
        {
            _warmupActive = false;
            _coldExitWarned = false;
            _warmReadyWarned = false;
        }

        if (alert is null && TryDescribePunctures(snap.Wheels) is { } puncture)
            alert = puncture;
        if (alert is null)
            alert = EvaluateFlags(snap);
        if (alert is null && snap.Overheating)
            alert = "Attention, moteur en surchauffe.";
        if (alert is null && snap.OilTempC > 140)
            alert = "Température huile critique.";
        if (alert is null && snap.WaterTempC > 120)
            alert = "Température eau élevée.";
        if (alert is null && snap.FuelCapacityL > 0 && snap.FuelL / snap.FuelCapacityL < 0.08)
            alert = "Carburant bas. Prépare la box.";
        if (alert is null && IsTireWearCritical(snap.Wheels))
            alert = "Usure pneus critique.";
        if (alert is null && IsTireWearWarning(snap.Wheels))
            alert = "Attention, usure pneus à cinquante pourcent.";
        if (alert is null && IsTireWearInfo(snap.Wheels))
            alert = "Info, usure pneus à soixante-dix pourcent.";
        if (alert is null && snap.Detached)
            alert = "Dégâts détectés. Vérifie la voiture.";
        if (alert is null && _warmupActive && !inStands)
            alert = EvaluateWarmup(snap);
        if (alert is null && !inStands)
            alert = EvaluateSpotter(snap);

        // Keep flag trackers in sync even when another alert won this tick.
        _wasInPitsOrGarage = inStands;
        _lastGamePhase = snap.GamePhase;
        _lastYellowState = snap.YellowFlagState;
        _lastPrimaryFlag = snap.PrimaryFlag;
        SyncSectorYellow(snap.SectorFlags);

        if (alert is null)
            return null;

        if (alert == _lastAlert && now - _lastAlertAt < SameAlertCooldown)
            return null;

        _lastAlert = alert;
        _lastAlertAt = now;
        return alert;
    }

    private string? EvaluateFlags(TelemetrySnapshot snap)
    {
        // Full course yellow / safety car.
        if (snap.GamePhase == 6 && _lastGamePhase != 6)
            return "Full course yellow. Safety car. Ralentis.";

        // Green after FCY.
        if (_lastGamePhase == 6 && snap.GamePhase == 5)
            return "Green flag. Course relancée.";

        // Session stopped / red-like halt.
        if (snap.GamePhase == 7 && _lastGamePhase != 7)
            return "Session stoppée. Arrête-toi en sécurité.";

        // FCY yellow-state transitions (pits / restart).
        if (snap.IsFullCourseYellow || snap.YellowFlagState > 0)
        {
            if (snap.YellowFlagState != _lastYellowState)
            {
                var yel = DescribeYellowState(snap.YellowFlagState);
                if (yel is not null)
                    return yel;
            }
        }

        // Local sector yellow (skip while already under FCY — covered above).
        if (!snap.IsFullCourseYellow)
        {
            var local = DescribeNewLocalYellow(snap.SectorFlags);
            if (local is not null)
                return local;
        }

        // Blue flag shown to the player.
        if (snap.IsBlueFlag && _lastPrimaryFlag != 6)
            return "Drapeau bleu. Laisse passer plus vite.";

        return null;
    }

    private static string? DescribeYellowState(sbyte state) => state switch
    {
        1 => "Yellow pending. Prudence, possible safety car.",
        2 => "Full course yellow. Box fermée.",
        3 => "Yellow. Lead lap aux stands.",
        4 => "Yellow. Box ouverte.",
        5 => "Yellow. Dernier tour sous safety car.",
        6 => "Yellow resume. Prépare le restart.",
        7 => "Race halt. Immobilise la voiture.",
        _ => null
    };

    private string? DescribeNewLocalYellow(sbyte[] flags)
    {
        if (flags.Length < 3)
            return null;

        var newly = new List<string>(3);
        for (var i = 0; i < 3; i++)
        {
            var on = flags[i] > 0;
            if (on && !_lastSectorYellow[i])
                newly.Add(SectorLabel(i));
        }

        if (newly.Count == 0)
            return null;

        return newly.Count == 1
            ? $"Yellow local secteur {newly[0]}. Prudence."
            : $"Yellow local secteurs {string.Join(", ", newly)}. Prudence.";
    }

    private void SyncSectorYellow(sbyte[] flags)
    {
        for (var i = 0; i < 3; i++)
            _lastSectorYellow[i] = flags.Length > i && flags[i] > 0;
    }

    /// <summary>rF2 sector flag index: 0=S3, 1=S1, 2=S2.</summary>
    private static string SectorLabel(int index) => index switch
    {
        1 => "1",
        2 => "2",
        0 => "3",
        _ => "?"
    };

    private string? EvaluateWarmup(TelemetrySnapshot snap)
    {
        if (!TryGetTireTemps(snap.Wheels, out var minT, out var avgT))
            return null;

        if (!_coldExitWarned && minT < _settings.TireColdBelowC)
        {
            _coldExitWarned = true;
            return "Pneus froids. N'attaque pas : ménage pneus et freinages.";
        }

        if (!_warmReadyWarned
            && minT >= _settings.TireWarmReadyC
            && avgT >= _settings.TireWarmReadyC)
        {
            _warmReadyWarned = true;
            _warmupActive = false;
            return "Pneus en température. Tu peux attaquer.";
        }

        return null;
    }

    private static string? EvaluateSpotter(TelemetrySnapshot snap)
    {
        var close = snap.Nearby.FirstOrDefault(n => Math.Abs(n.RelativeLapDist) < 12);
        if (close is null)
            return null;

        if (close.RelativeLapDist < 0 && close.Side is "left" or "right")
            return $"Trafic {SideFr(close.Side)}, proche.";
        if (close.RelativeLapDist < -5)
            return "Voiture derrière, proche.";
        return null;
    }

    private static bool TryGetTireTemps(WheelTemps w, out double minC, out double avgC)
    {
        var temps = new[] { w.FlCarcassC, w.FrCarcassC, w.RlCarcassC, w.RrCarcassC };
        var valid = temps.Where(t => !double.IsNaN(t) && t is > 15 and < 180).ToArray();
        if (valid.Length < 4)
        {
            minC = 0;
            avgC = 0;
            return false;
        }

        minC = valid.Min();
        avgC = valid.Average();
        return true;
    }

    private static string? TryDescribePunctures(WheelTemps w)
    {
        if (!w.AnyFlat)
            return null;

        var corners = new List<string>(4);
        if (w.FlFlat) corners.Add("avant gauche");
        if (w.FrFlat) corners.Add("avant droit");
        if (w.RlFlat) corners.Add("arrière gauche");
        if (w.RrFlat) corners.Add("arrière droit");

        if (corners.Count == 4)
            return "Pneus à plat. Rentre immédiatement à la box.";

        return corners.Count == 1
            ? $"Pneu à plat {corners[0]}. Rentre à la box."
            : $"Pneus à plat {string.Join(", ", corners)}. Rentre à la box.";
    }

    private static bool IsTireWearCritical(WheelTemps w) =>
        HasWearBelow(w, remainingBelow: 0.30);

    private static bool IsTireWearWarning(WheelTemps w) =>
        HasWearBelow(w, remainingBelow: 0.50);

    private static bool IsTireWearInfo(WheelTemps w) =>
        HasWearBelow(w, remainingBelow: 0.70);

    private static bool HasWearBelow(WheelTemps w, double remainingBelow) =>
        IsLowRemaining(w.FlWear, remainingBelow)
        || IsLowRemaining(w.FrWear, remainingBelow)
        || IsLowRemaining(w.RlWear, remainingBelow)
        || IsLowRemaining(w.RrWear, remainingBelow);

    private static bool IsLowRemaining(double wear, double threshold)
    {
        if (double.IsNaN(wear) || wear < 0 || wear > 1.0001)
            return false;
        return wear < threshold;
    }

    private static string SideFr(string side) => side switch
    {
        "left" => "à gauche",
        "right" => "à droite",
        _ => side
    };
}
