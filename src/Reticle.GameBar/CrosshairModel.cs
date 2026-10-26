using System;
using System.Collections.Generic;

namespace Reticle.GameBar
{
    // Kept free of WinRT so geometry and physical-pixel conversion can be checked on .NET.
    public sealed class CrosshairModel
    {
        public string Shape = "Cross", Color = "#67E8B0";
        public double Thickness = 2, Length = 10, Gap = 5, Radius = 12, Opacity = 100;
        public bool CenterDot, Outline = true;
        public double DotSize = 3, OutlineWidth = 1, OffsetX, OffsetY;

        public double EffectiveOpacity(double requestedOpacity)
        {
            // XboxGameBarSamples assigns RequestedOpacity directly to XAML Opacity (0..1).
            // Only the editor's own percentage needs converting; dividing both by 100 hides the reticle.
            double editor = double.IsNaN(Opacity) || double.IsInfinity(Opacity) ? 100 : Opacity;
            double host = double.IsNaN(requestedOpacity) || double.IsInfinity(requestedOpacity) ? 1 : requestedOpacity;
            return Math.Max(0, Math.Min(100, editor)) / 100 * Math.Max(0, Math.Min(1, host));
        }

        public static double Clamp(double value, double minimum, double maximum, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Round(Math.Max(minimum, Math.Min(maximum, value)), 1);
        }

        public double Extent
        {
            get
            {
                double shapeRadius = Shape == "Circle" ? Radius : Gap + Length;
                return Math.Max(shapeRadius + Thickness / 2, CenterDot ? DotSize / 2 : 0) + (Outline ? OutlineWidth : 0) + 2;
            }
        }

        public IEnumerable<CrosshairSegment> Arms()
        {
            if (Shape == "Circle") yield break;
            double rotation = Shape == "Diagonal" ? Math.PI / 4 : 0;
            for (int i = 0; i < 4; i++)
            {
                double angle = rotation + i * Math.PI / 2;
                yield return new CrosshairSegment(Math.Cos(angle) * Gap, Math.Sin(angle) * Gap,
                    Math.Cos(angle) * (Gap + Length), Math.Sin(angle) * (Gap + Length));
            }
        }

        public double RequiredWidth(double dpiScale) { return Math.Max(400, 2 * (Math.Abs(OffsetX) + Extent) / dpiScale); }
        public double RequiredHeight(double dpiScale) { return Math.Max(400, 2 * (Math.Abs(OffsetY) + Extent) / dpiScale); }
    }

    public struct CrosshairSegment
    {
        public readonly double X1, Y1, X2, Y2;
        public CrosshairSegment(double x1, double y1, double x2, double y2) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
    }
}
