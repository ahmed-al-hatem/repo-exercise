using System.Windows;
using System.Windows.Media;
using BleFinder.Core.Models;

namespace BleFinder.App.Controls;

public sealed class SignalHistoryControl : FrameworkElement
{
    private const double MinimumRssi = -100;
    private const double MaximumRssi = -35;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(60);
    private static readonly Brush PlotBrush = CreateFrozenBrush(Color.FromRgb(15, 23, 42));
    private static readonly Pen GuidePen = CreateFrozenPen(Color.FromArgb(90, 148, 163, 184), 1);
    private static readonly Pen SignalPen = CreateFrozenPen(Color.FromRgb(34, 211, 238), 2.5);

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples),
        typeof(IReadOnlyList<SignalSample>),
        typeof(SignalHistoryControl),
        new FrameworkPropertyMetadata(
            Array.Empty<SignalSample>(),
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnSamplesChanged));

    public IReadOnlyList<SignalSample> Samples
    {
        get => (IReadOnlyList<SignalSample>?)GetValue(SamplesProperty) ?? [];
        set => SetValue(SamplesProperty, value ?? []);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var samples = Samples;
        if (samples.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        const double padding = 14;
        var plotRect = new Rect(0, 0, ActualWidth, ActualHeight);
        var contentWidth = Math.Max(0, plotRect.Width - (padding * 2));
        var contentHeight = Math.Max(0, plotRect.Height - (padding * 2));
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        drawingContext.DrawRoundedRectangle(PlotBrush, null, plotRect, 10, 10);

        for (var guide = 0; guide < 5; guide++)
        {
            var y = padding + (contentHeight * guide / 4);
            drawingContext.DrawLine(
                GuidePen,
                new Point(padding, y),
                new Point(padding + contentWidth, y));
        }

        var newestTimestamp = samples.Max(sample => sample.Timestamp);
        var windowStart = newestTimestamp - WindowDuration;
        var visibleSamples = samples
            .Where(sample => sample.Timestamp >= windowStart)
            .OrderBy(sample => sample.Timestamp)
            .ToArray();
        if (visibleSamples.Length < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Map(visibleSamples[0], windowStart, padding, contentWidth, contentHeight), false, false);

            for (var index = 1; index < visibleSamples.Length; index++)
            {
                context.LineTo(Map(visibleSamples[index], windowStart, padding, contentWidth, contentHeight), true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, SignalPen, geometry);
    }

    private static void OnSamplesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((SignalHistoryControl)dependencyObject).InvalidateVisual();

    private static Point Map(
        SignalSample sample,
        DateTimeOffset windowStart,
        double padding,
        double width,
        double height)
    {
        var elapsed = Math.Clamp((sample.Timestamp - windowStart).TotalSeconds, 0, WindowDuration.TotalSeconds);
        var rssi = Math.Clamp(sample.Rssi, MinimumRssi, MaximumRssi);
        var x = padding + (elapsed / WindowDuration.TotalSeconds * width);
        var normalizedRssi = (rssi - MinimumRssi) / (MaximumRssi - MinimumRssi);
        var y = padding + ((1 - normalizedRssi) * height);
        return new Point(x, y);
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
