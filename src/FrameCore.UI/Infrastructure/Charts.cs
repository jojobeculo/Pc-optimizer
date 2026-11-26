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
        if (values == null || values.Count < 2) return;
        var max = AutoScale ? Math.Max(1e-6, values.Max() * 1.15) : Math.Max(1e-6, Maximum);
        var cap = Math.Max(Capacity, values.Count);
        var step = w / (cap - 1);
        var offset = (cap - values.Count) * step;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var p = new Point(offset + i * step, h - Math.Clamp(values[i] / max, 0, 1) * (h - 2) - 1);
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
            }
        }
        geo.Freeze();
        if (Fill != null)
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
