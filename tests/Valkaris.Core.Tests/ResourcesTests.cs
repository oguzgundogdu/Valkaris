using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

public class ResourcesTests
{
    [Fact]
    public void AddAndSubtractWorkPerResource()
    {
        var a = new Resources(10, 20, 30);
        var b = new Resources(1, 2, 3);

        Assert.Equal(new Resources(11, 22, 33), a + b);
        Assert.Equal(new Resources(9, 18, 27), a - b);
    }

    [Fact]
    public void MultiplyScalesEveryResource() =>
        Assert.Equal(new Resources(2, 4, 6), new Resources(1, 2, 3) * 2);

    [Theory]
    [InlineData(10, 20, 30, true)]
    [InlineData(5, 5, 5, true)]
    [InlineData(11, 20, 30, false)]
    [InlineData(10, 21, 30, false)]
    [InlineData(10, 20, 31, false)]
    public void CoversRequiresEveryResource(double wood, double stone, double food, bool expected) =>
        Assert.Equal(expected, new Resources(10, 20, 30).Covers(new Resources(wood, stone, food)));

    [Fact]
    public void CappedAtLimitsEachResourceIndependently() =>
        Assert.Equal(new Resources(10, 15, 15), new Resources(10, 20, 30).CappedAt(15));

    [Theory]
    [InlineData(ResourceType.Wood, 1)]
    [InlineData(ResourceType.Stone, 2)]
    [InlineData(ResourceType.Food, 3)]
    public void GetReturnsTheNamedResource(ResourceType type, double expected) =>
        Assert.Equal(expected, new Resources(1, 2, 3).Get(type));

    [Fact]
    public void OfSetsOnlyTheNamedResource()
    {
        Assert.Equal(new Resources(5, 0, 0), Resources.Of(ResourceType.Wood, 5));
        Assert.Equal(new Resources(0, 5, 0), Resources.Of(ResourceType.Stone, 5));
        Assert.Equal(new Resources(0, 0, 5), Resources.Of(ResourceType.Food, 5));
    }
}
