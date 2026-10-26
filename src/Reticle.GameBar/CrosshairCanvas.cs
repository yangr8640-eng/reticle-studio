using System;
using System.Globalization;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace Reticle.GameBar
{
    public sealed class CrosshairCanvas : Canvas
    {
        public CrosshairCanvas() { IsHitTestVisible = false; }

        public void Draw(CrosshairModel settings, double dpiScale, bool enabled, double requestedOpacity)
        {
            Children.Clear();
            if (!enabled || ActualWidth <= 0 || ActualHeight <= 0) return;
            var group = new Canvas
            {
                Width = ActualWidth, Height = ActualHeight,
                Opacity = settings.Opacity / 100 * Math.Max(0, Math.Min(100, requestedOpacity)) / 100,
                IsHitTestVisible = false
            };
            Children.Add(group);
            double x = ActualWidth / 2 + settings.OffsetX / dpiScale;
            double y = ActualHeight / 2 + settings.OffsetY / dpiScale;
            uint color = uint.Parse(settings.Color.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var colorBrush = new SolidColorBrush(Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color));
            var outlineBrush = new SolidColorBrush(Colors.Black);
            if (settings.Outline)
            {
                double width = (settings.Thickness + settings.OutlineWidth * 2) / dpiScale;
                if (settings.Shape == "Circle") AddRing(group, x, y, settings.Radius / dpiScale, width, outlineBrush);
                else foreach (var arm in settings.Arms()) AddArm(group, x, y, dpiScale, arm, width, outlineBrush, PenLineCap.Square);
                if (settings.CenterDot) AddDot(group, x, y, (settings.DotSize / 2 + settings.OutlineWidth) / dpiScale, outlineBrush);
            }
            if (settings.Shape == "Circle") AddRing(group, x, y, settings.Radius / dpiScale, settings.Thickness / dpiScale, colorBrush);
            else foreach (var arm in settings.Arms()) AddArm(group, x, y, dpiScale, arm, settings.Thickness / dpiScale, colorBrush, PenLineCap.Flat);
            if (settings.CenterDot) AddDot(group, x, y, settings.DotSize / 2 / dpiScale, colorBrush);
        }

        private static void AddArm(Canvas parent, double x, double y, double scale, CrosshairSegment arm, double width, Brush brush, PenLineCap cap)
        {
            parent.Children.Add(new Line
            {
                X1 = x + arm.X1 / scale, Y1 = y + arm.Y1 / scale,
                X2 = x + arm.X2 / scale, Y2 = y + arm.Y2 / scale,
                Stroke = brush, StrokeThickness = width, StrokeStartLineCap = cap, StrokeEndLineCap = cap
            });
        }
        private static void AddRing(Canvas parent, double x, double y, double radius, double width, Brush brush)
        {
            // UWP strokes are inset within Shape bounds; widen the box to preserve centerline radius.
            double diameter = radius * 2 + width;
            var ellipse = new Ellipse { Width = diameter, Height = diameter, Stroke = brush, StrokeThickness = width };
            SetLeft(ellipse, x - diameter / 2); SetTop(ellipse, y - diameter / 2); parent.Children.Add(ellipse);
        }
        private static void AddDot(Canvas parent, double x, double y, double radius, Brush brush)
        {
            var ellipse = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = brush };
            SetLeft(ellipse, x - radius); SetTop(ellipse, y - radius); parent.Children.Add(ellipse);
        }
    }
}
