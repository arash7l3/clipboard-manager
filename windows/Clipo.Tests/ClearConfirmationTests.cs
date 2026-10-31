using Clipo.Services;

namespace Clipo.Tests;

public sealed class ClearConfirmationTests
{
    private DateTime _now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheFirstPressOnlyArms()
    {
        var clear = Confirmation();

        Assert.False(clear.Press());
        Assert.True(clear.IsArmed);
    }

    [Fact]
    public void ASecondPressInTimeClearsAndDisarms()
    {
        var clear = Confirmation();
        clear.Press();
        _now += TimeSpan.FromSeconds(2);

        Assert.True(clear.Press());
        Assert.False(clear.IsArmed);
        // The next press starts over.
        Assert.False(clear.Press());
    }

    [Fact]
    public void ALateSecondPressArmsAgain()
    {
        var clear = Confirmation();
        clear.Press();
        _now += TimeSpan.FromSeconds(3);

        Assert.False(clear.IsArmed);
        Assert.False(clear.Press());
        Assert.True(clear.IsArmed);
    }

    [Fact]
    public void DisarmingNeedsTwoPressesAgain()
    {
        var clear = Confirmation();
        clear.Press();
        clear.Disarm();

        Assert.False(clear.Press());
        Assert.True(clear.Press());
    }

    private ClearConfirmation Confirmation() => new(TimeSpan.FromSeconds(3), () => _now);
}
