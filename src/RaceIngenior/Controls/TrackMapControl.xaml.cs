using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RaceIngenior.Models;

namespace RaceIngenior.Controls;

public partial class TrackMapControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty CarsProperty = DependencyProperty.Register(
        nameof(Cars), typeof(IEnumerable), typeof(TrackMapControl),
        new PropertyMetadata(null, (d, _) => ((TrackMapControl)d).OnCarsChanged()));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(TrackLayout), typeof(TrackMapControl),
        new PropertyMetadata(null, (d, _) => ((TrackMapControl)d).InvalidateTrack()));

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage), typeof(string), typeof(TrackMapControl),
        new PropertyMetadata("", (d, _) => ((TrackMapControl)d).UpdateBanner()));

    public static readonly DependencyProperty DetailScaleProperty = DependencyProperty.Register(
        nameof(DetailScale), typeof(double), typeof(TrackMapControl),
        new PropertyMetadata(1.0, (d, _) => ((TrackMapControl)d).OnDetailScaleChanged()));

    private static readonly SolidColorBrush TrackStroke = MakeBrush("#3A3A3A");
    private static readonly SolidColorBrush TrackFill = MakeBrush("#0E0E0E");
    private static readonly SolidColorBrush OutlineStroke = MakeBrush("#888888");
    private static readonly SolidColorBrush HypercarBrush = MakeBrush("#E10600");
    private static readonly SolidColorBrush Lmp2Brush = MakeBrush("#2E7DFF");
    private static readonly SolidColorBrush Lmgt3Brush = MakeBrush("#22C55E");
    private static readonly SolidColorBrush UnknownBrush = MakeBrush("#AAAAAA");
    private static readonly SolidColorBrush LabelBrush = MakeBrush("#F2F2F2");
    private static readonly SolidColorBrush SfBrush = MakeBrush("#E10600");
    private static readonly SolidColorBrush PlayerFill = MakeBrush("#FFFFFF");

    /// <summary>Exponential smoothing rate (higher = snappier). Tuned for ~100 ms telemetry.</summary>
    private const double SmoothRate = 14.0;

    private readonly Dictionary<string, CarVisual> _visuals = new(StringComparer.Ordinal);
    private List<TrackLayoutPoint>? _outlinePts;
    private Func<double, System.Windows.Point>? _mapT;
    private bool _trackDirty = true;
    private bool _animating;
    private TimeSpan _lastRender = TimeSpan.Zero;

    public TrackMapControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => InvalidateTrack();
        Loaded += (_, _) =>
        {
            InvalidateTrack();
            StartAnimation();
        };
        Unloaded += (_, _) => StopAnimation();
    }

    public IEnumerable? Cars
    {
        get => (IEnumerable?)GetValue(CarsProperty);
        set => SetValue(CarsProperty, value);
    }

    public TrackLayout? Layout
    {
        get => (TrackLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    /// <summary>Multiplies stroke and marker sizes (e.g. 2 for the enlarged window).</summary>
    public double DetailScale
    {
        get => (double)GetValue(DetailScaleProperty);
        set => SetValue(DetailScaleProperty, value);
    }

    private double S => Math.Clamp(DetailScale <= 0 ? 1.0 : DetailScale, 0.5, 4.0);

    private void OnDetailScaleChanged()
    {
        UpdateLegendScale();
        InvalidateTrack();
        foreach (var v in _visuals.Values)
            ApplyDotChrome(v);
    }

    private void InvalidateTrack()
    {
        _trackDirty = true;
        UpdateBanner();
        UpdateLegendScale();
        EnsureTrackDrawn();
        PlaceAllMarkers();
    }

    private void OnCarsChanged()
    {
        SyncTargetsFromCars();
        EnsureTrackDrawn();
        PlaceAllMarkers();
    }

    private void StartAnimation()
    {
        if (_animating)
            return;
        _animating = true;
        _lastRender = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating)
            return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs re)
            return;

        var now = re.RenderingTime;
        var dt = _lastRender == TimeSpan.Zero
            ? 1.0 / 60.0
            : Math.Clamp((now - _lastRender).TotalSeconds, 0, 0.05);
        _lastRender = now;

        if (_visuals.Count == 0 || _mapT is null)
            return;

        var alpha = 1.0 - Math.Exp(-SmoothRate * dt);
        var moved = false;
        foreach (var v in _visuals.Values)
        {
            var next = LerpLap(v.DisplayT, v.TargetT, alpha);
            if (Math.Abs(WrapDelta(v.DisplayT, next)) < 1e-6)
                continue;
            v.DisplayT = next;
            moved = true;
        }

        if (moved)
            PlaceAllMarkers();
    }

    private void SyncTargetsFromCars()
    {
        var list = Cars?.OfType<TrackCar>().ToList() ?? [];
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var car in list)
        {
            var key = CarKey(car);
            seen.Add(key);
            if (_visuals.TryGetValue(key, out var v))
            {
                v.Car = car;
                v.TargetT = NormalizeLap(car.LapDistFraction);
                // Large teleport (garage / session reset): snap.
                if (Math.Abs(WrapDelta(v.DisplayT, v.TargetT)) > 0.35)
                    v.DisplayT = v.TargetT;
                ApplyDotChrome(v);
            }
            else
            {
                v = CreateVisual(car);
                _visuals[key] = v;
            }
        }

        foreach (var key in _visuals.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            RemoveVisual(_visuals[key]);
            _visuals.Remove(key);
        }
    }

    private CarVisual CreateVisual(TrackCar car)
    {
        var t = NormalizeLap(car.LapDistFraction);
        var v = new CarVisual
        {
            Car = car,
            DisplayT = t,
            TargetT = t,
            Dot = new Ellipse { ToolTip = FormatTip(car) },
            Label = new TextBlock
            {
                Foreground = LabelBrush,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false
            }
        };
        ApplyDotChrome(v);
        CarsCanvas.Children.Add(v.Dot);
        CarsCanvas.Children.Add(v.Label);
        return v;
    }

    private void RemoveVisual(CarVisual v)
    {
        CarsCanvas.Children.Remove(v.Dot);
        if (v.Label is not null)
            CarsCanvas.Children.Remove(v.Label);
    }

    private void ApplyDotChrome(CarVisual v)
    {
        var d = S;
        var car = v.Car;
        var size = (car.IsPlayer ? 12.0 : 9.0) * d;
        v.Size = size;
        v.Dot.Width = size;
        v.Dot.Height = size;
        v.Dot.Fill = car.IsPlayer ? PlayerFill : BrushForClass(car.VehicleClass);
        v.Dot.Stroke = null;
        v.Dot.StrokeThickness = 0;
        v.Dot.Opacity = car.InPits ? 0.55 : 1.0;
        v.Dot.ToolTip = FormatTip(car);

        if (v.Label is null)
            return;

        if (car.Place > 0)
        {
            v.Label.Text = car.Place.ToString();
            v.Label.FontSize = (car.IsPlayer ? 10 : 9) * d;
            v.Label.Visibility = Visibility.Visible;
        }
        else
        {
            v.Label.Visibility = Visibility.Collapsed;
        }
    }

    private void EnsureTrackDrawn()
    {
        if (MapCanvas is null || CarsCanvas is null)
            return;

        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 40 || h < 40)
            return;

        if (!_trackDirty && _mapT is not null)
            return;

        MapCanvas.Children.Clear();
        _outlinePts = null;
        _mapT = null;

        if (Layout?.HasOutline == true)
            BuildLearnedTrack(w, h, Layout);
        else
            BuildOvalTrack(w, h);

        _trackDirty = false;
    }

    private void BuildLearnedTrack(double w, double h, TrackLayout layout)
    {
        var pts = layout.Points.OrderBy(p => p.T).ToList();
        if (pts.Count < 2)
        {
            BuildOvalTrack(w, h);
            return;
        }

        var minX = pts.Min(p => p.X);
        var maxX = pts.Max(p => p.X);
        var minZ = pts.Min(p => p.Z);
        var maxZ = pts.Max(p => p.Z);
        var spanX = Math.Max(1.0, maxX - minX);
        var spanZ = Math.Max(1.0, maxZ - minZ);
        var d = S;
        var pad = 28.0 * Math.Max(1.0, d * 0.85);
        var scale = Math.Min((w - pad * 2) / spanX, (h - pad * 2 - 18 * d) / spanZ);
        var ox = (w - spanX * scale) / 2;
        var oy = (h - spanZ * scale) / 2 - 4;

        System.Windows.Point Map(double x, double z) =>
            new(ox + (x - minX) * scale, oy + (maxZ - z) * scale);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var first = Map(pts[0].X, pts[0].Z);
            ctx.BeginFigure(first, false, false);
            for (var i = 1; i < pts.Count; i++)
                ctx.LineTo(Map(pts[i].X, pts[i].Z), true, false);
            if (pts[0].T < 0.05 && pts[^1].T > 0.9)
                ctx.LineTo(first, true, false);
        }
        geo.Freeze();

        MapCanvas.Children.Add(new Path
        {
            Data = geo,
            Stroke = OutlineStroke,
            StrokeThickness = 3 * d,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

        var sf = InterpolateOutline(pts, 0);
        var sfp = Map(sf.X, sf.Z);
        var sfSize = 6 * d;
        var sfDot = new Ellipse { Width = sfSize, Height = sfSize, Fill = SfBrush };
        Canvas.SetLeft(sfDot, sfp.X - sfSize / 2);
        Canvas.SetTop(sfDot, sfp.Y - sfSize / 2);
        MapCanvas.Children.Add(sfDot);

        _outlinePts = pts;
        _mapT = t =>
        {
            var p = InterpolateOutline(pts, t);
            return Map(p.X, p.Z);
        };
    }

    private void BuildOvalTrack(double w, double h)
    {
        var d = S;
        var cx = w * 0.5;
        var cy = h * 0.52;
        var rx = Math.Max(20, w * 0.38);
        var ry = Math.Max(16, h * 0.30);

        var track = new Ellipse
        {
            Width = rx * 2,
            Height = ry * 2,
            Stroke = TrackStroke,
            StrokeThickness = 3 * d,
            Fill = TrackFill
        };
        Canvas.SetLeft(track, cx - rx);
        Canvas.SetTop(track, cy - ry);
        MapCanvas.Children.Add(track);

        var sfLen = 6 * d;
        MapCanvas.Children.Add(new Line
        {
            X1 = cx,
            Y1 = cy - ry - sfLen,
            X2 = cx,
            Y2 = cy - ry + sfLen,
            Stroke = SfBrush,
            StrokeThickness = 2 * d
        });

        _outlinePts = null;
        _mapT = t =>
        {
            var angle = NormalizeLap(t) * Math.PI * 2.0 - Math.PI / 2.0;
            return new System.Windows.Point(
                cx + rx * Math.Cos(angle),
                cy + ry * Math.Sin(angle));
        };
    }

    private void PlaceAllMarkers()
    {
        if (_mapT is null || CarsCanvas is null)
            return;

        foreach (var v in _visuals.Values)
        {
            var p = _mapT(v.DisplayT);
            Canvas.SetLeft(v.Dot, p.X - v.Size / 2);
            Canvas.SetTop(v.Dot, p.Y - v.Size / 2);

            if (v.Label is null || v.Label.Visibility != Visibility.Visible)
                continue;

            v.Label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(v.Label, p.X - v.Label.DesiredSize.Width / 2);
            Canvas.SetTop(v.Label, p.Y - v.Size / 2 - v.Label.DesiredSize.Height - 1);
        }
    }

    private void UpdateLegendScale()
    {
        if (LegendPanel is null)
            return;

        var s = S;
        LegendPanel.Margin = new Thickness(8 * s, 0, 0, 6 * s);
        foreach (var child in LegendPanel.Children)
        {
            if (child is Ellipse el)
            {
                el.Width = 8 * s;
                el.Height = 8 * s;
                el.Margin = new Thickness(0, 0, 4 * s, 0);
                if (el.StrokeThickness > 0)
                    el.StrokeThickness = 1.5 * s;
            }
            else if (child is TextBlock tb)
            {
                tb.FontSize = 9 * s;
                tb.Margin = tb.Text == "YOU"
                    ? new Thickness(0)
                    : new Thickness(0, 0, 10 * s, 0);
            }
        }

        if (EmptyHint is not null)
            EmptyHint.FontSize = 11 * s;
        if (FallbackText is not null)
            FallbackText.FontSize = 11 * s;
    }

    private void UpdateBanner()
    {
        if (FallbackBanner is null || FallbackText is null)
            return;

        var hasLayout = Layout?.HasOutline == true;
        if (hasLayout || string.IsNullOrWhiteSpace(StatusMessage))
        {
            FallbackBanner.Visibility = Visibility.Collapsed;
            return;
        }

        FallbackText.Text = StatusMessage;
        FallbackBanner.Visibility = Visibility.Visible;
    }

    private static string CarKey(TrackCar car) =>
        car.IsPlayer
            ? "__player__"
            : (string.IsNullOrWhiteSpace(car.Name) ? $"#{car.Place}" : car.Name);

    private static double NormalizeLap(double t)
    {
        t %= 1.0;
        if (t < 0) t += 1.0;
        return t;
    }

    /// <summary>Shortest signed delta on the [0,1) lap circle (forward preferred when equal).</summary>
    private static double WrapDelta(double from, double to)
    {
        var d = NormalizeLap(to) - NormalizeLap(from);
        if (d > 0.5) d -= 1.0;
        if (d < -0.5) d += 1.0;
        return d;
    }

    private static double LerpLap(double from, double to, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        return NormalizeLap(from + WrapDelta(from, to) * alpha);
    }

    private static TrackLayoutPoint InterpolateOutline(List<TrackLayoutPoint> pts, double t)
    {
        t = NormalizeLap(t);

        if (t <= pts[0].T)
            return pts[0];
        if (t >= pts[^1].T)
            return pts[^1];

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            if (t < a.T || t > b.T)
                continue;

            var span = Math.Max(1e-6, b.T - a.T);
            var u = (t - a.T) / span;
            return new TrackLayoutPoint
            {
                T = t,
                X = a.X + (b.X - a.X) * u,
                Z = a.Z + (b.Z - a.Z) * u
            };
        }

        return pts[^1];
    }

    internal static SolidColorBrush BrushForClass(string? vehicleClass)
    {
        var c = (vehicleClass ?? "").Trim().ToUpperInvariant();
        if (c.Length == 0)
            return UnknownBrush;

        if (c.Contains("HYPER", StringComparison.Ordinal)
            || c.Contains("LMH", StringComparison.Ordinal)
            || c.Contains("LMDH", StringComparison.Ordinal)
            || c.Contains("GTP", StringComparison.Ordinal))
            return HypercarBrush;

        if (c.Contains("LMP2", StringComparison.Ordinal))
            return Lmp2Brush;

        if (c.Contains("LMGT3", StringComparison.Ordinal)
            || c.Contains("GT3", StringComparison.Ordinal)
            || c.Contains("GTE", StringComparison.Ordinal))
            return Lmgt3Brush;

        return UnknownBrush;
    }

    private static string FormatTip(TrackCar car)
    {
        var name = string.IsNullOrWhiteSpace(car.Name) ? "Driver" : car.Name;
        var place = car.Place > 0 ? $"P{car.Place}" : "P—";
        var cls = string.IsNullOrWhiteSpace(car.VehicleClass) ? "" : $" · {car.VehicleClass}";
        var where = car.InPits ? " · pits" : "";
        var you = car.IsPlayer ? " · YOU" : "";
        return $"{place} {name}{cls}{where}{you}";
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        if (b.CanFreeze)
            b.Freeze();
        return b;
    }

    private sealed class CarVisual
    {
        public required Ellipse Dot { get; init; }
        public TextBlock? Label { get; init; }
        public required TrackCar Car { get; set; }
        public double DisplayT { get; set; }
        public double TargetT { get; set; }
        public double Size { get; set; }
    }
}
