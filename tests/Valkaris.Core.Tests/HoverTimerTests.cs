using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class HoverTimerTests
{
    private const double Frame = 0.05;

    private readonly HoverTimer<int> _timer = new();

    private void Run(int? under, bool overCard, double seconds)
    {
        for (var elapsed = 0.0; elapsed < seconds; elapsed += Frame)
            _timer.Update(under, overCard, Frame);
    }

    [Fact]
    public void ItOpensOnlyAfterTheDelay()
    {
        Run(7, false, 0.2);
        Assert.Null(_timer.Shown);
        Run(7, false, 0.2);
        Assert.Equal(7, _timer.Shown);
    }

    [Fact]
    public void PassingOverSomethingDoesNotOpenIt()
    {
        Run(7, false, 0.15);
        Run(8, false, 0.15);
        Run(9, false, 0.15);

        Assert.Null(_timer.Shown);
    }

    [Fact]
    public void ItStaysWhileOnTheVillage()
    {
        Run(7, false, 0.4);
        Run(7, false, 5);

        Assert.Equal(7, _timer.Shown);
    }

    [Fact]
    public void ItStaysWhileOnTheCard()
    {
        Run(7, false, 0.4);
        Run(null, true, 5);

        Assert.Equal(7, _timer.Shown);
    }

    [Fact]
    public void ThereIsTimeToCrossTheGapToTheCard()
    {
        Run(7, false, 0.4);
        Run(null, false, 0.2);
        Assert.Equal(7, _timer.Shown);

        Run(null, true, 1);
        Assert.Equal(7, _timer.Shown);
    }

    [Fact]
    public void LeavingBothClosesIt()
    {
        Run(7, false, 0.4);
        Run(null, false, 0.4);

        Assert.Null(_timer.Shown);
    }

    [Fact]
    public void RestingOnAnotherVillageMovesTheCard()
    {
        Run(7, false, 0.4);
        Run(8, false, 0.2);
        Assert.Equal(7, _timer.Shown);
        Run(8, false, 0.2);
        Assert.Equal(8, _timer.Shown);
    }

    [Fact]
    public void BeingOverTheCardDoesNotOpenOneThatIsNotShown()
    {
        Run(7, true, 0.4);

        Assert.Equal(7, _timer.Shown);
    }
}
