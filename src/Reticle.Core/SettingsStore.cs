using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reticle.Core;

public sealed class SettingsStore(string path)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };
    public string Path { get; } = path;

    public CrosshairSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(Path)) return new();
        try { return Parse(File.ReadAllText(Path)); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            warning = "配置无法读取，已恢复默认。原文件保留在损坏配置备份中。";
            try { File.Copy(Path, Path + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            { warning = "配置无法读取且备份失败；本次关闭自动保存，请导出配置。"; }
            return new();
        }
    }

    public static CrosshairSettings Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("Shape", out _))
            throw new InvalidDataException("请选择由准星工具导出的 JSON 配置。");
        if (doc.RootElement.TryGetProperty("Color", out var color) &&
            (color.ValueKind != JsonValueKind.String || !CrosshairSettings.IsColor(color.GetString())))
            throw new InvalidDataException("颜色必须为 #RRGGBB 格式。");
        return JsonSerializer.Deserialize<CrosshairSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("配置内容为空。");
    }

    public void Save(CrosshairSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, Path, true);
    }
}
