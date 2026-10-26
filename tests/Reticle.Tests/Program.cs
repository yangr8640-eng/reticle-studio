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
    foreach (string json in new[] { "null", "[]", "{}", "{\"Shape\":\"Cross\",\"Color\":\"invalid\"}" })
    {
        bool rejected = false; try { SettingsStore.Parse(json); } catch (Exception e) when (e is InvalidDataException or JsonException) { rejected = true; }
        Assert(rejected);
    }
});
Console.WriteLine($"All {passed} checks passed.");
