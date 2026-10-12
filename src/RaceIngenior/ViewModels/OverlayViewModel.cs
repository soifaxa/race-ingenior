using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;
using RaceIngenior.Telemetry;
using MediaBrush = System.Windows.Media.Brush;

namespace RaceIngenior.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    // WEC timing palette: per-tyre blue / amber / green; status strip uses same green when PUSH OK
    private static readonly MediaBrush ColdBrush = BrushFrom("#0072CE");
    private static readonly MediaBrush WarmBrush = BrushFrom("#C45C00");
    private static readonly MediaBrush ReadyBrush = BrushFrom("#1B8A3E");
    private static readonly MediaBrush IdleBrush = BrushFrom("#888888");
    private static readonly MediaBrush FlatBrush = BrushFrom("#E10600");

    private static readonly MediaBrush StatusCold = BrushFrom("#0072CE");
    private static readonly MediaBrush StatusWarm = BrushFrom("#F5A623");
    private static readonly MediaBrush StatusReady = BrushFrom("#1B8A3E");
    private static readonly MediaBrush StatusFlat = BrushFrom("#E10600");
    private static readonly MediaBrush StatusIdle = BrushFrom("#333333");
    private static readonly MediaBrush WhiteBrush = BrushFrom("#FFFFFF");
    private static readonly MediaBrush BlackBrush = BrushFrom("#111111");
    private static readonly MediaBrush DeltaNeutral = BrushFrom("#F2F2F2");
    private static readonly MediaBrush DeltaSlow = BrushFrom("#E10600");
    private static readonly MediaBrush DeltaFast = BrushFrom("#1B8A3E");

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
    [ObservableProperty] private string _fuel = "FUEL —";
    [ObservableProperty] private string _delta = "Δ —";
    [ObservableProperty] private MediaBrush _deltaBrush = DeltaNeutral;
    [ObservableProperty] private string _conversationDriver = "—";
    [ObservableProperty] private string _conversationEngineer = "—";

    public OverlayViewModel(TelemetryHub hub, IOptions<AppSettings> settings)
    {
        _settings = settings.Value;
        hub.Updated += OnTelemetry;
        OnTelemetry(hub.Latest);
    }

    public void SetConversation(string driver, string engineer)
    {
        void Apply()
        {
            ConversationDriver = string.IsNullOrWhiteSpace(driver) ? "—" : driver.Trim();
            ConversationEngineer = string.IsNullOrWhiteSpace(engineer) ? "—" : engineer.Trim();
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
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
                Fuel = "FUEL —";
                Delta = "Δ —";
                DeltaBrush = DeltaNeutral;
                return;
            }

            Fl = snap.Wheels.FlFlat ? "FLAT" : $"{snap.Wheels.FlCarcassC:0}";
            Fr = snap.Wheels.FrFlat ? "FLAT" : $"{snap.Wheels.FrCarcassC:0}";
            Rl = snap.Wheels.RlFlat ? "FLAT" : $"{snap.Wheels.RlCarcassC:0}";
            Rr = snap.Wheels.RrFlat ? "FLAT" : $"{snap.Wheels.RrCarcassC:0}";
            FlBrush = snap.Wheels.FlFlat ? FlatBrush : ColorFor(snap.Wheels.FlCarcassC);
            FrBrush = snap.Wheels.FrFlat ? FlatBrush : ColorFor(snap.Wheels.FrCarcassC);
            RlBrush = snap.Wheels.RlFlat ? FlatBrush : ColorFor(snap.Wheels.RlCarcassC);
            RrBrush = snap.Wheels.RrFlat ? FlatBrush : ColorFor(snap.Wheels.RrCarcassC);

            Fuel = snap.FuelCapacityL > 0
                ? $"FUEL {snap.FuelL:0.0}/{snap.FuelCapacityL:0.0} L"
                : $"FUEL {snap.FuelL:0.0} L";

            if (snap.LastLap > 0 && snap.BestLap > 0)
            {
                var d = snap.LastLap - snap.BestLap;
                Delta = d >= 0 ? $"Δ +{d:0.000}" : $"Δ {d:0.000}";
                DeltaBrush = Math.Abs(d) < 0.001 ? DeltaNeutral : (d > 0 ? DeltaSlow : DeltaFast);
            }
            else
            {
                Delta = "Δ —";
                DeltaBrush = DeltaNeutral;
            }

            if (snap.Wheels.AnyFlat)
            {
                Status = "PUNCTURE — BOX";
                StatusBrush = StatusFlat;
                StatusForeground = WhiteBrush;
                return;
            }

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
