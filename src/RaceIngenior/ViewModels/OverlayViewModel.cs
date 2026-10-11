using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;
using RaceIngenior.Telemetry;
using MediaBrush = System.Windows.Media.Brush;

namespace RaceIngenior.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    // WEC timing palette: black numbers, cool blue / amber / race green for state
    private static readonly MediaBrush ColdBrush = BrushFrom("#0072CE");
    private static readonly MediaBrush WarmBrush = BrushFrom("#C45C00");
    private static readonly MediaBrush ReadyBrush = BrushFrom("#111111");
    private static readonly MediaBrush IdleBrush = BrushFrom("#888888");

    private static readonly MediaBrush StatusCold = BrushFrom("#0072CE");
    private static readonly MediaBrush StatusWarm = BrushFrom("#F5A623");
    private static readonly MediaBrush StatusReady = BrushFrom("#1B8A3E");
    private static readonly MediaBrush StatusIdle = BrushFrom("#333333");
    private static readonly MediaBrush WhiteBrush = BrushFrom("#FFFFFF");
    private static readonly MediaBrush BlackBrush = BrushFrom("#111111");

    private readonly AppSettings _settings;

    [ObservableProperty] private string _fl = "—";
    [ObservableProperty] private string _fr = "—";
    [ObservableProperty] private string _rl = "—";
    [ObservableProperty] private string _rr = "—";
    [ObservableProperty] private MediaBrush _flBrush = IdleBrush;
    [ObservableProperty] private MediaBrush _frBrush = IdleBrush;
    [ObservableProperty] private MediaBrush _rlBrush = IdleBrush;
    [ObservableProperty] private MediaBrush _rrBrush = IdleBrush;
    [ObservableProperty] private string _status = "TYRES";
    [ObservableProperty] private MediaBrush _statusBrush = StatusIdle;
    [ObservableProperty] private MediaBrush _statusForeground = WhiteBrush;

    public OverlayViewModel(TelemetryHub hub, IOptions<AppSettings> settings)
    {
        _settings = settings.Value;
        hub.Updated += OnTelemetry;
        OnTelemetry(hub.Latest);
    }

    private void OnTelemetry(TelemetrySnapshot snap)
    {
        void Apply()
        {
            if (!snap.Connected)
            {
                Fl = Fr = Rl = Rr = "—";
                FlBrush = FrBrush = RlBrush = RrBrush = IdleBrush;
                Status = "NO DATA";
                StatusBrush = StatusIdle;
                StatusForeground = WhiteBrush;
                return;
            }

            Fl = $"{snap.Wheels.FlCarcassC:0}";
            Fr = $"{snap.Wheels.FrCarcassC:0}";
            Rl = $"{snap.Wheels.RlCarcassC:0}";
            Rr = $"{snap.Wheels.RrCarcassC:0}";
            FlBrush = ColorFor(snap.Wheels.FlCarcassC);
            FrBrush = ColorFor(snap.Wheels.FrCarcassC);
            RlBrush = ColorFor(snap.Wheels.RlCarcassC);
            RrBrush = ColorFor(snap.Wheels.RrCarcassC);

            var min = new[]
            {
                snap.Wheels.FlCarcassC, snap.Wheels.FrCarcassC,
                snap.Wheels.RlCarcassC, snap.Wheels.RrCarcassC
            }.Min();

            if (min < _settings.TireColdBelowC)
            {
                Status = "COLD — DO NOT PUSH";
                StatusBrush = StatusCold;
                StatusForeground = WhiteBrush;
            }
            else if (min >= _settings.TireWarmReadyC)
            {
                Status = "PUSH OK";
                StatusBrush = StatusReady;
                StatusForeground = WhiteBrush;
            }
            else
            {
                Status = "WARMING UP";
                StatusBrush = StatusWarm;
                StatusForeground = BlackBrush;
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    private MediaBrush ColorFor(double c)
    {
        if (c < _settings.TireColdBelowC) return ColdBrush;
        if (c >= _settings.TireWarmReadyC) return ReadyBrush;
        return WarmBrush;
    }

    private static MediaBrush BrushFrom(string hex)
    {
        var brush = (MediaBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
