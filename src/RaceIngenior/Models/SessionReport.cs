namespace RaceIngenior.Models;

public sealed class SessionReport
{
    public string ReportId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset EndedUtc { get; set; }
    public string EndReason { get; set; } = "";

    public string Track { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string VehicleClass { get; set; } = "";
    public string Driver { get; set; } = "";
    public string SessionType { get; set; } = "";
    public int SessionCode { get; set; }

    public int LapsCompleted { get; set; }
    public int BestPlace { get; set; }
    public int FinalPlace { get; set; }
    public double BestLapSec { get; set; }
    public double LastLapSec { get; set; }
    public double DurationSec { get; set; }

    public string FrontCompound { get; set; } = "";
    public string RearCompound { get; set; } = "";
    public double? WarmupSeconds { get; set; }
    public int? WarmupLaps { get; set; }
    public double? WarmupReadyCarcassC { get; set; }
    public string WarmupNotes { get; set; } = "";

    public double FuelStartL { get; set; }
    public double FuelEndL { get; set; }
    public double FuelUsedL { get; set; }
    public double FuelCapacityL { get; set; }

    public double? VirtualEnergyStart { get; set; }
    public double? VirtualEnergyEnd { get; set; }
    public double? VirtualEnergyUsed { get; set; }

    public WheelWearSnapshot WearStart { get; set; } = new();
    public WheelWearSnapshot WearEnd { get; set; } = new();
    public WheelTempSnapshot CarcassMaxC { get; set; } = new();
    public WheelTempSnapshot CarcassMinC { get; set; } = new();

    public bool HadPuncture { get; set; }
    public int FullCourseYellowCount { get; set; }
    public int BlueFlagCount { get; set; }
    public int LocalYellowCount { get; set; }

    public double TrackTempStartC { get; set; }
    public double TrackTempEndC { get; set; }
    public double TrackTempMinC { get; set; }
    public double TrackTempMaxC { get; set; }
    public double TrackTempAvgC { get; set; }
    public double AmbientTempStartC { get; set; }
    public double AmbientTempEndC { get; set; }
    public double AmbientTempMinC { get; set; }
    public double AmbientTempMaxC { get; set; }
    public double AmbientTempAvgC { get; set; }
    public double RainEnd { get; set; }
    public double AvgWetnessEnd { get; set; }
    public string RecommendedCompoundEnd { get; set; } = "";
}

public sealed class WheelWearSnapshot
{
    public double Fl { get; set; }
    public double Fr { get; set; }
    public double Rl { get; set; }
    public double Rr { get; set; }

    public double Min => Math.Min(Math.Min(Fl, Fr), Math.Min(Rl, Rr));
}

public sealed class WheelTempSnapshot
{
    public double Fl { get; set; }
    public double Fr { get; set; }
    public double Rl { get; set; }
    public double Rr { get; set; }
}
