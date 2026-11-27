using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FrameCore.UI.Infrastructure;

/// <summary>Lightweight area/line chart drawn in OnRender (no charting dependency). Bind Values to a new array to refresh.</summary>
public sealed class SparkChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<double>), typeof(SparkChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SparkChart),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AutoScaleProperty = DependencyProperty.Register(nameof(AutoScale), typeof(bool), typeof(SparkChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SparkChart),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(SparkChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(SparkChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(nameof(Capacity), typeof(int), typeof(SparkChart),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool AutoScale { get => (bool)GetValue(AutoScaleProperty); set => SetValue(AutoScaleProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 2 || h <= 2) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (GridBrush != null)
        {
            var gp = new Pen(GridBrush, 1);
            for (var i = 1; i < 4; i++) dc.DrawLine(gp, new Point(0, Math.Round(h * i / 4) + 0.5), new Point(w, Math.Round(h * i / 4) + 0.5));
        }
        var values = Values;
        if (values == null || values.Count < 2 || values.All(double.IsNaN)) return;
        var max = AutoScale ? Math.Max(1e-6, values.Where(v => !double.IsNaN(v)).Max() * 1.15) : Math.Max(1e-6, Maximum);
        var cap = Math.Max(Capacity, values.Count);
        var step = w / (cap - 1);
        var offset = (cap - values.Count) * step;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var open = false;
            for (var i = 0; i < values.Count; i++)
            {
                if (double.IsNaN(values[i])) { open = false; continue; }
                var p = new Point(offset + i * step, h - Math.Clamp(values[i] / max, 0, 1) * (h - 2) - 1);
                if (!open) { ctx.BeginFigure(p, false, false); open = true; } else ctx.LineTo(p, true, true);
            }
        }
        geo.Freeze();
        if (Fill != null && !values.Any(double.IsNaN))
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(offset, h), true, true);
                for (var i = 0; i < values.Count; i++)
                    ctx.LineTo(new Point(offset + i * step, h - Math.Clamp(values[i] / max, 0, 1) * (h - 2) - 1), true, true);
                ctx.LineTo(new Point(offset + (values.Count - 1) * step, h), true, true);
            }
            area.Freeze();
            dc.DrawGeometry(Fill, null, area);
        }
        dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, geo);
    }
}

public sealed class ChartSeries
{
    public string Name { get; set; } = "";
    public Brush Brush { get; set; } = Brushes.MediumPurple;
    public IReadOnlyList<double> Values { get; set; } = Array.Empty<double>();
}

/// <summary>Multi-series line chart with min/max labels and category labels (benchmark history).</summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(nameof(Labels), typeof(IReadOnlyList<string>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public IReadOnlyList<string>? Labels { get => (IReadOnlyList<string>?)GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    private FormattedText Text(string s) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        var series = Series;
        double w = ActualWidth, h = ActualHeight;
        if (series == null || series.Count == 0 || w < 50 || h < 40) return;
        var all = series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v)).ToList();
        if (all.Count == 0) return;
        double min = Math.Min(0, all.Min()), max = all.Max() * 1.1;
        if (max - min < 1e-6) max = min + 1;
        const double left = 44, bottom = 20, top = 6;
        var plotW = w - left - 8; var plotH = h - bottom - top;
        var gp = new Pen(GridBrush, 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = top + plotH * i / 4;
            dc.DrawLine(gp, new Point(left, Math.Round(y) + 0.5), new Point(w - 8, Math.Round(y) + 0.5));
            var v = max - (max - min) * i / 4;
            var t = Text(v.ToString(v >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture));
            dc.DrawText(t, new Point(left - 6 - t.Width, y - t.Height / 2));
        }
        var n = series.Max(s => s.Values.Count);
        if (n == 0) return;
        double X(int i) => left + (n == 1 ? plotW / 2 : plotW * i / (n - 1));
        double Y(double v) => top + plotH - (v - min) / (max - min) * plotH;
        foreach (var s in series)
        {
            var pen = new Pen(s.Brush, 2) { LineJoin = PenLineJoin.Round };
            for (var i = 0; i < s.Values.Count; i++)
            {
                if (i > 0) dc.DrawLine(pen, new Point(X(i - 1), Y(s.Values[i - 1])), new Point(X(i), Y(s.Values[i])));
                dc.DrawEllipse(s.Brush, null, new Point(X(i), Y(s.Values[i])), 3, 3);
            }
        }
        if (Labels != null)
            for (var i = 0; i < Math.Min(n, Labels.Count); i++)
            {
                if (n > 8 && i % (n / 8 + 1) != 0) continue;
                var t = Text(Labels[i]);
                dc.DrawText(t, new Point(Math.Clamp(X(i) - t.Width / 2, left, w - t.Width), h - bottom + 4));
            }
    }
}

