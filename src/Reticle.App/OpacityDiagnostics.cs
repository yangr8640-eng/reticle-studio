using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Reticle.Core;

namespace Reticle.App;

internal static class OpacityDiagnostics
{
    internal static void Run(string directory, Action<string, bool> check)
    {
        bool opaquePixels = true, exactColor = true, halfOpacity = true, zeroOpacity = true, dpiApplied = true;
        int renderedCases = 0;
        var samples = new List<object>();
        foreach (var shape in Enum.GetValues<CrosshairShape>())
        foreach (double thickness in new[] { 1.0, 1.5, 2.0, 3.0 })
        foreach (bool outline in new[] { false, true })
        foreach (bool dot in new[] { false, true })
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        foreach (int size in new[] { 128, 129 })
        foreach (int opacity in new[] { 0, 50, 100 })
        {
            var settings = new CrosshairSettings
            {
                Shape = shape, Thickness = thickness, Outline = outline, CenterDot = dot,
                // Arms and the dot overlap so 50% also detects accidentally applying opacity per primitive.
                Gap = dot ? 0 : 5, DotSize = 3, Color = "#31C7E9", Opacity = opacity
            };
            var view = new CrosshairView { Settings = settings, PhysicalPixels = true };
            VisualTreeHelper.SetRootDpi(view, new DpiScale(scale, scale));
            double logicalSize = size / scale;
            view.Measure(new Size(logicalSize, logicalSize));
            view.Arrange(new Rect(0, 0, logicalSize, logicalSize));
            view.UpdateLayout();
            dpiApplied &= Math.Abs(VisualTreeHelper.GetDpi(view).DpiScaleX - scale) < 0.000001;
            var bitmap = new RenderTargetBitmap(size, size, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(view);
            byte[] pixels = new byte[size * size * 4];
            bitmap.CopyPixels(pixels, size * 4, 0);
            int visible = 0, minimumAlpha = 255, maximumAlpha = 0, colorPixels = 0;
            bool alphaMatches = true, transparent = true, colorsMatch = true;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                transparent &= pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0 && pixels[i + 3] == 0;
                int alpha = pixels[i + 3];
                if (alpha == 0) continue;
                visible++;
                minimumAlpha = Math.Min(minimumAlpha, alpha);
                maximumAlpha = Math.Max(maximumAlpha, alpha);
                alphaMatches &= opacity == 100 ? alpha == 255 : Math.Abs(alpha - 128) <= 1;
                bool color = pixels[i] == 233 && pixels[i + 1] == 199 && pixels[i + 2] == 49;
                if (color) colorPixels++;
                colorsMatch &= color || (outline && pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0);
            }
            if (opacity == 0) zeroOpacity &= transparent;
            if (opacity == 50) halfOpacity &= visible > 0 && alphaMatches;
            if (opacity == 100)
            {
                opaquePixels &= visible > 0 && alphaMatches;
                exactColor &= colorPixels > 0 && colorsMatch;
            }
            renderedCases++;
            samples.Add(new { shape = shape.ToString(), thickness, outline, dot, scale, size, opacity, visible,
                minimumAlpha = visible == 0 ? 0 : minimumAlpha, maximumAlpha, colorPixels });
            if (opacity == 100 && thickness == 1 && !outline && !dot && scale == 1 && size == 128)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, "opaque-thin-" + shape + ".png"));
                encoder.Save(file);
            }
        }
        File.WriteAllText(Path.Combine(directory, "opacity-rendering.json"),
            JsonSerializer.Serialize(new { renderedCases, opaquePixels, exactColor, halfOpacity, zeroOpacity, dpiApplied, samples },
                new JsonSerializerOptions { WriteIndented = true }));
        check("opacityPhysicalDpiApplied", dpiApplied);
        check("opacityZeroClearsAllShapesAndDpis", zeroOpacity);
        check("opacity100HasNoTranslucentPixels", opaquePixels);
        check("opacity100PreservesExactColor", exactColor);
        check("opacity50DoesNotCompoundAtOverlaps", halfOpacity);
        CheckCenterDots(directory, check);
    }

    private static void CheckCenterDots(string directory, Action<string, bool> check)
    {
        bool opaqueColor = true, halfOpacity = true, transparentWhenDisabled = true, dpiApplied = true;
        int renderedCases = 0;
        var samples = new List<object>();
        foreach (double dotSize in new[] { 1.0, 1.5, 3.0 })
        foreach (bool outline in new[] { false, true })
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        foreach (int size in new[] { 128, 129 })
        foreach (var mode in new[] { (physical: true, zoom: 1.0), (physical: false, zoom: 1.0), (physical: false, zoom: 4.0) })
        // -1 is a disabled-dot control at 100%: a visible ring cannot satisfy these center-only checks.
        foreach (int opacity in new[] { -1, 0, 50, 100 })
        {
            var settings = new CrosshairSettings
            {
                Shape = CrosshairShape.Circle, Radius = 12, Thickness = 1,
                Color = "#31C7E9", CenterDot = opacity != -1, DotSize = dotSize,
                Outline = outline, Opacity = Math.Max(0, opacity == -1 ? 100 : opacity)
            };
            var view = new CrosshairView { Settings = settings, PhysicalPixels = mode.physical, Zoom = mode.zoom };
            VisualTreeHelper.SetRootDpi(view, new DpiScale(scale, scale));
            double logicalSize = size / scale;
            view.Measure(new Size(logicalSize, logicalSize));
            view.Arrange(new Rect(0, 0, logicalSize, logicalSize));
            view.UpdateLayout();
            dpiApplied &= Math.Abs(VisualTreeHelper.GetDpi(view).DpiScaleX - scale) < 0.000001;
            var bitmap = new RenderTargetBitmap(size, size, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(view);
            byte[] pixels = new byte[size * size * 4];
            bitmap.CopyPixels(pixels, size * 4, 0);
            double factor = mode.zoom * (mode.physical ? 1 : scale);
            // This box includes the complete dot and outline while excluding the independent radius-12 ring.
            double halfExtent = (dotSize / 2 + settings.OutlineWidth + 1) * factor;
            int first = (int)Math.Floor(size / 2.0 - halfExtent);
            int last = (int)Math.Ceiling(size / 2.0 + halfExtent);
            int visible = 0, colorPixels = 0, minimumAlpha = 255, maximumAlpha = 0;
            bool alphasMatch = true, colorsMatch = true, transparent = true;
            for (int y = first; y < last; y++)
            for (int x = first; x < last; x++)
            {
                int i = (y * size + x) * 4;
                transparent &= pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0 && pixels[i + 3] == 0;
                int alpha = pixels[i + 3];
                if (alpha == 0) continue;
                visible++;
                minimumAlpha = Math.Min(minimumAlpha, alpha);
                maximumAlpha = Math.Max(maximumAlpha, alpha);
                alphasMatch &= opacity == 100 ? alpha == 255 : Math.Abs(alpha - 128) <= 1;
                bool color = pixels[i] == 233 && pixels[i + 1] == 199 && pixels[i + 2] == 49;
                if (color) colorPixels++;
                colorsMatch &= color || (outline && pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0);
            }
            if (opacity <= 0) transparentWhenDisabled &= transparent;
            if (opacity == 50) halfOpacity &= visible > 0 && alphasMatch;
            if (opacity == 100) opaqueColor &= colorPixels > 0 && colorsMatch && alphasMatch;
            renderedCases++;
            samples.Add(new { dotSize, outline, scale, size, physicalPixels = mode.physical, mode.zoom,
                opacity, visible, colorPixels, minimumAlpha = visible == 0 ? 0 : minimumAlpha, maximumAlpha });
        }
        File.WriteAllText(Path.Combine(directory, "center-dot-rendering.json"),
            JsonSerializer.Serialize(new { renderedCases, opaqueColor, halfOpacity, transparentWhenDisabled, dpiApplied, samples },
                new JsonSerializerOptions { WriteIndented = true }));
        check("centerDotPhysicalAndPreviewDpiApplied", dpiApplied);
        check("centerDotZeroOrDisabledHasNoPixels", transparentWhenDisabled);
        check("centerDot100VisibleWithExactColor", opaqueColor);
        check("centerDot50PreservesGroupOpacity", halfOpacity);
    }
}
