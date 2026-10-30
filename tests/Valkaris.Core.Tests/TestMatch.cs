using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

internal static class TestMatch
{
    public const double OneHour = 3600;

    public static PlayerSlot[] Slots(int count) => Enumerable.Range(1, count)
        .Select(i => new PlayerSlot($"Player {i}", Nation.Valdren, IsAi: i > 1))
        .ToArray();

    public static GameState Create(
        int players = 2,
        double duration = OneHour,
        double speed = 1,
        GameData? data = null) =>
        GameFactory.CreateMatch(
            new GameSettings(duration, speed, MapSize.Small),
            Slots(players),
            data ?? TestData.Shipped);
}
