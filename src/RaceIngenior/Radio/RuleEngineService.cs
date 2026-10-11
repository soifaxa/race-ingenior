using Microsoft.Extensions.Options;
using RaceIngenior.Models;

namespace RaceIngenior.Radio;

/// <summary>
/// Local low-cost alerts (works offline, bypasses Gemini when urgent).
/// </summary>
public sealed class RuleEngineService
{
    private readonly AppSettings _settings;
    private string? _lastAlert;
    private DateTimeOffset _lastAlertAt = DateTimeOffset.MinValue;
    private byte _lastGamePhase = 255;
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
        if (!snap.Connected)
            return null;

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

        if (alert is null && snap.GamePhase == 6 && _lastGamePhase != 6)
            alert = "Full course yellow. Safety car.";
        else if (alert is null && snap.Overheating)
            alert = "Attention, moteur en surchauffe.";
        else if (alert is null && snap.OilTempC > 140)
            alert = "Température huile critique.";
        else if (alert is null && snap.WaterTempC > 120)
            alert = "Température eau élevée.";
        else if (alert is null && snap.FuelCapacityL > 0 && snap.FuelL / snap.FuelCapacityL < 0.08)
            alert = "Carburant bas. Prépare la box.";
        else if (alert is null && IsTireWearCritical(snap.Wheels))
            alert = "Usure pneus critique.";
        else if (alert is null && snap.Detached)
            alert = "Dégâts détectés. Vérifie la voiture.";
        else if (alert is null && _warmupActive && !inStands)
            alert = EvaluateWarmup(snap);
        else if (alert is null && !inStands)
            alert = EvaluateSpotter(snap);

        _wasInPitsOrGarage = inStands;
        _lastGamePhase = snap.GamePhase;

        if (alert is null)
            return null;

        if (alert == _lastAlert && now - _lastAlertAt < TimeSpan.FromSeconds(8))
            return null;

        _lastAlert = alert;
        _lastAlertAt = now;
        return alert;
    }

    private string? EvaluateWarmup(TelemetrySnapshot snap)
    {
        if (!TryGetTireTemps(snap.Wheels, out var minT, out var avgT))
            return null;

        if (!_coldExitWarned && minT < _settings.TireColdBelowC)
        {
            _coldExitWarned = true;
            return "Pneus froids. N'attaque pas : ménage pneus et freinages.";
        }

        // Ready when coldest tire reaches the warm window (and average is healthy).
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

    /// <summary>Warmup decisions use carcass temps (not surface).</summary>
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

    private static bool IsTireWearCritical(WheelTemps w)
    {
        const double criticalRemaining = 0.15;
        return IsLowRemaining(w.FlWear, criticalRemaining)
               || IsLowRemaining(w.FrWear, criticalRemaining)
               || IsLowRemaining(w.RlWear, criticalRemaining)
               || IsLowRemaining(w.RrWear, criticalRemaining);
    }

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
