using System.IO;
using System.Text.Json;
using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

public sealed record TreadmillConfig
{
    public List<EventActionMapping> Mappings { get; init; } = [];
}

public sealed class TreadmillConfigStore
{
    private readonly JsonSerializerOptions serializerOptions;

    public TreadmillConfigStore(string dataFolderName, EventActionTypeRegistry registry)
    {
        ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), dataFolderName, "treadmill-config.json");
        serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new EventActionJsonConverter(registry) },
        };
    }

    public string ConfigFilePath { get; }

    public TreadmillConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return new TreadmillConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<TreadmillConfig>(json, serializerOptions) ?? new TreadmillConfig();
        }
        catch (JsonException)
        {
            TryBackUpCorruptConfig();
            return new TreadmillConfig();
        }
    }

    public void Save(TreadmillConfig config)
    {
        var directory = Path.GetDirectoryName(ConfigFilePath)!;
        Directory.CreateDirectory(directory);

        var tempPath = ConfigFilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(config, serializerOptions));
        File.Move(tempPath, ConfigFilePath, overwrite: true);
    }

    private void TryBackUpCorruptConfig()
    {
        try
        {
            File.Copy(ConfigFilePath, ConfigFilePath + ".bak", overwrite: true);
        }
        catch (Exception)
        {
        }
    }
}
