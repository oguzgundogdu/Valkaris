using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class AiRivalTests
{
    private static AiMatch Match(int players, ulong seed = 1, int[]? idle = null) =>
        new(Enumerable.Repeat(AiDifficulty.Normal, players).ToArray(), seed: seed, idle: idle);

    private static AiView View(GameState state, int player) =>
        new(state, TestData.Shipped, player, _ => CommandResult.Ok);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EachPlayerIsTheRivalOfExactlyOneOther(int players)
    {
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var match = Match(players, seed);
            var assignment = RivalPlanner.Assignment(match.Views[1]);

            Assert.Equal(players, assignment.Count);
            Assert.All(assignment, pair => Assert.NotEqual(pair.Key, pair.Value));
            Assert.Equal(players, assignment.Values.Distinct().Count());
        }
    }

    [Fact]
    public void EveryPlayerWorksOutTheSameAssignment()
    {
        var match = Match(4, 7);

        var first = RivalPlanner.Assignment(match.Views[1]);

        for (var player = 2; player <= 4; player++)
            Assert.Equal(first, RivalPlanner.Assignment(match.Views[player]));
    }

    [Fact]
    public void TheSeedDecidesIt()
    {
        var picks = Enumerable.Range(1, 30)
            .Select(seed => RivalPlanner.Assignment(Match(4, (ulong)seed).Views[1])[1])
            .Distinct();

        Assert.True(picks.Count() > 1);
        Assert.Equal(RivalPlanner.Assignment(Match(4, 5).Views[1]), RivalPlanner.Assignment(Match(4, 5).Views[1]));
    }

    [Fact]
    public void NearbyPlayersAreFavoured()
    {
        var nearest = 0;
        const int Seeds = 200;
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Match(4, seed);
            var home = match.State.Villages[1].Position;
            var rival = RivalPlanner.Assignment(match.Views[1])[1];
            var closest = new[] { 2, 3, 4 }.OrderBy(p => UnitRules.Distance(home, match.State.Villages[p].Position)).First();
            if (rival == closest)
                nearest++;
        }

        Assert.True(nearest > Seeds / 3, $"{nearest} of {Seeds}");
    }

    [Fact]
    public void AnEliminatedRivalIsReplaced()
    {
        var match = Match(3, idle: new[] { 1, 2, 3 });
        var view = View(match.State, 1);
        var rival = new RivalPlanner(view, new AiMemory(view));
        var first = rival.Current;
        Assert.True(match.Session.SendAs(first, new SurrenderCommand()).Success);

        rival.Update();

        Assert.NotEqual(first, rival.Current);
        Assert.NotEqual(1, rival.Current);
    }

    private static void Saw(GameState state, int player, int enemy, double power)
    {
        var none = new Dictionary<UnitType, int>();
        state.AddReport(new BattleReport(state.NextReportId++, state.ElapsedTime, MovementType.Attack, enemy, player, enemy, player, none, none, none, none, 0, power, power, true, Resources.Zero, null));
    }

    [Fact]
    public void AMuchStrongerRivalIsDroppedAfterTheCooldown()
    {
        var match = Match(3, idle: new[] { 1, 2, 3 });
        var view = View(match.State, 1);
        var memory = new AiMemory(view);
        var rival = new RivalPlanner(view, memory);
        var first = rival.Current;
        Saw(match.State, 1, first, 1000000);
        memory.Update();

        match.State.ElapsedTime = (match.Data.Ai.RivalSwitchCooldownSeconds + 1);
        rival.Update();

        Assert.NotEqual(first, rival.Current);
    }

    [Fact]
    public void ANewRivalIsKeptThroughTheCooldown()
    {
        var match = Match(3, idle: new[] { 1, 2, 3 });
        var view = View(match.State, 1);
        var memory = new AiMemory(view);
        var rival = new RivalPlanner(view, memory);
        var first = rival.Current;
        Saw(match.State, 1, first, 1000000);
        memory.Update();

        match.State.ElapsedTime = match.Data.Ai.RivalSwitchCooldownSeconds / 2;
        rival.Update();

        Assert.Equal(first, rival.Current);
    }

    [Fact]
    public void AnEvenRivalIsKept()
    {
        var match = Match(3, idle: new[] { 1, 2, 3 });
        var view = View(match.State, 1);
        var memory = new AiMemory(view);
        var rival = new RivalPlanner(view, memory);
        var first = rival.Current;
        match.State.Villages[1].AddTroops(UnitType.Swordsman, 100);
        Saw(match.State, 1, first, 3000);
        memory.Update();

        match.State.ElapsedTime = match.Data.Ai.RivalSwitchCooldownSeconds + 1;
        rival.Update();

        Assert.Equal(first, rival.Current);
    }

    [Fact]
    public void ThirdPartyArmiesAreNotSeen()
    {
        var match = Match(3, idle: new[] { 1, 2, 3 });
        var view = View(match.State, 1);
        var memory = new AiMemory(view);
        var none = new Dictionary<UnitType, int>();
        match.State.AddReport(new BattleReport(1, 0, MovementType.Attack, 2, 3, 2, 3, none, none, none, none, 0, 99999, 99999, true, Resources.Zero, null));

        memory.Update();

        Assert.Equal(0, memory.StrongestSeenOf(2));
    }

    [Fact]
    public void MostAttacksGoToTheRival()
    {
        var match = Match(4, 2);
        var rivals = RivalPlanner.Assignment(match.Views[1]);

        match.Run(900);

        var attacks = match.State.Reports.Where(r => r.Type == MovementType.Attack && r.DefenderId > 0).ToList();
        Assert.NotEmpty(attacks);
        var onRival = attacks.Count(r => rivals[r.AttackerId] == r.DefenderId);
        Assert.True(onRival > attacks.Count / 2, $"{onRival} of {attacks.Count}");
    }
}
