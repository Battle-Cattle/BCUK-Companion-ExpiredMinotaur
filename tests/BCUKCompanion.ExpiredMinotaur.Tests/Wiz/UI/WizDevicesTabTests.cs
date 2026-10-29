using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.UI;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz.UI;

public sealed class WizDevicesTabTests
{
    private static readonly WizDevice Desk = new(Guid.NewGuid(), "Desk", "192.168.1.20", WizDeviceType.Light, true, true, true);
    private static readonly WizDevice Fan = new(Guid.NewGuid(), "Fan", "192.168.1.22", WizDeviceType.Plug, false, false, false);

    /// <summary>A tab over Desk and Fan, with mapped actions on both, recording what it tells its host.</summary>
    private sealed class Rig
    {
        public Rig(WizClient? client = null)
        {
            Tab = new WizDevicesTab(
                Devices,
                client ?? new WizClient(),
                new WizDevicesTabHost(
                    Statuses.Add,
                    predicate =>
                    {
                        var removed = Actions.RemoveAll(a => predicate(a));
                        return removed;
                    },
                    () => Refreshes++,
                    () => new WizActionContext(new WizClient(), Devices)));
        }

        public ObservableCollection<WizDevice> Devices { get; } = [Desk, Fan];

        public List<IEventAction> Actions { get; } =
        [
            new WizSetColorAction { DeviceId = Desk.Id, R = 1 },
            new WizTurnOnAction { DeviceId = Desk.Id },
            new WizTurnOnAction { DeviceId = Fan.Id },
            new DelayAction(),
        ];

        public List<string> Statuses { get; } = [];

        public int Refreshes { get; private set; }

        public WizDevicesTab Tab { get; }

        public ListBox List => WpfTest.All<ListBox>(Tab).Single();

        /// <summary>Answers the next dialog by running <paramref name="drive"/> on it.</summary>
        public void OnDialog(Action<Window> drive) => Tab.ShowDialogWindow = window => WpfTest.ShowDialog(window, drive);

        public void Click(string button) => WpfTest.Click(Tab, button);
    }

    private static void Fill(Window dialog, string name, string ip, bool supportsColor = true)
    {
        var boxes = WpfTest.All<TextBox>(dialog).ToList();
        boxes[0].Text = name;
        boxes[1].Text = ip;
        WpfTest.All<CheckBox>(dialog).First().IsChecked = supportsColor;
        WpfTest.Click(dialog, "OK");
    }

    [SkippableFact]
    public void Add_AddsTheEnteredDevice()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig();
            rig.OnDialog(dialog => Fill(dialog, "Hall", "192.168.1.30"));

            rig.Click("Add");

            Assert.Equal("Hall", rig.Devices[^1].Name);
            Assert.Equal(3, rig.List.Items.Count);
        });
    }

    [SkippableFact]
    public void Add_RejectsADuplicateAddress()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig();
            rig.OnDialog(dialog => Fill(dialog, "Copy", Desk.IpAddress));

            rig.Click("Add");

            Assert.Equal(2, rig.Devices.Count);
            Assert.Equal([WizDeviceList.DuplicateIpMessage], rig.Statuses);
        });
    }

    [SkippableFact]
    public void Add_Cancelled_ChangesNothing()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig();
            rig.OnDialog(_ => { });

            rig.Click("Add");

            Assert.Equal(2, rig.Devices.Count);
            Assert.Empty(rig.Statuses);
        });
    }

    [SkippableFact]
    public void EditAndRemove_AskForASelection()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig();

            rig.Click("Edit");
            rig.Click("Remove");

            Assert.Equal(["Select a device to edit.", "Select a device to remove."], rig.Statuses);
        });
    }

    [SkippableFact]
    public void Edit_ReplacesTheDevice_AndDropsActionsItNoLongerSupports()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig();
            rig.List.SelectedItem = Desk;
            rig.OnDialog(dialog => Fill(dialog, "Desk lamp", Desk.IpAddress, supportsColor: false));

            rig.Click("Edit");

            Assert.Equal("Desk lamp", rig.Devices[0].Name);
            Assert.Equal(Desk.Id, rig.Devices[0].Id);
            Assert.DoesNotContain(rig.Actions, a => a is WizSetColorAction);
            Assert.Equal(3, rig.Actions.Count);
            Assert.Equal(1, rig.Refreshes);
            Assert.Equal([WizDeviceList.UpdatedMessage(1)], rig.Statuses);
        });
    }

    [SkippableFact]
    public void Edit_RejectsAnotherDevicesAddress()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig();
            rig.List.SelectedItem = Desk;
            rig.OnDialog(dialog => Fill(dialog, "Desk", Fan.IpAddress));

            rig.Click("Edit");

            Assert.Equal(Desk, rig.Devices[0]);
            Assert.Equal([WizDeviceList.DuplicateIpMessage], rig.Statuses);
        });
    }

    [SkippableFact]
    public void Edit_Cancelled_ChangesNothing()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig();
            rig.List.SelectedItem = Desk;
            rig.OnDialog(_ => { });

            rig.Click("Edit");

            Assert.Equal(Desk, rig.Devices[0]);
            Assert.Empty(rig.Statuses);
        });
    }

    [SkippableFact]
    public void Remove_RemovesTheDeviceAndItsActions()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig();
            rig.List.SelectedItem = Fan;

            rig.Click("Remove");

            Assert.Equal([Desk], rig.Devices);
            Assert.Equal(3, rig.Actions.Count);
            Assert.Equal([WizDeviceList.RemovedMessage(1)], rig.Statuses);
        });
    }

    [SkippableFact]
    public void Discover_AddsThePickedDevices()
    {
        WindowsOnly.Skip();
        using var fake = new FakeWizDevice((_, _) => """{"result":{"moduleName":"ESP25_SHRGB_01"}}""");
        WpfTest.Run(() =>
        {
            var rig = new Rig(fake.CreateClient());
            rig.Tab.ShowDialogWindow = window => WpfTest.ShowDialog(window, async dialog =>
            {
                var list = WpfTest.All<ListBox>(dialog).Single();
                await WpfTest.WaitUntil(() => list.Items.Count > 0);
                list.SelectAll();
                WpfTest.Click(dialog, "Add Selected");
            });

            rig.Click("Discover...");

            Assert.Equal(3, rig.Devices.Count);
            Assert.Equal(IPAddress.Loopback.ToString(), rig.Devices[^1].IpAddress);
        });
    }

    [SkippableFact]
    public void Discover_Closed_AddsNothing()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var rig = new Rig(new WizClient(1, IPAddress.Loopback));
            rig.OnDialog(dialog => WpfTest.Click(dialog, "Close"));

            rig.Click("Discover...");

            Assert.Equal(2, rig.Devices.Count);
        });
    }
}
