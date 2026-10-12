using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;

namespace RaceIngenior.Telemetry;

public sealed class TelemetryHostedService : BackgroundService
{
    private readonly SharedMemoryTelemetrySource _source;
    private readonly LmuRestClient _rest;
    private readonly TelemetryHub _hub;
    private readonly AppSettings _settings;
    private readonly ILogger<TelemetryHostedService> _logger;

    public TelemetryHostedService(
        SharedMemoryTelemetrySource source,
        LmuRestClient rest,
        TelemetryHub hub,
        IOptions<AppSettings> settings,
        ILogger<TelemetryHostedService> logger)
    {
        _source = source;
        _rest = rest;
        _hub = hub;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var restTick = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snap = _source.TryReadSnapshot();
                if (snap is not null)
                {
                    if (restTick++ % 10 == 0)
                        await _rest.EnrichAsync(snap, stoppingToken);

                    // Rebuild with REST cache fields
                    snap = new TelemetrySnapshot
                    {
                        Timestamp = snap.Timestamp,
                        Connected = snap.Connected,
                        Track = snap.Track,
                        Vehicle = snap.Vehicle,
                        Driver = snap.Driver,
                        VehicleClass = snap.VehicleClass,
                        Session = snap.Session,
                        GamePhase = snap.GamePhase,
                        YellowFlagState = snap.YellowFlagState,
                        UnderYellow = snap.UnderYellow,
                        SectorFlags = snap.SectorFlags,
                        PrimaryFlag = snap.PrimaryFlag,
                        InPits = snap.InPits,
                        InGarage = snap.InGarage,
                        PitState = snap.PitState,
                        InRealtime = snap.InRealtime,
                        Place = snap.Place,
                        Lap = snap.Lap,
                        FinishStatus = snap.FinishStatus,
                        Gear = snap.Gear,
                        SpeedKmh = snap.SpeedKmh,
                        Rpm = snap.Rpm,
                        FuelL = snap.FuelL,
                        FuelCapacityL = snap.FuelCapacityL,
                        WaterTempC = snap.WaterTempC,
                        OilTempC = snap.OilTempC,
                        Overheating = snap.Overheating,
                        Detached = snap.Detached,
                        BatteryFraction = snap.BatteryFraction,
                        BestLap = snap.BestLap,
                        LastLap = snap.LastLap,
                        TimeBehindNext = snap.TimeBehindNext,
                        TimeBehindLeader = snap.TimeBehindLeader,
                        LapDistFraction = snap.LapDistFraction,
                        PlayerWorldX = snap.PlayerWorldX,
                        PlayerWorldZ = snap.PlayerWorldZ,
                        DarkCloud = snap.DarkCloud,
                        Raining = snap.Raining,
                        AmbientTempC = snap.AmbientTempC,
                        TrackTempC = snap.TrackTempC,
                        MinPathWetness = snap.MinPathWetness,
                        MaxPathWetness = snap.MaxPathWetness,
                        AvgPathWetness = snap.AvgPathWetness,
                        FrontTireCompound = snap.FrontTireCompound,
                        RearTireCompound = snap.RearTireCompound,
                        Wheels = snap.Wheels,
                        Nearby = snap.Nearby,
                        Field = snap.Field,
                        VirtualEnergy = RestCache.VirtualEnergy,
                        RestNotes = RestCache.LastNotes
                    };

                    _hub.Publish(snap);
                }
                else
                {
                    _hub.Publish(new TelemetrySnapshot { Connected = false });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telemetry poll failed");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_settings.TelemetryPollMs), stoppingToken);
        }
    }
}

public sealed class TelemetryHub
{
    private TelemetrySnapshot _latest = new() { Connected = false };

    public event Action<TelemetrySnapshot>? Updated;

    public TelemetrySnapshot Latest => _latest;

    public void Publish(TelemetrySnapshot snapshot)
    {
        _latest = snapshot;
        Updated?.Invoke(snapshot);
    }
}
