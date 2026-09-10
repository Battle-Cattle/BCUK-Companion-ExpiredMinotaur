using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;

namespace BCUKCompanion.ExpiredMinotaur;

public sealed class ActionsConfigStore(string dataFolderName, EventActionTypeRegistry registry)
    : EventActionConfigStore<ActionsConfig>(dataFolderName, "actions-config.json", registry)
{
    // Shapes of the two per-integration config files this replaced (wiz-config.json,
    // treadmill-config.json), kept only so a pre-consolidation install's devices and mappings
    // can be migrated into actions-config.json instead of silently disappearing.
    private sealed record LegacyWizConfig
    {
        public List<WizDevice> Devices { get; init; } = [];
        public List<EventActionMapping> Mappings { get; init; } = [];
    }

    private sealed record LegacyTreadmillConfig
    {
        public List<EventActionMapping> Mappings { get; init; } = [];
    }

    // Hides the base Load() (not virtual) so that, in addition to the base class's
    // whole-file corruption handling, individual devices with an unparsable IpAddress
    // (e.g. from a hand-edited actions-config.json) are dropped rather than flowing through
    // to runtime code such as WizClient, which needs a valid IP to operate on.
    public new ActionsConfig Load()
    {
        var config = File.Exists(ConfigFilePath) ? base.Load() : TryMigrateLegacyConfig();

        List<WizDevice>? validDevices = null;
        for (var i = 0; i < config.Devices.Count; i++)
        {
            var device = config.Devices[i];
            if (IPAddress.TryParse(device.IpAddress, out _))
            {
                validDevices?.Add(device);
                continue;
            }

            Debug.WriteLine(
                $"Discarding Wiz device '{device.Name}' ({device.Id}) from '{ConfigFilePath}': " +
                $"invalid IP address '{device.IpAddress}'.");

            validDevices ??= [.. config.Devices.Take(i)];
        }

        return validDevices is null ? config : config with { Devices = validDevices };
    }

    // A pre-consolidation install stored Wiz devices/mappings in wiz-config.json and Treadmill
    // mappings in treadmill-config.json (see git history of this file for their shapes). If
    // neither exists this is a fresh install and base.Load() (returning a new ActionsConfig)
    // covers it; otherwise combine what's there into one ActionsConfig and persist it so this
    // migration runs at most once. The legacy files are left in place rather than deleted -
    // they're inert once actions-config.json exists, and keeping them is a safer failure mode
    // than deleting a user's only copy of their old settings if this migration has a bug.
    private ActionsConfig TryMigrateLegacyConfig()
    {
        var directory = Path.GetDirectoryName(ConfigFilePath)!;
        var wizPath = Path.Combine(directory, "wiz-config.json");
        var treadmillPath = Path.Combine(directory, "treadmill-config.json");

        if (!File.Exists(wizPath) && !File.Exists(treadmillPath))
        {
            return base.Load();
        }

        var options = new JsonSerializerOptions { Converters = { new EventActionJsonConverter(registry) } };
        var devices = new List<WizDevice>();
        var mappings = new List<EventActionMapping>();

        if (File.Exists(wizPath))
        {
            TryDeserializeLegacyFile<LegacyWizConfig>(wizPath, options, legacy =>
            {
                devices.AddRange(legacy.Devices);
                mappings.AddRange(legacy.Mappings);
            });
        }

        if (File.Exists(treadmillPath))
        {
            TryDeserializeLegacyFile<LegacyTreadmillConfig>(treadmillPath, options, legacy =>
                mappings.AddRange(legacy.Mappings));
        }

        var migrated = new ActionsConfig { Devices = devices, Mappings = mappings };
        Save(migrated);
        return migrated;
    }

    private static void TryDeserializeLegacyFile<TLegacy>(string path, JsonSerializerOptions options, Action<TLegacy> onSuccess)
    {
        try
        {
            var legacy = JsonSerializer.Deserialize<TLegacy>(File.ReadAllText(path), options);
            if (legacy is not null)
            {
                onSuccess(legacy);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Failed to migrate legacy config '{path}': {ex}");
        }
    }
}
