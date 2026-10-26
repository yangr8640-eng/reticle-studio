using Reticle.Core;
using Reticle.GameBar;

int passed = 0;
void Check(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
void Near(double actual, double expected) => Assert(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-9);
string Live(string settings = "{}", string enabled = "true", string schema = "1", string timestamp = "\"2026-10-26T12:34:56.1234567Z\"") =>
    "{\"schemaVersion\":" + schema + ",\"enabled\":" + enabled + ",\"updatedAtUtc\":" + timestamp + ",\"settings\":" + settings + "}";
bool IsRejectedInput(Exception error) => error is InvalidDataException or ArgumentException or System.Runtime.InteropServices.COMException or FormatException;
void Reject(string name, string json) => Check(name, () =>
{
    bool rejected = false;
    try { ConfigurationReader.ParseLive(json); }
    catch (Exception error) when (IsRejectedInput(error)) { rejected = true; }
    Assert(rejected);
});

Check("Real desktop writer to WinRT parser roundtrip", () =>
{
    string path = Path.Combine(AppContext.BaseDirectory, "overlay-state.json");
    var store = new GameBarStateStore(path);
    var now = DateTimeOffset.Parse("2026-10-26T12:34:56.1234567Z");
    foreach (var shape in Enum.GetValues<CrosshairShape>())
    foreach (bool enabled in new[] { false, true })
    {
        var source = new CrosshairSettings
        {
            Shape = shape, Color = "#Ab23F0", Thickness = 4.2, Length = 21.1, Gap = 18.2, Radius = 25.5,
            Opacity = 72.3, CenterDot = true, DotSize = 4.1, Outline = false, OutlineWidth = 2.5,
            OffsetX = -123.4, OffsetY = 432.1
        };
        store.Save(source, enabled, now);
        var actual = ConfigurationReader.ParseLive(File.ReadAllText(path));
        var s = actual.Settings;
        Assert(actual.Enabled == enabled && actual.UpdatedAtUtc == now && actual.UpdatedAtUtc.Offset == TimeSpan.Zero);
        Assert(s.Shape == source.Shape.ToString() && s.Color == source.Color && s.Thickness == source.Thickness && s.Length == source.Length
            && s.Gap == source.Gap && s.Radius == source.Radius && s.Opacity == source.Opacity && s.CenterDot == source.CenterDot
            && s.DotSize == source.DotSize && s.Outline == source.Outline && s.OutlineWidth == source.OutlineWidth
            && s.OffsetX == source.OffsetX && s.OffsetY == source.OffsetY);
    }
});
Check("Defaults and BOM accepted", () => Assert(ConfigurationReader.ParseLive("\uFEFF" + Live()).Settings.Color == "#67E8B0"));
Check("Case normalization", () =>
{
    var model = ConfigurationReader.ParseLive(Live("{\"Shape\":\"diagonal\",\"Color\":\"#aabbcc\"}")).Settings;
    Assert(model.Shape == "Diagonal" && model.Color == "#AABBCC");
});
Reject("Malformed JSON", "{broken}");
Reject("Non-object root", "[]");
Reject("Unsupported schema", Live(schema: "2"));
Reject("Missing schema", "{\"enabled\":true,\"updatedAtUtc\":\"2026-10-26T12:00:00Z\",\"settings\":{}}");
Reject("Wrong schema type", Live(schema: "\"1\""));
Reject("Wrong enabled type", Live(enabled: "1"));
Reject("Wrong settings type", Live("[]"));
Reject("Invalid UTC time", Live(timestamp: "\"bad date\""));
Reject("Wrong UTC time type", Live(timestamp: "123"));
Reject("Unknown shape", Live("{\"Shape\":\"Triangle\"}"));
Reject("Invalid color", Live("{\"Color\":\"#GG0000\"}"));
Reject("Wrong color type", Live("{\"Color\":123}"));
Reject("Wrong numeric type", Live("{\"Thickness\":\"2\"}"));
Reject("Wrong boolean type", Live("{\"Outline\":1}"));
Reject("Oversized UTF8 input", Live("{\"Extra\":\"" + new string('界', 22000) + "\"}"));
Check("Finite out-of-range values clamp", () =>
{
    var s = ConfigurationReader.ParseLive(Live("{\"Thickness\":999,\"Length\":-5,\"Gap\":1000,\"Radius\":-1,\"Opacity\":1000,\"DotSize\":-1,\"OutlineWidth\":999,\"OffsetX\":-999,\"OffsetY\":999}")).Settings;
    Assert(s.Thickness == 16 && s.Length == 1 && s.Gap == 80 && s.Radius == 2 && s.Opacity == 100
        && s.DotSize == 1 && s.OutlineWidth == 6 && s.OffsetX == -500 && s.OffsetY == 500);
});
Check("Nonfinite model inputs use fallback", () =>
{
    foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        Assert(CrosshairModel.Clamp(value, 1, 16, 2) == 2);
});
Reject("NaN JSON rejected", Live("{\"Thickness\":NaN}"));
Check("JSON exponent overflow cannot escape finite geometry", () =>
{
    try { Assert(double.IsFinite(ConfigurationReader.ParseLive(Live("{\"Thickness\":1e999}")).Settings.Thickness)); }
    catch (Exception error) when (IsRejectedInput(error)) { }
});

// Game Bar RequestedOpacity is a 0..1 fraction; it must not be divided by 100.
Check("Full Game Bar opacity leaves a fully opaque crosshair visible", () => Near(new CrosshairModel().EffectiveOpacity(1), 1));
Check("Half Game Bar opacity stays half visible", () => Near(new CrosshairModel().EffectiveOpacity(0.5), 0.5));
Check("Zero Game Bar opacity hides the crosshair", () => Near(new CrosshairModel().EffectiveOpacity(0), 0));
Check("Editor and host opacity multiply in the same units", () => Near(new CrosshairModel { Opacity = 40 }.EffectiveOpacity(0.5), 0.2));
Check("Host fractional opacity retains precision", () => Near(new CrosshairModel().EffectiveOpacity(0.375), 0.375));
Check("Opacity rejects nonfinite inputs with documented defaults", () =>
{
    foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        Near(new CrosshairModel { Opacity = 40 }.EffectiveOpacity(value), 0.4);
        Near(new CrosshairModel { Opacity = value }.EffectiveOpacity(0.5), 0.5);
        Near(new CrosshairModel { Opacity = value }.EffectiveOpacity(value), 1);
    }
});
Check("Out-of-range opacity clamps without escaping 0..1", () =>
{
    Near(new CrosshairModel { Opacity = -1 }.EffectiveOpacity(1), 0);
    Near(new CrosshairModel { Opacity = 101 }.EffectiveOpacity(1), 1);
    Near(new CrosshairModel().EffectiveOpacity(-1), 0);
    Near(new CrosshairModel().EffectiveOpacity(2), 1);
    foreach (double opacity in new[] { -100, 0, 40, 100, 200, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    foreach (double host in new[] { -2, 0, 0.2, 1, 3, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        double actual = new CrosshairModel { Opacity = opacity }.EffectiveOpacity(host);
        Assert(double.IsFinite(actual) && actual >= 0 && actual <= 1);
    }
});
Check("Cross and diagonal geometry preserve radial gap and arm length", () =>
{
    foreach (string shape in new[] { "Cross", "Diagonal" })
    {
        var model = new CrosshairModel { Shape = shape, Gap = 13, Length = 27 };
        var arms = model.Arms().ToArray();
        Assert(arms.Length == 4);
        foreach (var arm in arms)
        {
            Near(Math.Sqrt(arm.X1 * arm.X1 + arm.Y1 * arm.Y1), 13);
            Near(Math.Sqrt(arm.X2 * arm.X2 + arm.Y2 * arm.Y2), 40);
            Near(Math.Sqrt(Math.Pow(arm.X2 - arm.X1, 2) + Math.Pow(arm.Y2 - arm.Y1, 2)), 27);
        }
        Near(arms[0].X1, shape == "Diagonal" ? 13 / Math.Sqrt(2) : 13);
        Near(arms[0].Y1, shape == "Diagonal" ? 13 / Math.Sqrt(2) : 0);
    }
    Assert(!new CrosshairModel { Shape = "Circle" }.Arms().Any());
});
Check("Widget bounds contain maximum offsets and strokes at supported DPI scales", () =>
{
    foreach (string shape in new[] { "Cross", "Diagonal", "Circle" })
    foreach (double x in new[] { -500d, 0, 500 })
    foreach (double y in new[] { -500d, 0, 500 })
    foreach (double dpi in new[] { 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4 })
    {
        var model = new CrosshairModel
        {
            Shape = shape, Gap = 80, Length = 80, Radius = 100, Thickness = 16,
            Outline = true, OutlineWidth = 6, CenterDot = true, DotSize = 20, OffsetX = x, OffsetY = y
        };
        double strokeHalf = 14;
        double halfDrawingWidth = shape == "Circle" ? 114 : model.Arms().Max(arm => Math.Max(Math.Abs(arm.X1), Math.Abs(arm.X2))) + strokeHalf * (shape == "Diagonal" ? Math.Sqrt(2) : 1);
        double halfDrawingHeight = shape == "Circle" ? 114 : model.Arms().Max(arm => Math.Max(Math.Abs(arm.Y1), Math.Abs(arm.Y2))) + strokeHalf * (shape == "Diagonal" ? Math.Sqrt(2) : 1);
        double width = model.RequiredWidth(dpi), height = model.RequiredHeight(dpi);
        Assert(width >= 400 && height >= 400);
        Assert(width * dpi / 2 >= Math.Abs(x) + halfDrawingWidth && height * dpi / 2 >= Math.Abs(y) + halfDrawingHeight);
    }
});
Console.WriteLine($"All {passed} Game Bar checks passed.");
