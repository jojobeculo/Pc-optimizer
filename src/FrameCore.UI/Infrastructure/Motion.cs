using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameCore.UI.Infrastructure;

/// <summary>
/// Small, purposeful animations. Everything is skipped when Windows "Animation effects" is turned off
/// (SPI_GETCLIENTAREAANIMATION) and nothing ever delays a real operation: animations only decorate values
/// that are already known.
/// </summary>
public static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation && !Dialogs.Headless;

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    // ---------------------------------------------------------------- entrance (fade + 8 px rise)
    public static readonly DependencyProperty EnterProperty =
        DependencyProperty.RegisterAttached("Enter", typeof(int), typeof(Motion), new PropertyMetadata(-1, OnEnterChanged));
    public static void SetEnter(DependencyObject d, int order) => d.SetValue(EnterProperty, order);
    public static int GetEnter(DependencyObject d) => (int)d.GetValue(EnterProperty);

    private static void OnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || (int)e.NewValue < 0) return;
        fe.Loaded += (_, _) => PlayEnter(fe, GetEnter(fe));
    }

    public static void PlayEnter(FrameworkElement fe, int order = 0)
    {
        if (!Enabled) { fe.Opacity = 1; return; }
        var delay = TimeSpan.FromMilliseconds(Math.Min(order, 8) * 45);
        var t = new TranslateTransform(0, 8);
        fe.RenderTransform = t;
        fe.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = Ease };
        var rise = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(320)) { BeginTime = delay, EasingFunction = Ease };
        fe.BeginAnimation(UIElement.OpacityProperty, fade);
        t.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    // ---------------------------------------------------------------- count-up of a number
    /// <summary>Animated numeric text: set CountTo (double, NaN = unknown) and CountFormat (e.g. "0", "0.0").</summary>
    public static readonly DependencyProperty CountToProperty =
        DependencyProperty.RegisterAttached("CountTo", typeof(double), typeof(Motion), new PropertyMetadata(double.NaN, OnCountToChanged));
    public static readonly DependencyProperty CountFormatProperty =
        DependencyProperty.RegisterAttached("CountFormat", typeof(string), typeof(Motion), new PropertyMetadata("0"));
    private static readonly DependencyProperty CountCurrentProperty =
        DependencyProperty.RegisterAttached("CountCurrent", typeof(double), typeof(Motion), new PropertyMetadata(0.0, OnCountCurrentChanged));

    public static void SetCountTo(DependencyObject d, double v) => d.SetValue(CountToProperty, v);
    public static double GetCountTo(DependencyObject d) => (double)d.GetValue(CountToProperty);
    public static void SetCountFormat(DependencyObject d, string v) => d.SetValue(CountFormatProperty, v);
    public static string GetCountFormat(DependencyObject d) => (string)d.GetValue(CountFormatProperty);

    private static void OnCountToChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        var target = (double)e.NewValue;
        if (double.IsNaN(target)) { tb.BeginAnimation(CountCurrentProperty, null); tb.Text = "—"; return; }
        if (!Enabled) { tb.BeginAnimation(CountCurrentProperty, null); tb.SetValue(CountCurrentProperty, target); Render(tb, target); return; }
        var from = (double)tb.GetValue(CountCurrentProperty);
        if (double.IsNaN((double)e.OldValue)) from = 0;
        tb.BeginAnimation(CountCurrentProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(650)) { EasingFunction = Ease });
    }

    private static void OnCountCurrentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => Render((TextBlock)d, (double)e.NewValue);
    private static void Render(TextBlock tb, double v) => tb.Text = v.ToString(GetCountFormat(tb), CultureInfo.CurrentCulture);

    // ---------------------------------------------------------------- horizontal bar growing to a fraction
    public static readonly DependencyProperty FractionProperty =
        DependencyProperty.RegisterAttached("Fraction", typeof(double), typeof(Motion), new PropertyMetadata(double.NaN, OnFractionChanged));
    public static void SetFraction(DependencyObject d, double v) => d.SetValue(FractionProperty, v);
    public static double GetFraction(DependencyObject d) => (double)d.GetValue(FractionProperty);

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        var target = double.IsNaN((double)e.NewValue) ? 0 : Math.Clamp((double)e.NewValue, 0, 1);
        if (fe.RenderTransform is not ScaleTransform st) { st = new ScaleTransform(0, 1); fe.RenderTransform = st; fe.RenderTransformOrigin = new Point(0, 0.5); }
        if (!Enabled) { st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.ScaleX = target; return; }
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(600)) { EasingFunction = Ease });
    }
}

/// <summary>Page host that cross-fades and slides content when the page changes.</summary>
public sealed class TransitionContentControl : ContentControl
{
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (!Motion.Enabled) return;
        var t = new TranslateTransform(14, 0);
        RenderTransform = t;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
