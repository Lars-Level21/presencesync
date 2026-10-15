using System.Text.Json;
using System.Text.Json.Serialization;

namespace PresenceSync.Core.Configuration;

public sealed class ConfigStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PresenceSync", "config.json");

    public string Path { get; } = path;

    public bool Exists => File.Exists(Path);

    public AppConfig Load()
    {
        if (!File.Exists(Path))
            return new AppConfig();

        using var stream = File.OpenRead(Path);
        return JsonSerializer.Deserialize<AppConfig>(stream, Options) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
        File.Move(temp, Path, overwrite: true);
    }
}
