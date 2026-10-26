using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Reticle.Core;

namespace Reticle.App;

public sealed class CrosshairView : FrameworkElement
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings), typeof(CrosshairSettings), typeof(CrosshairView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public CrosshairSettings? Settings { get => (CrosshairSettings?)GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }
    public double Zoom { get; set; } = 1;
    public bool PhysicalPixels { get; set; }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (CrosshairView)d;
        if (e.OldValue is CrosshairSettings old) PropertyChangedEventManager.RemoveHandler(old, view.OnSettingsChanged, "");
        if (e.NewValue is CrosshairSettings current) PropertyChangedEventManager.AddHandler(current, view.OnSettingsChanged, "");
    }
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Settings is not { } s) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        dc.PushTransform(new TranslateTransform(ActualWidth / 2, ActualHeight / 2));
        dc.PushTransform(new ScaleTransform(Zoom / (PhysicalPixels ? dpi.DpiScaleX : 1), Zoom / (PhysicalPixels ? dpi.DpiScaleY : 1)));
        // One opacity group keeps overlapping strokes and the center dot equally translucent.
        dc.PushOpacity(s.Opacity / 100);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Color)); brush.Freeze();
        var pen = new Pen(brush, s.Thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        var outline = new Pen(Brushes.Black, s.Thickness + s.OutlineWidth * 2) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
        var arms = CrosshairGeometry.Arms(s);
        if (s.Outline)
        {
            if (s.Shape == CrosshairShape.Circle) dc.DrawEllipse(null, outline, new Point(), s.Radius, s.Radius);
            else foreach (var a in arms) dc.DrawLine(outline, new Point(a.X1, a.Y1), new Point(a.X2, a.Y2));
            if (s.CenterDot) dc.DrawEllipse(Brushes.Black, null, new Point(), s.DotSize / 2 + s.OutlineWidth, s.DotSize / 2 + s.OutlineWidth);
        }
        if (s.Shape == CrosshairShape.Circle) dc.DrawEllipse(null, pen, new Point(), s.Radius, s.Radius);
        else foreach (var a in arms) dc.DrawLine(pen, new Point(a.X1, a.Y1), new Point(a.X2, a.Y2));
        if (s.CenterDot) dc.DrawEllipse(brush, null, new Point(), s.DotSize / 2, s.DotSize / 2);
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}
