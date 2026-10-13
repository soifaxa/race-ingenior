using System.Text.Json.Serialization;

namespace RaceIngenior.Models;

public sealed class TelemetrySnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool Connected { get; init; }

    public string Track { get; init; } = "";
    /// <summary>Team / livery label (scoring/telem <c>mVehicleName</c>).</summary>
    public string Vehicle { get; init; } = "";
    /// <summary>LMU <c>mVehFilename</c> — identifies the car model for manufacturer logos.</summary>
    public string VehFilename { get; init; } = "";
    public string Driver { get; init; } = "";
    public string VehicleClass { get; init; } = "";

    public int Session { get; init; }
    public byte GamePhase { get; init; }

    /// <summary>English label for UI, e.g. "Green flag (5)". Unknown codes keep the number only.</summary>
    public string GamePhaseDisplay => FormatGamePhase(GamePhase);

    public static string FormatGamePhase(byte phase) => phase switch
    {
        0 => "Garage (0)",
        1 => "Warm-up (1)",
        2 => "Grid walk (2)",
        3 => "Formation (3)",
        4 => "Countdown (4)",
        5 => "Green flag (5)",
        6 => "Full course yellow (6)",
        7 => "Session stopped (7)",
        8 => "Session over (8)",
        9 => "Paused (9)",
        _ => $"Unknown ({phase})",
    };
    public sbyte YellowFlagState { get; init; }
    public bool UnderYellow { get; init; }
    /// <summary>Local yellow per sector index (0=S3, 1=S1, 2=S2) — non-zero = yellow.</summary>
    public sbyte[] SectorFlags { get; init; } = [0, 0, 0];
    /// <summary>Primary flag shown to the player: 0=green, 6=blue (rF2).</summary>
    public byte PrimaryFlag { get; init; }
    public bool HasLocalYellow =>
        SectorFlags.Length >= 3 && (SectorFlags[0] > 0 || SectorFlags[1] > 0 || SectorFlags[2] > 0);
    public bool IsBlueFlag => PrimaryFlag == 6;
    public bool IsFullCourseYellow => GamePhase == 6;
    public bool InPits { get; init; }
    public bool InGarage { get; init; }
    public byte PitState { get; init; }
    public bool InRealtime { get; init; }

    /// <summary>Garage, pit lane, or pit box — no spotter calls.</summary>
    public bool InPitsOrGarage =>
        InPits || InGarage || GamePhase == 0 || PitState is 2 or 3 or 4;

    /// <summary>Stopped in pit box / garage stall (not driving the pit lane).</summary>
    public bool InPitBox =>
        InGarage || GamePhase == 0 || PitState == 3;

    /// <summary>rF2/LMU game phase 9 = paused heartbeat.</summary>
    public bool IsPaused => GamePhase == 9 || (Connected && !InRealtime);

    /// <summary>
    /// Live on-track session (not menus, garage-before-session, pause, or session ended).
    /// GamePhase 1–6: warmup → green / FCY.
    /// </summary>
    public bool IsInLiveSession =>
        Connected && InRealtime && GamePhase is >= 1 and <= 6;

    public int Place { get; init; }
    public int Lap { get; init; }
    /// <summary>rF2/LMU <c>mFinishStatus</c>: 0=none, 1=finished, 2=dnf, 3=dq.</summary>
    public sbyte FinishStatus { get; init; }
    public bool HasFinishedRace => FinishStatus == 1;
    public bool IsOutOfSession => FinishStatus is 2 or 3;
    public int Gear { get; init; }
    public double SpeedKmh { get; init; }
    public double Rpm { get; init; }
    public double FuelL { get; init; }
    public double FuelCapacityL { get; init; }
    public double WaterTempC { get; init; }
    public double OilTempC { get; init; }
    public bool Overheating { get; init; }
    public bool Detached { get; init; }
    public double BatteryFraction { get; init; }

    public double BestLap { get; init; }
    public double LastLap { get; init; }
    public double TimeBehindNext { get; init; }
    public double TimeBehindLeader { get; init; }
    public double LapDistFraction { get; init; }
    /// <summary>Player world X (meters) from scoring — used to learn track outlines.</summary>
    public double PlayerWorldX { get; init; }
    /// <summary>Player world Z (meters) from scoring — used to learn track outlines.</summary>
    public double PlayerWorldZ { get; init; }

    public double DarkCloud { get; init; }
    public double Raining { get; init; }
    public double AmbientTempC { get; init; }
    public double TrackTempC { get; init; }
    public double MinPathWetness { get; init; }
    public double MaxPathWetness { get; init; }
    public double AvgPathWetness { get; init; }
    public string FrontTireCompound { get; init; } = "";
    public string RearTireCompound { get; init; } = "";

    public WheelTemps Wheels { get; init; } = new();
    public List<NearbyCar> Nearby { get; init; } = [];
    /// <summary>Full field for the relative track map (lap-distance positions).</summary>
    public List<TrackCar> Field { get; init; } = [];

    public double? VirtualEnergy { get; init; }
    public string? RestNotes { get; init; }

    public string ToCompactJson() => System.Text.Json.JsonSerializer.Serialize(this, CompactJsonOptions);

    private static readonly System.Text.Json.JsonSerializerOptions CompactJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

