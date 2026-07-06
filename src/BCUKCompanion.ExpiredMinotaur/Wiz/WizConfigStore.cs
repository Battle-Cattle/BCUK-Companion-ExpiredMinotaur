using System.IO;
using System.Text.Json;
using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

public sealed class WizConfigStore
{
    private readonly JsonSerializerOptions serializerOptions;

    public WizConfigStore(string dataFolderName, EventActionTypeRegistry registry)
    {
        ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), dataFolderName, "wiz-config.json");
        serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new EventActionJsonConverter(registry) },
        };
    }

    public string ConfigFilePath { get; }

    public WizConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return new WizConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<WizConfig>(json, serializerOptions) ?? new WizConfig();
        }
        catch (JsonException)
        {
            TryBackUpCorruptConfig();
            return new WizConfig();
        }
    }

    public void Save(WizConfig config)
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
