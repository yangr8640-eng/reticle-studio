using System.Globalization;

namespace Reticle.Core;

/// <summary>Hue in degrees; saturation and value in the inclusive range 0..1.</summary>
public readonly record struct HsvColor(double Hue, double Saturation, double Value);

public static class ColorMath
{
    public static HsvColor HexToHsv(string hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#' || !hex.Skip(1).All(Uri.IsHexDigit))
            throw new ArgumentException("Color must use the #RRGGBB format.", nameof(hex));

        uint rgb = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double r = ((rgb >> 16) & 255) / 255d;
        double g = ((rgb >> 8) & 255) / 255d;
        double b = (rgb & 255) / 255d;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        // Gray has no hue; a picker can retain its previous hue when saturation is zero.
        double hue = 0;
        if (delta > 0)
        {
            hue = max == r ? 60 * ((g - b) / delta % 6)
                : max == g ? 60 * ((b - r) / delta + 2)
                : 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
        }
        return new HsvColor(hue, max == 0 ? 0 : delta / max, max);
    }

    public static string HsvToHex(double hue, double saturation, double value)
    {
        if (!double.IsFinite(hue)) throw new ArgumentOutOfRangeException(nameof(hue));
        if (!double.IsFinite(saturation)) throw new ArgumentOutOfRangeException(nameof(saturation));
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));

        hue = (hue % 360 + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        double chroma = value * saturation;
        double secondary = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double match = value - chroma;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, secondary, 0d),
            < 120 => (secondary, chroma, 0d),
            < 180 => (0d, chroma, secondary),
            < 240 => (0d, secondary, chroma),
            < 300 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };
        int Channel(double component) => (int)Math.Clamp(Math.Round((component + match) * 255, MidpointRounding.AwayFromZero), 0, 255);
        return $"#{Channel(r):X2}{Channel(g):X2}{Channel(b):X2}";
    }
}
