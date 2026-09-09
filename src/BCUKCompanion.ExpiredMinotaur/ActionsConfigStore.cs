using System.Diagnostics;
using System.Net;
using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur;

public sealed class ActionsConfigStore(string dataFolderName, EventActionTypeRegistry registry)
    : EventActionConfigStore<ActionsConfig>(dataFolderName, "actions-config.json", registry)
{
    // Hides the base Load() (not virtual) so that, in addition to the base class's
    // whole-file corruption handling, individual devices with an unparsable IpAddress
    // (e.g. from a hand-edited actions-config.json) are dropped rather than flowing through
    // to runtime code such as WizClient, which needs a valid IP to operate on.
    public new ActionsConfig Load()
    {
        var config = base.Load();

        List<Wiz.WizDevice>? validDevices = null;
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
}
