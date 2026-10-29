using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

// The default constructors wire in Windows' Bluetooth APIs, but only use them once asked to scan or connect.
public sealed class DefaultConstructionTests
{
    [SkippableFact]
    public void TheRealTreadmillParts_StartIdleAndDisconnected()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "The default parts are Windows' Bluetooth APIs.");

        using var client = new TreadmillClient();
        using var device = new FtmsDevice();
        _ = new FitnessMachineScanner();

        Assert.False(client.IsConnected);
        Assert.False(device.IsConnected);
        Assert.Empty(device.LastConnectWarnings);
        Assert.Equal(FtmsConstants.MinSpeedKmh, client.MinSpeedKmh);
    }
}
