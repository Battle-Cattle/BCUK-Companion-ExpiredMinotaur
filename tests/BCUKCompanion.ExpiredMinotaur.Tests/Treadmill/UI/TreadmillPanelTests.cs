using System.Windows.Controls;
using BCUKCompanion.ExpiredMinotaur.Treadmill.UI;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill.UI;

public sealed class TreadmillPanelTests
{
    [SkippableFact]
    public void ConnectAndDisconnect_FollowTheConnection()
    {
        WindowsOnly.Skip();
        var treadmill = new FakeTreadmill();
        WpfTest.Run(async () =>
        {
            var panel = new TreadmillPanel(treadmill.Client);
            var connect = WpfTest.Button(panel, "Connect Treadmill");
            var disconnect = WpfTest.Button(panel, "Disconnect Treadmill");
            Assert.True(connect.IsEnabled);
            Assert.False(disconnect.IsEnabled);

            WpfTest.Click(connect);
            await WpfTest.WaitUntil(() => WpfTest.Texts(panel).Contains("Connected."));
            await WpfTest.WaitUntil(() => WpfTest.Texts(panel).Any(t => t.StartsWith("Current: 3") && t.Contains("Target: 3")));
            Assert.False(connect.IsEnabled);
            Assert.True(disconnect.IsEnabled);

            WpfTest.Click(disconnect);
            await WpfTest.WaitUntil(() => WpfTest.Texts(panel).Contains("Disconnected."));
            Assert.True(connect.IsEnabled);
            Assert.False(disconnect.IsEnabled);

            panel.Detach();
        });
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void AConnectThatThrows_IsReported()
    {
        WindowsOnly.Skip();
        var treadmill = new FakeTreadmill(() => new FakeTreadmillDevice { ConnectThrows = new InvalidOperationException("adapter off") });
        WpfTest.Run(async () =>
        {
            var panel = new TreadmillPanel(treadmill.Client);

            WpfTest.Click(panel, "Connect Treadmill");

            await WpfTest.WaitUntil(() => WpfTest.Texts(panel).Contains("Connect failed: adapter off"));
            Assert.True(WpfTest.Button(panel, "Connect Treadmill").IsEnabled);
        });
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void AfterDetach_StatusMessagesAreIgnored()
    {
        WindowsOnly.Skip();
        var treadmill = new FakeTreadmill();
        WpfTest.Run(async () =>
        {
            var panel = new TreadmillPanel(treadmill.Client);
            panel.Detach();

            await treadmill.Client.DisconnectAsync();
            await Task.Delay(100);

            Assert.DoesNotContain("Disconnected.", WpfTest.Texts(panel));
        });
        treadmill.Client.Dispose();
    }
}
