using System.IO;
using System.Text.Json;
using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur;

/// <summary>
/// Shared load/save/backup-on-corruption logic for the per-integration config files (Wiz,
/// Treadmill, ...), each of which is just a JSON document containing that integration's own
/// state plus a list of <see cref="EventActionMapping"/>.
/// </summary>
public abstract class EventActionConfigStore<TConfig> where TConfig : new()
{
    private readonly JsonSerializerOptions serializerOptions;

    protected EventActionConfigStore(string dataFolderName, string configFileName, EventActionTypeRegistry registry)
    {
        ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), dataFolderName, configFileName);
        serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new EventActionJsonConverter(registry) },
        };
    }

    public string ConfigFilePath { get; }

    public TConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return new TConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<TConfig>(json, serializerOptions) ?? new TConfig();
        }
        catch (JsonException)
        {
            TryBackUpCorruptConfig();
            return new TConfig();
        }
    }

    public void Save(TConfig config)
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