/// <summary>
/// Benchmark repetitions as dots on one axis, with the median (line) and the 95 % confidence interval (band).
/// Hovering a dot shows its value.
/// </summary>
public sealed class SamplePlot : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<double>), typeof(SamplePlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MedianProperty = DependencyProperty.Register(nameof(Median), typeof(double), typeof(SamplePlot),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LowProperty = DependencyProperty.Register(nameof(Low), typeof(double), typeof(SamplePlot),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HighProperty = DependencyProperty.Register(nameof(High), typeof(double), typeof(SamplePlot),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(nameof(DotBrush), typeof(Brush), typeof(SamplePlot),
        new FrameworkPropertyMetadata(Brushes.MediumSeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BandBrushProperty = DependencyProperty.Register(nameof(BandBrush), typeof(Brush), typeof(SamplePlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(nameof(AxisBrush), typeof(Brush), typeof(SamplePlot),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SamplePlot),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Samples { get => (IReadOnlyList<double>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double Median { get => (double)GetValue(MedianProperty); set => SetValue(MedianProperty, value); }
    public double Low { get => (double)GetValue(LowProperty); set => SetValue(LowProperty, value); }
    public double High { get => (double)GetValue(HighProperty); set => SetValue(HighProperty, value); }
    public Brush DotBrush { get => (Brush)GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }
    public Brush? BandBrush { get => (Brush?)GetValue(BandBrushProperty); set => SetValue(BandBrushProperty, value); }
    public Brush AxisBrush { get => (Brush)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    private readonly System.Windows.Controls.ToolTip _tip = new();
    private (double min, double max) _range;

    public SamplePlot()
    {
        ToolTip = _tip;
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 0);
    }

    private (double min, double max) Range(IReadOnlyList<double> s)
    {
        var all = s.Concat(new[] { Low, High, Median }).Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        var min = all.Min(); var max = all.Max();
        var pad = Math.Max((max - min) * 0.12, Math.Abs(max) * 0.01 + 1e-9);
        return (min - pad, max + pad);
    }

    private double X(double v, double w) => 8 + (v - _range.min) / Math.Max(1e-12, _range.max - _range.min) * (w - 16);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Math.Max(0, w), Math.Max(0, h)));
        var s = Samples;
        if (s == null || s.Count == 0 || w < 40 || h < 24) return;
        _range = Range(s);
        var mid = Math.Round((h - 16) / 2) + 0.5;
        if (BandBrush != null && !double.IsNaN(Low) && !double.IsNaN(High))
            dc.DrawRoundedRectangle(BandBrush, null, new Rect(new Point(X(Low, w), mid - 9), new Point(X(High, w), mid + 9)), 4, 4);
        dc.DrawLine(new Pen(AxisBrush, 1), new Point(8, mid), new Point(w - 8, mid));
        for (var i = 0; i < s.Count; i++)
        {
            // Small vertical offsets keep identical values visible.
            var y = mid + (i % 3 - 1) * 4;
            dc.DrawEllipse(DotBrush, null, new Point(X(s[i], w), y), 3.6, 3.6);
        }
        if (!double.IsNaN(Median))
        {
            var x = Math.Round(X(Median, w)) + 0.5;
            dc.DrawLine(new Pen(AxisBrush, 2), new Point(x, mid - 12), new Point(x, mid + 12));
        }
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(System.Windows.Controls.Control.FontFamilyProperty.DefaultMetadata.DefaultValue as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText T(double v) => new(v.ToString(Math.Abs(v) >= 100 ? "N0" : "N1", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 11, AxisBrush, dpi);
        var lo = T(s.Min()); var hi = T(s.Max());
        dc.DrawText(lo, new Point(Math.Max(0, X(s.Min(), w) - lo.Width / 2), h - 14));
        dc.DrawText(hi, new Point(Math.Min(w - hi.Width, X(s.Max(), w) - hi.Width / 2), h - 14));
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var s = Samples;
        if (s == null || s.Count == 0) { _tip.Content = null; return; }
        var x = e.GetPosition(this).X;
        var best = 0;
        for (var i = 1; i < s.Count; i++) if (Math.Abs(X(s[i], ActualWidth) - x) < Math.Abs(X(s[best], ActualWidth) - x)) best = i;
        _tip.Content = FrameCore.Core.Localization.Loc.T("quick.details.sample", best + 1, s[best].ToString(Math.Abs(s[best]) >= 100 ? "N0" : "N2", CultureInfo.CurrentCulture), Unit);
    }
}
