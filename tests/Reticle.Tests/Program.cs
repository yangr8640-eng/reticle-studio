using Reticle.Core;
using System.Text.Json;

int passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
bool Near(double a, double b) => Math.Abs(a - b) < 0.00001;

Check("Cross arms preserve gap and length", () =>
{
    var s = new CrosshairSettings { Gap = 7, Length = 13 };
    var arms = CrosshairGeometry.Arms(s); Assert(arms.Count == 4);
    foreach (var a in arms)
    {
        Assert(Near(Math.Sqrt(a.X1 * a.X1 + a.Y1 * a.Y1), 7));
        Assert(Near(Math.Sqrt(Math.Pow(a.X2 - a.X1, 2) + Math.Pow(a.Y2 - a.Y1, 2)), 13));
        Assert(Near(a.X1, 0) || Near(a.Y1, 0));
    }
});
Check("Diagonal rotates 45 degrees and preserves radial gap", () =>
{
    foreach (var a in CrosshairGeometry.Arms(new() { Shape = CrosshairShape.Diagonal, Gap = 10 }))
    { Assert(Near(Math.Abs(a.X1), Math.Abs(a.Y1))); Assert(Near(Math.Sqrt(a.X1 * a.X1 + a.Y1 * a.Y1), 10)); }
});
Check("Circle has no cross arms", () => Assert(CrosshairGeometry.Arms(new() { Shape = CrosshairShape.Circle }).Count == 0));
Check("Untrusted settings clamp numeric inputs", () =>
{
    var s = new CrosshairSettings { Thickness = -99, Gap = 10000, Opacity = double.NaN, OffsetX = double.PositiveInfinity, Shape = (CrosshairShape)123 };
    Assert(s.Thickness == 1 && s.Gap == 80 && s.Opacity == 100 && s.OffsetX == 0 && s.Shape == CrosshairShape.Cross);
});
Check("Invalid colors cannot reach renderer", () =>
{
    var s = new CrosshairSettings(); s.Color = "red"; Assert(s.Color == "#67E8B0"); s.Color = "#abcdef"; Assert(s.Color == "#ABCDEF");
    s.Color = "#FF0000\n"; Assert(s.Color == "#ABCDEF" && !CrosshairSettings.IsColor("#FF0000\n"));
});
Check("Presets preserve selected monitor and position", () =>
{
    var s = new CrosshairSettings { Monitor = "screen2", OffsetX = 10, OffsetY = -8 };
    s.Apply(CrosshairSettings.Preset("circle"), false);
    Assert(s.Shape == CrosshairShape.Circle && s.Monitor == "screen2" && s.OffsetX == 10 && s.OffsetY == -8);
});
Check("Changed properties notify once", () =>
{
    var s = new CrosshairSettings(); int calls = 0; s.PropertyChanged += (_, _) => calls++;
    s.Thickness = 4; s.Thickness = 4; Assert(calls == 1);
});
Check("Configuration round trip and atomic replacement", () =>
{
    string path = Path.Combine(Path.GetTempPath(), "reticle-tests-" + Guid.NewGuid(), "settings.json");
    try
    {
        var store = new SettingsStore(path);
        store.Save(new() { Shape = CrosshairShape.Circle, Color = "#112233", Radius = 24, Opacity = 42, OffsetY = -10, Monitor = "test" });
        var s = store.Load(out var warning);
        Assert(warning is null && s.Shape == CrosshairShape.Circle && s.Radius == 24 && s.Opacity == 42 && s.Color == "#112233" && s.OffsetY == -10 && s.Monitor == "test");
        s.Gap = 13; store.Save(s); Assert(store.Load(out _).Gap == 13 && !File.Exists(path + ".tmp"));
        File.WriteAllText(path, "{broken"); s = store.Load(out warning);
        Assert(warning is not null && s.Gap == 5 && Directory.GetFiles(Path.GetDirectoryName(path)!, "*.invalid-*").Length == 1);
    }
    finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
});
Check("Import rejects unrelated or invalid documents", () =>
{
    foreach (string json in new[] { "null", "[]", "{}", "{\"Shape\":\"Cross\",\"Color\":\"invalid\"}", "{\"Shape\":\"Cross\",\"Color\":\"#FF0000\\n\"}" })
    {
        bool rejected = false; try { SettingsStore.Parse(json); } catch (Exception e) when (e is InvalidDataException or JsonException) { rejected = true; }
        Assert(rejected);
    }
});
Check("HEX primaries and secondaries map to HSV hue sectors", () =>
{
    var colors = new[] { "#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF" };
    for (int i = 0; i < colors.Length; i++)
    {
        var hsv = ColorMath.HexToHsv(colors[i]);
        Assert(Near(hsv.Hue, i * 60) && Near(hsv.Saturation, 1) && Near(hsv.Value, 1));
        Assert(ColorMath.HsvToHex(i * 60, 1, 1) == colors[i]);
    }
});
Check("HSV saturation and brightness produce expected RGB values", () =>
{
    Assert(ColorMath.HsvToHex(0, 0.5, 1) == "#FF8080");
    Assert(ColorMath.HsvToHex(120, 1, 0.5) == "#008000");
    Assert(ColorMath.HsvToHex(240, 0.5, 0.5) == "#404080");
});
Check("Grayscale and black do not divide by zero", () =>
{
    foreach (string hex in new[] { "#000000", "#808080", "#FFFFFF" })
    {
        var hsv = ColorMath.HexToHsv(hex);
        Assert(hsv.Hue == 0 && hsv.Saturation == 0 && double.IsFinite(hsv.Value));
        Assert(ColorMath.HsvToHex(217, 0, hsv.Value) == hex);
    }
    Assert(ColorMath.HsvToHex(83, 1, 0) == "#000000");
});
Check("Hue wraps and saturation/value clamp at palette boundaries", () =>
{
    Assert(ColorMath.HsvToHex(360, 1, 1) == "#FF0000");
    Assert(ColorMath.HsvToHex(720, 1, 1) == "#FF0000");
    Assert(ColorMath.HsvToHex(-120, 1, 1) == "#0000FF");
    Assert(ColorMath.HsvToHex(120, 2, 2) == "#00FF00");
    Assert(ColorMath.HsvToHex(120, -1, 1) == "#FFFFFF");
    Assert(ColorMath.HsvToHex(120, 1, -1) == "#000000");
});
Check("HSV conversion preserves selected 24-bit colors", () =>
{
    foreach (string hex in new[] { "#67e8b0", "#71C7FF", "#FFC66D", "#FF7AA2", "#010203", "#FEFDFC", "#123456", "#A942E7" })
    {
        var hsv = ColorMath.HexToHsv(hex);
        Assert(ColorMath.HsvToHex(hsv.Hue, hsv.Saturation, hsv.Value) == hex.ToUpperInvariant());
    }
    for (int r = 0; r <= 255; r += 17)
        for (int g = 0; g <= 255; g += 17)
            for (int b = 0; b <= 255; b += 17)
            {
                string hex = $"#{r:X2}{g:X2}{b:X2}";
                var hsv = ColorMath.HexToHsv(hex);
                Assert(ColorMath.HsvToHex(hsv.Hue, hsv.Saturation, hsv.Value) == hex);
            }
});
Check("Color conversion rejects malformed HEX and nonfinite HSV", () =>
{
    foreach (string? hex in new string?[] { null, "", "red", "FF0000", "#FFF", "#FF000000", "#GG0000", "#FF0000\n", "#  FF00" })
    {
        bool rejected = false;
        try { ColorMath.HexToHsv(hex!); } catch (ArgumentException) { rejected = true; }
        Assert(rejected);
    }
    foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        foreach (int channel in new[] { 0, 1, 2 })
        {
            bool rejected = false;
            try { ColorMath.HsvToHex(channel == 0 ? invalid : 0, channel == 1 ? invalid : 1, channel == 2 ? invalid : 1); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(rejected);
        }
});
Check("Game Bar state preserves settings, heartbeat and explicit disabled state", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "reticle-gamebar-tests-" + Guid.NewGuid());
    string path = Path.Combine(directory, "overlay-state.json");
    try
    {
        var store = new GameBarStateStore(path);
        var settings = new CrosshairSettings { Shape = CrosshairShape.Diagonal, Color = "#123456", OffsetX = -123, Opacity = 41 };
        var now = DateTimeOffset.Parse("2026-10-26T12:34:56Z");
        store.Save(settings, true, now);
        using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var root = doc.RootElement;
            Assert(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("enabled").GetBoolean());
            Assert(root.GetProperty("updatedAtUtc").GetDateTimeOffset() == now);
            var restored = SettingsStore.Parse(root.GetProperty("settings").GetRawText());
            Assert(restored.Shape == settings.Shape && restored.Color == settings.Color && restored.OffsetX == -123 && restored.Opacity == 41);
        }
        store.Save(settings, false, now.AddSeconds(3));
        using var updated = JsonDocument.Parse(File.ReadAllText(path));
        Assert(!updated.RootElement.GetProperty("enabled").GetBoolean());
        Assert(!File.Exists(path + ".tmp"));
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
UpdateChecks.Run(Check);
Console.WriteLine($"All {passed} checks passed.");
