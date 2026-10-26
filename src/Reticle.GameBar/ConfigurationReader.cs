using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace Reticle.GameBar
{
    internal sealed class OverlayState
    {
        public CrosshairModel Settings;
        public bool Enabled;
        public DateTimeOffset UpdatedAtUtc;
    }

    internal static class ConfigurationReader
    {
        internal const int MaximumBytes = 65536;
        internal const string LiveFileName = "overlay-state.json", ImportFileName = "imported-crosshair.json";
        internal const string SyncSettingName = "UseDesktopSync";

        internal static bool UseDesktopSync
        {
            get
            {
                object value;
                return !ApplicationData.Current.LocalSettings.Values.TryGetValue(SyncSettingName, out value) || !(value is bool) || (bool)value;
            }
            set { ApplicationData.Current.LocalSettings.Values[SyncSettingName] = value; }
        }

        internal static async Task<string> ReadBoundedAsync(StorageFile file)
        {
            // Bound the stream itself so replacing/growing a file cannot bypass a metadata check.
            using (var input = await file.OpenStreamForReadAsync())
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096];
                int count;
                while ((count = await input.ReadAsync(buffer, 0, Math.Min(buffer.Length, MaximumBytes + 1 - (int)output.Length))) > 0)
                {
                    output.Write(buffer, 0, count);
                    if (output.Length > MaximumBytes) throw new InvalidDataException("配置文件不能超过 64 KB。");
                }
                return new UTF8Encoding(false, true).GetString(output.ToArray());
            }
        }

        internal static OverlayState ParseLive(string text)
        {
            var json = ParseObject(text);
            if (Number(json, "schemaVersion", -1) != 1) throw new InvalidDataException("不支持的桌面同步版本。");
            if (!json.ContainsKey("enabled") || json["enabled"].ValueType != JsonValueType.Boolean)
                throw new InvalidDataException("桌面同步缺少有效的 enabled 开关。");
            if (!json.ContainsKey("settings") || json["settings"].ValueType != JsonValueType.Object)
                throw new InvalidDataException("桌面同步缺少准星配置。");
            DateTimeOffset updated;
            if (!DateTimeOffset.TryParse(Text(json, "updatedAtUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out updated))
                throw new InvalidDataException("桌面同步时间无效。");
            return new OverlayState { Settings = ParseSettings(json["settings"].GetObject()), Enabled = json["enabled"].GetBoolean(), UpdatedAtUtc = updated };
        }

        internal static CrosshairModel ParseImported(string text)
        {
            var json = ParseObject(text);
            if (!json.ContainsKey("Shape") && !json.ContainsKey("Color")) throw new InvalidDataException("请选择 Reticle 导出的准星 JSON。");
            return ParseSettings(json);
        }

        private static JsonObject ParseObject(string text)
        {
            if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("配置文件不能超过 64 KB。");
            JsonObject json;
            if (!JsonObject.TryParse(text.TrimStart('\uFEFF'), out json)) throw new InvalidDataException("配置不是有效 JSON 对象。");
            return json;
        }

        private static CrosshairModel ParseSettings(JsonObject json)
        {
            var model = new CrosshairModel();
            string shape = Text(json, "Shape", model.Shape);
            if (string.Equals(shape, "Cross", StringComparison.OrdinalIgnoreCase)) model.Shape = "Cross";
            else if (string.Equals(shape, "Diagonal", StringComparison.OrdinalIgnoreCase)) model.Shape = "Diagonal";
            else if (string.Equals(shape, "Circle", StringComparison.OrdinalIgnoreCase)) model.Shape = "Circle";
            else throw new InvalidDataException("准星形状必须是 Cross、Diagonal 或 Circle。");
            string color = Text(json, "Color", model.Color);
            uint rgb;
            if (color.Length != 7 || color[0] != '#' || !uint.TryParse(color.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
                throw new InvalidDataException("准星颜色必须是 #RRGGBB。");
            model.Color = color.ToUpperInvariant();
            model.Thickness = CrosshairModel.Clamp(Number(json, "Thickness", 2), 1, 16, 2);
            model.Length = CrosshairModel.Clamp(Number(json, "Length", 10), 1, 80, 10);
            model.Gap = CrosshairModel.Clamp(Number(json, "Gap", 5), 0, 80, 5);
            model.Radius = CrosshairModel.Clamp(Number(json, "Radius", 12), 2, 100, 12);
            model.Opacity = CrosshairModel.Clamp(Number(json, "Opacity", 100), 0, 100, 100);
            model.CenterDot = Boolean(json, "CenterDot", false);
            model.Outline = Boolean(json, "Outline", true);
            model.DotSize = CrosshairModel.Clamp(Number(json, "DotSize", 3), 1, 20, 3);
            model.OutlineWidth = CrosshairModel.Clamp(Number(json, "OutlineWidth", 1), 0.5, 6, 1);
            model.OffsetX = CrosshairModel.Clamp(Number(json, "OffsetX", 0), -500, 500, 0);
            model.OffsetY = CrosshairModel.Clamp(Number(json, "OffsetY", 0), -500, 500, 0);
            return model;
        }

        private static string Text(JsonObject json, string key, string fallback)
        {
            if (!json.ContainsKey(key)) return fallback;
            if (json[key].ValueType != JsonValueType.String) throw new InvalidDataException(key + " 必须是文字。");
            return json[key].GetString();
        }
        private static double Number(JsonObject json, string key, double fallback)
        {
            if (!json.ContainsKey(key)) return fallback;
            if (json[key].ValueType != JsonValueType.Number) throw new InvalidDataException(key + " 必须是数值。");
            return json[key].GetNumber();
        }
        private static bool Boolean(JsonObject json, string key, bool fallback)
        {
            if (!json.ContainsKey(key)) return fallback;
            if (json[key].ValueType != JsonValueType.Boolean) throw new InvalidDataException(key + " 必须是开关。");
            return json[key].GetBoolean();
        }
    }
}
