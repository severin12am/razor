using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ResourcePanel;

public sealed class BoolVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (Invert)
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class MeterBar : FrameworkElement
{
    static readonly Brush Track = Freeze(Color.FromRgb(0x2A, 0x2F, 0x3A));
    static readonly Brush Ok = Freeze(Color.FromRgb(0x6F, 0xDB, 0xA2));
    static readonly Brush Warm = Freeze(Color.FromRgb(0xE2, 0xB1, 0x5A));
    static readonly Brush Hot = Freeze(Color.FromRgb(0xF0, 0x71, 0x78));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(MeterBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 2 || height < 2)
            return;
        var radius = height / 2;
        context.DrawRoundedRectangle(Track, null, new Rect(0, 0, width, height), radius, radius);
        var fraction = Math.Clamp(Value, 0, 100) / 100d;
        if (fraction <= 0)
            return;
        var fillWidth = Math.Max(height, width * fraction);
        var fill = Value >= 75 ? Hot : Value >= 45 ? Warm : Ok;
        context.DrawRoundedRectangle(fill, null, new Rect(0, 0, Math.Min(width, fillWidth), height), radius, radius);
    }

    static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[]? Values
    {
        get => (double[]?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var values = Values;
        if (values == null || values.Length < 2 || ActualWidth < 4 || ActualHeight < 4)
            return;

        var finite = values.Where(static value => !double.IsNaN(value) && !double.IsInfinity(value)).ToArray();
        if (finite.Length < 2)
            return;
        var max = Math.Max(finite.Max(), 0.001);
        var min = Math.Min(finite.Min(), 0);
        var span = Math.Max(0.001, max - min);
        var width = ActualWidth;
        var height = ActualHeight;
        var pen = new Pen(Stroke, 1.5);
        pen.Freeze();
        var figure = new StreamGeometry();
        using (var geometry = figure.Open())
        {
            for (var i = 0; i < finite.Length; i++)
            {
                var x = width * i / (finite.Length - 1);
                var y = height - ((finite[i] - min) / span) * (height - 2) - 1;
                if (i == 0)
                    geometry.BeginFigure(new Point(x, y), false, false);
                else
                    geometry.LineTo(new Point(x, y), true, false);
            }
        }
        figure.Freeze();
        context.DrawGeometry(null, pen, figure);
    }
}
