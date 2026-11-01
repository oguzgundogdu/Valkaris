using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class TopBarLayoutTests
{
    private static readonly TopBarWidths Widths = new(
        Brand: 100, Amounts: 180, Rates: 150, Capacities: 150, Stats: 80, StatLabels: 70,
        ViewLabels: 130, ViewIcons: 80, WarningsFull: 300, WarningsShort: 180, WarningsTimeOnly: 100,
        Fixed: 200, Gaps: 7, ChipGaps: 4);

    [Fact]
    public void WithRoomEverythingShows()
    {
        Assert.Equal(TopBarLayout.Levels[0], TopBarLayout.Choose(Widths, 5000));
    }

    [Fact]
    public void EachLevelIsNarrowerThanTheLast()
    {
        var widths = TopBarLayout.Levels.Select(plan => TopBarLayout.Width(plan, Widths)).ToList();

        Assert.Equal(widths.OrderDescending(), widths);
        Assert.Equal(widths.Count, widths.Distinct().Count());
    }

    [Fact]
    public void DetailDropsInTheAgreedOrder()
    {
        var levels = TopBarLayout.Levels;

        Assert.False(levels[1].Brand);
        Assert.True(levels[1].Rates);
        Assert.False(levels[2].Rates);
        Assert.True(levels[2].Capacity);
        Assert.False(levels[3].Capacity);
        Assert.True(levels[3].ViewLabels);
        Assert.False(levels[4].ViewLabels);
        Assert.Equal(TopBarWarning.Full, levels[5].Warning);
        Assert.Equal(TopBarWarning.Short, levels[6].Warning);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void EachLevelIsChosenWhenItJustFits(int level)
    {
        var plan = TopBarLayout.Levels[level];

        Assert.Equal(plan, TopBarLayout.Choose(Widths, TopBarLayout.Width(plan, Widths)));
    }

    [Fact]
    public void WithNoRoomAtAllTheLeastDetailIsUsed()
    {
        Assert.Equal(TopBarLayout.Levels[^1], TopBarLayout.Choose(Widths, 10));
    }
}
