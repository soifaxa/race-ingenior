using System.Text.Json.Serialization;

namespace RaceIngenior.Models;

public sealed class TelemetrySnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool Connected { get; init; }

    public string Track { get; init; } = "";
    public string Vehicle { get; init; } = "";
    public string Driver { get; init; } = "";
    public string VehicleClass { get; init; } = "";

    public int Session { get; init; }
    public byte GamePhase { get; init; }
    public sbyte YellowFlagState { get; init; }
    public bool UnderYellow { get; init; }
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

    public int Place { get; init; }
    public int Lap { get; init; }
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

    public WheelTemps Wheels { get; init; } = new();
    public List<NearbyCar> Nearby { get; init; } = [];

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
}

public sealed class NearbyCar
{
    public string Name { get; init; } = "";
    public int Place { get; init; }
    public double RelativeLapDist { get; init; }
    public string Side { get; init; } = "";
}
