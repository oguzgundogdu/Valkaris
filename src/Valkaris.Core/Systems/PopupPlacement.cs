namespace Valkaris.Core.Systems;

/// <summary>A rectangle on screen, in pixels.</summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Overlaps(ScreenRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>Where a popup opens: beside its target, never over it, and always inside the area it may use.</summary>
public static class PopupPlacement
{
    public const double Gap = 12;

    /// <summary>Tries right, left, below and above. Centres in the area only when none of them has room.</summary>
    public static (double X, double Y) Beside(ScreenRect target, double width, double height, ScreenRect area)
    {
        var maxX = Math.Max(area.X, area.Right - width);
        var maxY = Math.Max(area.Y, area.Bottom - height);
        var alongX = Math.Clamp(target.X + target.Width / 2 - width / 2, area.X, maxX);
        var alongY = Math.Clamp(target.Y + target.Height / 2 - height / 2, area.Y, maxY);

        var options = new[]
        {
            (X: target.Right + Gap, Y: alongY),
            (X: target.X - Gap - width, Y: alongY),
            (X: alongX, Y: target.Bottom + Gap),
            (X: alongX, Y: target.Y - Gap - height),
        };

        foreach (var (x, y) in options)
        {
            var popup = new ScreenRect(x, y, width, height);
            if (x >= area.X && y >= area.Y && popup.Right <= area.Right && popup.Bottom <= area.Bottom && !popup.Overlaps(target))
                return (x, y);
        }
        return Centred(width, height, area);
    }

    public static (double X, double Y) Centred(double width, double height, ScreenRect area) =>
        (Math.Max(area.X, area.X + (area.Width - width) / 2), Math.Max(area.Y, area.Y + (area.Height - height) / 2));
}
