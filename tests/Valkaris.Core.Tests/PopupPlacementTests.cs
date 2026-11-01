using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class PopupPlacementTests
{
    private static readonly ScreenRect Area = new(0, 54, 1024, 586);
    private const double Width = 420, Height = 400;

    private static ScreenRect Placed(ScreenRect target)
    {
        var (x, y) = PopupPlacement.Beside(target, Width, Height, Area);
        return new ScreenRect(x, y, Width, Height);
    }

    public static IEnumerable<object[]> Targets()
    {
        for (var x = 0; x < 1000; x += 90)
        {
            for (var y = 54; y < 610; y += 70)
                yield return new object[] { x, y };
        }
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void APopupStaysInsideAndOffItsTarget(int x, int y)
    {
        var target = new ScreenRect(x, y, 40, 40);
        var popup = Placed(target);

        Assert.False(popup.Overlaps(target));
        Assert.InRange(popup.X, Area.X, Area.Right - Width);
        Assert.InRange(popup.Y, Area.Y, Area.Bottom - Height);
    }

    [Fact]
    public void ItPrefersTheRight()
    {
        var target = new ScreenRect(100, 200, 40, 40);

        Assert.Equal(target.Right + PopupPlacement.Gap, Placed(target).X);
    }

    [Fact]
    public void NearTheRightEdgeItGoesLeft()
    {
        var target = new ScreenRect(900, 200, 40, 40);

        Assert.Equal(target.X - PopupPlacement.Gap - Width, Placed(target).X);
    }

    [Fact]
    public void WithNoRoomAnywhereItCentres()
    {
        var huge = new ScreenRect(100, 100, 800, 500);
        var (x, y) = PopupPlacement.Beside(huge, Width, Height, Area);

        Assert.Equal(PopupPlacement.Centred(Width, Height, Area), (x, y));
    }

    [Fact]
    public void APopupTallerThanTheAreaStartsAtItsTop()
    {
        var (_, y) = PopupPlacement.Centred(Width, 900, Area);

        Assert.Equal(Area.Y, y);
    }
}