public sealed class WheelTemps
{
    public double FlWear { get; init; }
    public double FrWear { get; init; }
    public double RlWear { get; init; }
    public double RrWear { get; init; }
    public double FlBrakeC { get; init; }
    public double FrBrakeC { get; init; }
    public double RlBrakeC { get; init; }
    public double RrBrakeC { get; init; }
    public double FlTempC { get; init; }
    public double FrTempC { get; init; }
    public double RlTempC { get; init; }
    public double RrTempC { get; init; }
    public double FlCarcassC { get; init; }
    public double FrCarcassC { get; init; }
    public double RlCarcassC { get; init; }
    public double RrCarcassC { get; init; }
    /// <summary>rF2/LMU <c>mFlat</c> — punctured / deflated tyre (not lock-up flat spot).</summary>
    public bool FlFlat { get; init; }
    public bool FrFlat { get; init; }
    public bool RlFlat { get; init; }
    public bool RrFlat { get; init; }

    public bool AnyFlat => FlFlat || FrFlat || RlFlat || RrFlat;
}

public sealed class NearbyCar
{
    public string Name { get; init; } = "";
    public int Place { get; init; }
    public double RelativeLapDist { get; init; }
    public string Side { get; init; } = "";
}

/// <summary>Car on the relative track map / standings (0–1 lap distance).</summary>
public sealed class TrackCar
{
    public int SlotId { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Team / livery label from scoring <c>mVehicleName</c> (often not the manufacturer).</summary>
    public string VehicleName { get; init; } = "";
    /// <summary>.veh filename — from LMU REST <c>vehicleFilename</c> (preferred) or scoring.</summary>
    public string VehFilename { get; init; } = "";
    public string VehicleClass { get; init; } = "";
    public int Place { get; init; }
    public double LapDistFraction { get; init; }
    public bool IsPlayer { get; init; }
    public bool InPits { get; init; }
    public int PitStops { get; init; }
    public double LastLapTime { get; init; }
    public double BestLapTime { get; init; }
    public int LapsCompleted { get; init; }
    public double TimeBehindLeader { get; init; }
    public int LapsBehindLeader { get; init; }
    public double TimeBehindNext { get; init; }
    public int LapsBehindNext { get; init; }
}

/// <summary>Formatted row for the live standings panel.</summary>
public sealed class StandingEntry
{
    public int Place { get; init; }
    public string PlaceText { get; init; } = "—";
    public string Name { get; init; } = "";
    public string Brand { get; init; } = "";
    public string VehFilename { get; init; } = "";
    public string BrandHint { get; init; } = "";
    public System.Windows.Media.ImageSource? BrandLogo { get; init; }
    /// <summary>HYPERCAR / LMP2 / LMP3 / LMGT3 / OTHER — drives class colour in UI.</summary>
    public string ClassKey { get; init; } = "OTHER";
    public string ClassLabel { get; init; } = "";
    public string LastLap { get; init; } = "—";
    public string BestLap { get; init; } = "—";
    public string Gap { get; init; } = "—";
    /// <summary>Pit-stop count, or IN when currently in pits.</summary>
    public string Pit { get; init; } = "—";
    public bool InPits { get; init; }
    public bool IsPlayer { get; init; }
}
