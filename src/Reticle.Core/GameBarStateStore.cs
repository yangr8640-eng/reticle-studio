using System.Text.Json;

namespace Reticle.Core;

/// <summary>Local, one-way state for the separately installed Game Bar widget.</summary>
public sealed class GameBarStateStore(string path)
{
    public string Path { get; } = path;

    public void Save(CrosshairSettings settings, bool enabled, DateTimeOffset? now = null)
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            enabled,
            updatedAtUtc = (now ?? DateTimeOffset.UtcNow).ToString("O"),
            settings
        }, SettingsStore.JsonOptions);
        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(directory);
        // Same-directory rename means the widget sees either complete snapshot, never partial JSON.
        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, Path, true);
    }
}
