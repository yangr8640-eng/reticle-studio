using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Reticle.Core;

public enum CrosshairShape { Cross, Diagonal, Circle }

public sealed class CrosshairSettings : INotifyPropertyChanged
{
    private CrosshairShape shape;
    private string color = "#67E8B0", monitor = "";
    private double thickness = 2, length = 10, gap = 5, radius = 12, opacity = 100,
        dotSize = 3, outlineWidth = 1, offsetX, offsetY;
    private bool centerDot, outline = true;
    public event PropertyChangedEventHandler? PropertyChanged;

    public CrosshairShape Shape { get => shape; set => Set(ref shape, Enum.IsDefined(value) ? value : CrosshairShape.Cross); }
    public string Color { get => color; set { if (IsColor(value)) Set(ref color, value.ToUpperInvariant()); } }
    public double Thickness { get => thickness; set => Set(ref thickness, Clamp(value, 1, 16, 2)); }
    public double Length { get => length; set => Set(ref length, Clamp(value, 1, 80, 10)); }
    public double Gap { get => gap; set => Set(ref gap, Clamp(value, 0, 80, 5)); }
    public double Radius { get => radius; set => Set(ref radius, Clamp(value, 2, 100, 12)); }
    public double Opacity { get => opacity; set => Set(ref opacity, Clamp(value, 0, 100, 100)); }
    public bool CenterDot { get => centerDot; set => Set(ref centerDot, value); }
    public double DotSize { get => dotSize; set => Set(ref dotSize, Clamp(value, 1, 20, 3)); }
    public bool Outline { get => outline; set => Set(ref outline, value); }
    public double OutlineWidth { get => outlineWidth; set => Set(ref outlineWidth, Clamp(value, 0.5, 6, 1)); }
    public double OffsetX { get => offsetX; set => Set(ref offsetX, Clamp(value, -500, 500, 0)); }
    public double OffsetY { get => offsetY; set => Set(ref offsetY, Clamp(value, -500, 500, 0)); }
    public string Monitor { get => monitor; set => Set(ref monitor, value ?? ""); }
    [JsonIgnore] public string ShapeLabel => Shape switch { CrosshairShape.Diagonal => "斜十字", CrosshairShape.Circle => "圆形", _ => "十字" };

    public static bool IsColor(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);
    private static double Clamp(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Round(Math.Clamp(value, min, max), 1) : fallback;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(Shape)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShapeLabel)));
    }

    public void Apply(CrosshairSettings other, bool includePosition = true)
    {
        Shape = other.Shape; Color = other.Color; Thickness = other.Thickness;
        Length = other.Length; Gap = other.Gap; Radius = other.Radius; Opacity = other.Opacity;
        CenterDot = other.CenterDot; DotSize = other.DotSize; Outline = other.Outline; OutlineWidth = other.OutlineWidth;
        if (includePosition) { OffsetX = other.OffsetX; OffsetY = other.OffsetY; Monitor = other.Monitor; }
    }

    public static CrosshairSettings Preset(string key) => key switch
    {
        "diagonal" => new() { Shape = CrosshairShape.Diagonal, Color = "#71C7FF", Length = 9, Gap = 5 },
        "circle" => new() { Shape = CrosshairShape.Circle, Color = "#FFC66D", Radius = 12, CenterDot = true, DotSize = 2 },
        "precision" => new() { Color = "#F4F6FC", Thickness = 1, Length = 5, Gap = 3, OutlineWidth = 1 },
        _ => new()
    };
}

public readonly record struct Segment(double X1, double Y1, double X2, double Y2);
public static class CrosshairGeometry
{
    // Gap is the distance from the center to the inner edge along each arm.
    public static IReadOnlyList<Segment> Arms(CrosshairSettings settings)
    {
        if (settings.Shape == CrosshairShape.Circle) return [];
        double rotation = settings.Shape == CrosshairShape.Diagonal ? Math.PI / 4 : 0;
        return Enumerable.Range(0, 4).Select(i =>
        {
            double a = rotation + i * Math.PI / 2;
            return new Segment(Math.Cos(a) * settings.Gap, Math.Sin(a) * settings.Gap,
                Math.Cos(a) * (settings.Gap + settings.Length), Math.Sin(a) * (settings.Gap + settings.Length));
        }).ToArray();
    }
}
