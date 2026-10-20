using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;

namespace BCUKCompanion.ExpiredMinotaur;

/// <summary>
/// Loads and saves <c>actions-config.json</c>: drops Wiz devices whose IP address can't be parsed,
/// and migrates the older per-integration config files the first time it runs.
/// </summary>
public sealed class ActionsConfigStore(string dataFolderName, EventActionTypeRegistry registry)
    : EventActionConfigStore<ActionsConfig>(dataFolderName, "actions-config.json", registry)
{
    // Copied into an explicit field (rather than referencing the primary constructor parameter
    // from a method) so the parameter isn't captured as well as passed to the base constructor
    // (CS9107).
    private readonly EventActionTypeRegistry registry = registry;

    // Shapes of the two per-integration config files this replaced (wiz-config.json,
    // treadmill-config.json), kept only so a pre-consolidation install's devices and mappings
    // can be migrated into actions-config.json instead of silently disappearing.
    /// <summary>The shape of the old <c>wiz-config.json</c>.</summary>
    private sealed record LegacyWizConfig
    {
        /// <summary>The WiZ devices.</summary>
        public List<WizDevice> Devices { get; init; } = [];

        /// <summary>The mappings, holding only Wiz actions.</summary>
        public List<EventActionMapping> Mappings { get; init; } = [];
    }

    /// <summary>The shape of the old <c>treadmill-config.json</c>.</summary>
    private sealed record LegacyTreadmillConfig
    {
        /// <summary>The mappings, holding only treadmill actions.</summary>
        public List<EventActionMapping> Mappings { get; init; } = [];
    }

    /// <summary>
    /// Drops devices with an unparsable IpAddress (e.g. from a hand-edited actions-config.json),
    /// in addition to the base class's whole-file corruption handling, so they never reach
    /// runtime code such as WizClient, which needs a valid IP to operate on.
    /// </summary>
    /// <param name="config">The config as read from disk (or migrated).</param>
    /// <returns><paramref name="config"/>, minus any invalid devices.</returns>
    protected override ActionsConfig OnLoaded(ActionsConfig config)
    {
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

    /// <summary>
    /// Runs only while actions-config.json doesn't exist. A pre-consolidation install stored Wiz
    /// devices/mappings in wiz-config.json and Treadmill mappings in treadmill-config.json (see
    /// git history of this file for their shapes). If neither exists this is a fresh install and
    /// gets a new ActionsConfig; otherwise combine what's there into one ActionsConfig and persist
    /// it so this migration runs at most once. The legacy files are left in place rather than
    /// deleted - they're inert once actions-config.json exists, and keeping them is a safer
    /// failure mode than deleting a user's only copy of their old settings if this migration has
    /// a bug.
    /// </summary>
    /// <returns>The migrated config, or a new one on a fresh install.</returns>
    protected override ActionsConfig CreateWhenMissing()
    {
        var directory = Path.GetDirectoryName(ConfigFilePath)!;
        var wizPath = Path.Combine(directory, "wiz-config.json");
        var treadmillPath = Path.Combine(directory, "treadmill-config.json");

        if (!File.Exists(wizPath) && !File.Exists(treadmillPath))
        {
            return new ActionsConfig();
        }

        var options = new JsonSerializerOptions { Converters = { new EventActionJsonConverter(registry) } };
        var devices = new List<WizDevice>();
        var mappings = new List<EventActionMapping>();
        var migratedEverything = true;

        if (File.Exists(wizPath))
        {
            migratedEverything &= TryDeserializeLegacyFile<LegacyWizConfig>(
                wizPath, options,
                legacy => legacy.Devices is not null && legacy.Mappings is not null,
                legacy =>
                {
                    devices.AddRange(legacy.Devices);
                    mappings.AddRange(legacy.Mappings);
                });
        }

        if (File.Exists(treadmillPath))
        {
            migratedEverything &= TryDeserializeLegacyFile<LegacyTreadmillConfig>(
                treadmillPath, options,
                legacy => legacy.Mappings is not null,
                legacy => mappings.AddRange(legacy.Mappings));
        }

        var migrated = new ActionsConfig { Devices = devices, Mappings = mappings };

        // Only persist (and so stop re-attempting migration on every Load()) once every
        // existing legacy file was read successfully - saving a partial result here would
        // make actions-config.json exist from then on, and Load() only migrates when it's
        // still missing, permanently losing whatever the failed file held. Save() re-enters
        // the lock Load() holds while calling this, which is safe (it's a Monitor).
        if (migratedEverything)
        {
            Save(migrated);
        }

        return migrated;
    }

    /// <summary>
    /// Reads one legacy config file and hands it to <paramref name="onSuccess"/>, or returns false
    /// if it can't be read, parsed or validated.
    /// </summary>
    /// <remarks>
    /// <paramref name="isValid"/> guards against a legacy file with an explicit JSON "null" for a
    /// required collection: System.Text.Json sets the property to null in that case (overriding
    /// the record's [] init default), and AddRange(null) would throw. Treat that the same as any
    /// other unreadable legacy file - a failed migration, not a crash.
    /// </remarks>
    /// <typeparam name="TLegacy">The legacy file's shape.</typeparam>
    /// <param name="path">The legacy file.</param>
    /// <param name="options">Serializer options that can read the file's actions.</param>
    /// <param name="isValid">Checks the parsed file's required collections are present.</param>
    /// <param name="onSuccess">Takes the parsed file's contents.</param>
    private static bool TryDeserializeLegacyFile<TLegacy>(
        string path, JsonSerializerOptions options, Func<TLegacy, bool> isValid, Action<TLegacy> onSuccess)
    {
        try
        {
            var legacy = JsonSerializer.Deserialize<TLegacy>(File.ReadAllText(path), options);
            if (legacy is null)
            {
                Debug.WriteLine($"Failed to migrate legacy config '{path}': deserialized to null.");
                return false;
            }

            if (!isValid(legacy))
            {
                Debug.WriteLine($"Failed to migrate legacy config '{path}': a required collection was null.");
                return false;
            }

            onSuccess(legacy);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Failed to migrate legacy config '{path}': {ex}");
            return false;
        }
    }
}
