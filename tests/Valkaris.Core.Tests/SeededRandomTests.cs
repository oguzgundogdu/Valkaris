using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class SeededRandomTests
{
    private static GameState Match(ulong seed) => GameFactory.CreateMatch(
        new GameSettings(TestMatch.OneHour, 1, MapSize.Small, seed), TestMatch.Slots(2), TestData.Shipped);

    private static List<ulong> Sequence(GameState state, int count = 20) =>
        Enumerable.Range(0, count).Select(_ => state.NextRandom()).ToList();

    [Fact]
    public void SameSeedGivesTheSameSequence() =>
        Assert.Equal(Sequence(Match(42)), Sequence(Match(42)));

    [Fact]
    public void DifferentSeedsGiveDifferentSequences() =>
        Assert.NotEqual(Sequence(Match(42)), Sequence(Match(43)));

    [Fact]
    public void RestoredStateContinuesTheSameSequence()
    {
        var original = Match(42);
        Sequence(original);

        var restored = SnapshotAssert.RoundTrips(original);

        Assert.Equal(Sequence(original), Sequence(restored));
    }

    [Fact]
    public void LimitedValuesStayInRange()
    {
        var state = Match(1);

        var values = Enumerable.Range(0, 1000).Select(_ => state.NextRandom(7)).ToList();

        Assert.All(values, v => Assert.InRange(v, 0, 6));
        Assert.Equal(7, values.Distinct().Count());
    }
}
