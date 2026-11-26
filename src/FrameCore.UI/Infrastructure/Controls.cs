using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameCore.UI.Infrastructure;

/// <summary>Stroke icon drawn from a 24×24 geometry (Lucide set, see Themes/Icons.xaml). Crisp at any DPI.</summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(Geometry), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Glyph { get => (Geometry?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    public Icon() { Width = 18; Height = 18; SnapsToDevicePixels = true; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Glyph == null || ActualWidth <= 0) return;
        var s = Math.Min(ActualWidth, ActualHeight) / 24.0;
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * s) / 2, (ActualHeight - 24 * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        var pen = new Pen(Foreground, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, Glyph);
        dc.Pop(); dc.Pop();
    }
}

/// <summary>270° ring gauge with an animated sweep. Value in 0..1; NaN draws only the track ("unknown").</summary>
public sealed class GaugeRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeRing),
        new PropertyMetadata(double.NaN, OnValueChanged));
    private static readonly DependencyProperty DisplayedProperty = DependencyProperty.Register("Displayed", typeof(double), typeof(GaugeRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(GaugeRing),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(GaugeRing),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(GaugeRing),
        new FrameworkPropertyMetadata(Brushes.MediumSeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (GaugeRing)d;
        var v = (double)e.NewValue;
        var target = double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);
        if (!Motion.Enabled) { g.BeginAnimation(DisplayedProperty, null); g.SetValue(DisplayedProperty, target); return; }
        g.BeginAnimation(DisplayedProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;
        var r = (size - Thickness) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        const double start = 135, sweep = 270;
        var cap = PenLineCap.Round;
        dc.DrawGeometry(null, new Pen(Track, Thickness) { StartLineCap = cap, EndLineCap = cap }, Arc(c, r, start, sweep));
        var shown = (double)GetValue(DisplayedProperty);
        if (!double.IsNaN(Value) && shown > 0.002)
            dc.DrawGeometry(null, new Pen(Fill, Thickness) { StartLineCap = cap, EndLineCap = cap }, Arc(c, r, start, sweep * shown));
    }

    private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        static Point P(Point c, double r, double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(c, r, startDeg), false, false);
            ctx.ArcTo(P(c, r, startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
