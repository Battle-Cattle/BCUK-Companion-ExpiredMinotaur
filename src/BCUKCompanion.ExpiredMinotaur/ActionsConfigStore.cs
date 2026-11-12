using System.Diagnostics;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;

namespace BCUKCompanion.ExpiredMinotaur;

/// <summary>
/// Loads and saves <c>actions-config.json</c>, dropping Wiz devices whose IP address can't be parsed.
/// </summary>
/// <param name="dataFolderName">Name of the per-app data folder under the user's profile that holds the config file.</param>
/// <param name="registry">Registry of the action types the stored actions are deserialized against.</param>
public sealed class ActionsConfigStore(string dataFolderName, EventActionTypeRegistry registry)
    : EventActionConfigStore<ActionsConfig>(dataFolderName, "actions-config.json", registry)
{
    /// <summary>
    /// Drops devices whose IpAddress isn't valid (see <see cref="WizAddress.IsValid"/>), e.g. a
    /// shorthand typo in a hand-edited actions-config.json, in addition to the base class's
    /// whole-file corruption handling, so they never reach runtime code such as WizClient, which
    /// needs a valid IP to operate on.
    /// </summary>
    /// <param name="config">The config as read from disk, or a new one when the file doesn't exist.</param>
    /// <returns><paramref name="config"/>, minus any invalid devices.</returns>
    protected override ActionsConfig OnLoaded(ActionsConfig config)
    {
        List<WizDevice>? validDevices = null;
        for (var i = 0; i < config.Devices.Count; i++)
        {
            var device = config.Devices[i];
            if (WizAddress.IsValid(device.IpAddress))
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
}
