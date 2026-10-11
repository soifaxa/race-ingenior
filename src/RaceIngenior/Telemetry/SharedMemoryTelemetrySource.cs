using System.Text;
using Microsoft.Extensions.Logging;
using RaceIngenior.Models;
using RaceIngenior.Telemetry.rFactor2Data;
using static RaceIngenior.Telemetry.rFactor2Constants;

namespace RaceIngenior.Telemetry;

public sealed class SharedMemoryTelemetrySource : IDisposable
{
    private readonly ILogger<SharedMemoryTelemetrySource> _logger;
    private readonly MappedBuffer<rF2Telemetry> _telemetry = new(MM_TELEMETRY_FILE_NAME, partial: true);
    private readonly MappedBuffer<rF2Scoring> _scoring = new(MM_SCORING_FILE_NAME, partial: true);
    private readonly MappedBuffer<rF2Rules> _rules = new(MM_RULES_FILE_NAME, partial: true);

    private rF2Telemetry _telem;
    private rF2Scoring _scoringData;
    private rF2Rules _rulesData;

    public SharedMemoryTelemetrySource(ILogger<SharedMemoryTelemetrySource> logger)
    {
        _logger = logger;
    }

    public bool Connected { get; private set; }

    public bool EnsureConnected()
    {
        if (Connected && _telemetry.IsConnected && _scoring.IsConnected)
            return true;

        var ok = _telemetry.TryConnect() && _scoring.TryConnect();
        _rules.TryConnect();
        Connected = ok;
        if (ok)
            _logger.LogInformation("Connected to LMU/rF2 shared memory");
        return ok;
    }

    public TelemetrySnapshot? TryReadSnapshot()
    {
        if (!EnsureConnected())
            return null;

        if (!_scoring.TryRead(ref _scoringData) || !_telemetry.TryRead(ref _telem))
        {
            Connected = false;
            _telemetry.Disconnect();
            _scoring.Disconnect();
            _rules.Disconnect();
            return null;
        }

        _rules.TryRead(ref _rulesData);

        var playerScoring = FindPlayerScoring(out _);
        if (playerScoring is null)
            return new TelemetrySnapshot { Connected = true, Track = Decode(_scoringData.mScoringInfo.mTrackName) };

        var playerTelem = FindTelemetry(playerScoring.Value.mID) ?? ( _telem.mNumVehicles > 0 ? _telem.mVehicles[0] : default);
        var speedMs = Math.Sqrt(
            playerTelem.mLocalVel.x * playerTelem.mLocalVel.x +
            playerTelem.mLocalVel.y * playerTelem.mLocalVel.y +
            playerTelem.mLocalVel.z * playerTelem.mLocalVel.z);

        var wheels = playerTelem.mWheels ?? Array.Empty<rF2Wheel>();
        var fl = wheels.ElementAtOrDefault(0);
        var fr = wheels.ElementAtOrDefault(1);
        var rl = wheels.ElementAtOrDefault(2);
        var rr = wheels.ElementAtOrDefault(3);

        var lapDist = _scoringData.mScoringInfo.mLapDist;
        var nearby = BuildNearby(playerScoring.Value, lapDist);

        return new TelemetrySnapshot
        {
            Connected = true,
            Track = Decode(_scoringData.mScoringInfo.mTrackName),
            Vehicle = Decode(playerTelem.mVehicleName),
            Driver = Decode(playerScoring.Value.mDriverName),
            VehicleClass = Decode(playerScoring.Value.mVehicleClass),
            Session = _scoringData.mScoringInfo.mSession,
            GamePhase = _scoringData.mScoringInfo.mGamePhase,
            YellowFlagState = _scoringData.mScoringInfo.mYellowFlagState,
            UnderYellow = playerScoring.Value.mUnderYellow != 0,
            InPits = playerScoring.Value.mInPits != 0,
            InGarage = playerScoring.Value.mInGarageStall != 0,
            PitState = playerScoring.Value.mPitState,
            InRealtime = _scoringData.mScoringInfo.mInRealtime != 0,
            Place = playerScoring.Value.mPlace,
            Lap = playerTelem.mLapNumber,
            Gear = playerTelem.mGear,
            SpeedKmh = speedMs * 3.6,
            Rpm = playerTelem.mEngineRPM,
            FuelL = playerTelem.mFuel,
            FuelCapacityL = playerTelem.mFuelCapacity,
            WaterTempC = playerTelem.mEngineWaterTemp,
            OilTempC = playerTelem.mEngineOilTemp,
            Overheating = playerTelem.mOverheating != 0,
            Detached = playerTelem.mDetached != 0,
            BatteryFraction = playerTelem.mBatteryChargeFraction,
            BestLap = playerScoring.Value.mBestLapTime,
            LastLap = playerScoring.Value.mLastLapTime,
            TimeBehindNext = playerScoring.Value.mTimeBehindNext,
            TimeBehindLeader = playerScoring.Value.mTimeBehindLeader,
            LapDistFraction = lapDist > 0 ? playerScoring.Value.mLapDist / lapDist : 0,
            Wheels = new WheelTemps
            {
                FlWear = fl.mWear,
                FrWear = fr.mWear,
                RlWear = rl.mWear,
                RrWear = rr.mWear,
                FlBrakeC = fl.mBrakeTemp,
                FrBrakeC = fr.mBrakeTemp,
                RlBrakeC = rl.mBrakeTemp,
                RrBrakeC = rr.mBrakeTemp,
                FlTempC = AvgTireTempC(fl),
                FrTempC = AvgTireTempC(fr),
                RlTempC = AvgTireTempC(rl),
                RrTempC = AvgTireTempC(rr),
                FlCarcassC = KelvinToC(fl.mTireCarcassTemperature),
                FrCarcassC = KelvinToC(fr.mTireCarcassTemperature),
                RlCarcassC = KelvinToC(rl.mTireCarcassTemperature),
                RrCarcassC = KelvinToC(rr.mTireCarcassTemperature)
            },
            Nearby = nearby,
            VirtualEnergy = RestCache.VirtualEnergy,
            RestNotes = RestCache.LastNotes
        };
    }

