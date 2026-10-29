using System.Windows.Controls;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.UI;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz.UI;

public sealed class WizDeviceDialogTests
{
    private static readonly WizDevice Existing = new(Guid.NewGuid(), "Desk", "192.168.1.20", WizDeviceType.Plug, true, false, false);

    [SkippableFact]
    public void EditDialog_AddsADeviceFromTheEnteredFields()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var dialog = new WizDeviceEditDialog();
            Assert.Equal("Add Device", dialog.Title);

            var result = WpfTest.ShowDialog(dialog, window =>
            {
                var boxes = WpfTest.All<TextBox>(window).ToList();
                boxes[0].Text = "  Hall  ";
                boxes[1].Text = "192.168.1.30";
                WpfTest.All<CheckBox>(window).First().IsChecked = true;
                WpfTest.Click(window, "OK");
            });

            Assert.True(result);
            var device = dialog.Result!;
            Assert.Equal(("Hall", "192.168.1.30", WizDeviceType.Light), (device.Name, device.IpAddress, device.DeviceType));
            Assert.Equal((true, false, true), (device.SupportsColor, device.SupportsColorTemperature, device.SupportsDimming));
        });
    }

    [SkippableFact]
    public void EditDialog_KeepsTheExistingDevicesIdAndFields()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var dialog = new WizDeviceEditDialog(Existing);
            Assert.Equal("Edit Device", dialog.Title);

            Assert.True(WpfTest.ShowDialog(dialog, window => WpfTest.Click(window, "OK")));

            Assert.Equal(Existing, dialog.Result);
        });
    }

    [SkippableTheory]
    [InlineData("", "192.168.1.30", "Name is required.")]
    [InlineData("Hall", "not an ip", "Enter a valid IP address.")]
    public void EditDialog_ExplainsInvalidInputAndStaysOpen(string name, string ip, string expectedError)
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var dialog = new WizDeviceEditDialog();
            var stillOpen = false;
            var errors = new List<string>();

            WpfTest.ShowDialog(dialog, window =>
            {
                var boxes = WpfTest.All<TextBox>(window).ToList();
                boxes[0].Text = name;
                boxes[1].Text = ip;
                WpfTest.Click(window, "OK");
                stillOpen = window.IsVisible;
                errors.AddRange(WpfTest.Texts(window));
            });

            Assert.True(stillOpen);
            Assert.Contains(expectedError, errors);
            Assert.Null(dialog.Result);
        });
    }

    [SkippableFact]
    public void DiscoverDialog_ListsNewDevicesAndAddsTheSelectedOnes()
    {
        WindowsOnly.Skip();
        using var fake = new FakeWizDevice((_, _) => """{"result":{"moduleName":"ESP10_SOCKET_06"}}""");
        WpfTest.Run(() =>
        {
            var dialog = new WizDiscoverDevicesDialog(fake.CreateClient(), []);
            var statuses = new List<string>();

            var result = WpfTest.ShowDialog(dialog, async window =>
            {
                var list = WpfTest.All<ListBox>(window).Single();
                await WpfTest.WaitUntil(() => list.Items.Count > 0 && WpfTest.Texts(window).Any(t => t.StartsWith("Found")));
                statuses.AddRange(WpfTest.Texts(window));
                Assert.Equal("ESP10_SOCKET_06 - 127.0.0.1 (inferred: Plug)", list.Items[0]!.ToString());

                WpfTest.Click(window, "Add Selected");
                Assert.Contains("Select at least one device first.", WpfTest.Texts(window));

                list.SelectedIndex = 0;
                WpfTest.Click(window, "Add Selected");
            });

            Assert.True(result);
            Assert.Contains("Found 1 new device(s).", statuses);
            var added = Assert.Single(dialog.AddedDevices);
            Assert.Equal(("127.0.0.1", WizDeviceType.Plug), (added.IpAddress, added.DeviceType));
        });
    }

    [SkippableFact]
    public void DiscoverDialog_LeavesOutKnownDevices_AndRescansOnRequest()
    {
        WindowsOnly.Skip();
        using var fake = new FakeWizDevice((_, _) => """{"result":{"moduleName":"ESP25_SHRGB_01"}}""");
        WpfTest.Run(() =>
        {
            var dialog = new WizDiscoverDevicesDialog(fake.CreateClient(), ["127.0.0.1"]);

            var result = WpfTest.ShowDialog(dialog, async window =>
            {
                await WpfTest.WaitUntil(() => WpfTest.Texts(window).Contains("No new devices found."));

                WpfTest.Click(window, "Rescan");
                WpfTest.Click(window, "Rescan"); // ignored while the first rescan runs
                Assert.Contains("Scanning local network...", WpfTest.Texts(window));
                await WpfTest.WaitUntil(() => WpfTest.Texts(window).Contains("No new devices found."));

                WpfTest.Click(window, "Close");
            });

            Assert.False(result);
            Assert.Empty(dialog.AddedDevices);
            Assert.Equal(2, fake.Requests.Count);
        });
    }
}
