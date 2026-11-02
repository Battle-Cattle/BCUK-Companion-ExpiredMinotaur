using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillDeviceSlotTests
{
    private readonly List<FakeTreadmillDevice> created = [];
    private readonly List<ITreadmillDevice> attached = [];
    private readonly TreadmillDeviceSlot slot;

    public TreadmillDeviceSlotTests()
    {
        slot = new TreadmillDeviceSlot(
            () =>
            {
                var device = new FakeTreadmillDevice();
                created.Add(device);
                return device;
            },
            attached.Add);
    }

    [Fact]
    public void StartsWithAFreshAttachedDevice()
    {
        var device = Assert.Single(created);
        Assert.Same(device, slot.Current);
        Assert.Same(device, Assert.Single(attached));
        Assert.False(device.Disposed);
    }

    [Fact]
    public void Replace_DisposesTheCurrentDeviceAndAttachesAFreshOne()
    {
        var first = slot.Current;

        slot.Replace();

        Assert.Equal(2, created.Count);
        Assert.True(created[0].Disposed);
        Assert.NotSame(first, slot.Current);
        Assert.Same(created[1], slot.Current);
        Assert.Same(created[1], attached[^1]);
        Assert.False(created[1].Disposed);
    }
}