    private rF2VehicleScoring? FindPlayerScoring(out int index)
    {
        index = -1;
        var vehicles = _scoringData.mVehicles;
        if (vehicles is null)
            return null;

        var count = Math.Min(_scoringData.mScoringInfo.mNumVehicles, vehicles.Length);
        for (var i = 0; i < count; i++)
        {
            if (vehicles[i].mIsPlayer != 0)
            {
                index = i;
                return vehicles[i];
            }
        }

        return null;
    }

    private rF2VehicleTelemetry? FindTelemetry(int id)
    {
        var vehicles = _telem.mVehicles;
        if (vehicles is null)
            return null;

        var count = Math.Min(_telem.mNumVehicles, vehicles.Length);
        for (var i = 0; i < count; i++)
        {
            if (vehicles[i].mID == id)
                return vehicles[i];
        }

        return count > 0 ? vehicles[0] : null;
    }

    private List<NearbyCar> BuildNearby(rF2VehicleScoring player, double lapDist)
    {
        var result = new List<NearbyCar>();
        var vehicles = _scoringData.mVehicles;
        if (vehicles is null || lapDist <= 0)
            return result;

        var count = Math.Min(_scoringData.mScoringInfo.mNumVehicles, vehicles.Length);
        for (var i = 0; i < count; i++)
        {
            var v = vehicles[i];
            if (v.mIsPlayer != 0)
                continue;

            var delta = v.mLapDist - player.mLapDist;
            // Wrap relative distance into [-half, +half]
            if (delta > lapDist / 2) delta -= lapDist;
            if (delta < -lapDist / 2) delta += lapDist;

            if (Math.Abs(delta) > 80)
                continue;

            var side = v.mPathLateral < player.mPathLateral ? "left" : "right";
            if (Math.Abs(v.mPathLateral - player.mPathLateral) < 1.5)
                side = delta > 0 ? "ahead" : "behind";

            result.Add(new NearbyCar
            {
                Name = Decode(v.mDriverName),
                Place = v.mPlace,
                RelativeLapDist = Math.Round(delta, 1),
                Side = side
            });
        }

        return result.OrderBy(c => Math.Abs(c.RelativeLapDist)).Take(5).ToList();
    }

    private static double AvgTireTempC(rF2Wheel w)
    {
        if (w.mTemperature is null || w.mTemperature.Length == 0)
            return 0;
        return KelvinToC(w.mTemperature.Average());
    }

    private static double KelvinToC(double kelvin) =>
        kelvin > 0 ? kelvin - 273.15 : 0;

    private static string Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return "";
        var len = Array.IndexOf(bytes, (byte)0);
        if (len < 0) len = bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, len).Trim();
    }

    public void Dispose()
    {
        _telemetry.Disconnect();
        _scoring.Disconnect();
        _rules.Disconnect();
    }
}
