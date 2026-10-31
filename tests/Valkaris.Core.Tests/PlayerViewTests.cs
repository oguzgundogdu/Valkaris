using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Valkaris.Ai;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class PlayerViewTests
{
    private const int Me = 1;
    private const int Other = 2;

    private static string Json(PlayerViewSnapshot view) => JsonSerializer.Serialize(view, SnapshotMapper.JsonOptions);

    private static PlayerViewSnapshot View(GameState state, int player = Me) => PlayerView.Of(state, TestData.Shipped, player);

    /// <summary>Puts a marked value in every place that must stay hidden from this player.</summary>
    private static GameState Planted(GameState state, int me)
    {
        var full = SnapshotMapper.ToSnapshot(state);
        var copy = SnapshotMapper.Restore(full with { Settings = full.Settings with { Seed = 777001 } });
        copy.RngState = 777002;
        copy.NextMovementId = 777003;
        copy.NextReportId = 777004;

        foreach (var player in copy.Players.Values.Where(p => p.Id != me))
        {
            player.Difficulty = AiDifficulty.Hard;
            player.CapitalChoiceDeadline = 777005.5;
            player.RecordCapture(777006);
        }

        foreach (var village in copy.Villages.Values.Where(v => v.OwnerId != me))
        {
            village.Resources = new Resources(777007.5, 777008.5, 777009.5);
            village.SetBuildingLevel(BuildingType.Wall, 777010);
            village.AddTroops(UnitType.Cavalry, 777011);
            village.EnqueueBuild(new BuildQueueItem(BuildingType.Farm, 777012, 777013.5, 777014.5));
            village.EnqueueTraining(new TrainingQueueItem(UnitType.Scout, 777015, 777016.5, 777017.5));
            village.Loyalty = 777018.5;
            village.NextTroopRegenTime = 777019.5;
        }

        var mine = copy.Villages.Values.First(v => v.OwnerId == me);
        var theirs = copy.Villages.Values.First(v => v.OwnerId != me);
        var troops = new Dictionary<UnitType, int> { [UnitType.Cavalry] = 777020 };
        var loot = new Resources(777021.5, 0, 0);
        Movement Sent(int id, int? to, MovementType type) => new(id, theirs.OwnerId, theirs.Id, to, type, troops, loot, copy.ElapsedTime, copy.ElapsedTime + 1000, BuildingType.Farm, null, null, 777022);

        copy.AddMovement(Sent(900001, mine.Id, MovementType.Raid));
        copy.AddMovement(Sent(900002, mine.Id, MovementType.Scout));
        copy.AddMovement(Sent(900003, theirs.Id, MovementType.Reinforce));
        copy.AddReport(new BattleReport(777023, 777024.5, MovementType.Raid, theirs.OwnerId, Village.BarbarianOwnerId, theirs.Id, theirs.Id, troops, troops, troops, troops, 0, 1, 1, true, loot, null));
        copy.AddScoutReport(new ScoutReport(777025, 777026.5, theirs.OwnerId, me, theirs.Id, mine.Id, troops, true, troops, 777027, loot));
        copy.AddScoutNotice(new ScoutNotice(777028, 777029.5, me, theirs.OwnerId, theirs.Id, true, 777030, 777031));
        copy.AddBattleArrival(new BattleArrival(copy.ElapsedTime, theirs.OwnerId, me, "x", mine.Id, "y"));
        return copy;
    }

    private static void AssertNothingPlantedShows(GameState state, int me)
    {
        // Someone out of the match has no village left to aim at, and is covered by the rules check.
        if (state.Villages.Values.All(v => v.OwnerId != me))
            return;

        var view = View(Planted(state, me), me);
        Assert.DoesNotContain(view.State.Movements, m => m.OwnerId != me && m.CatapultTarget is not null);
        Assert.DoesNotContain(view.State.Movements, m => m.Id is 900002 or 900003);

        var json = Json(view);
        var leak = Regex.Match(json, @"\b777\d{3}\b");
        Assert.False(leak.Success, $"Player {me} received {leak.Value}: {json[Math.Max(0, leak.Index - 80)..Math.Min(json.Length, leak.Index + 20)]}");
        Assert.DoesNotContain("difficulty\":\"", json);
    }

    private static void AssertFollowsTheRules(GameState state, int me)
    {
        var view = View(state, me);

        Assert.Equal(0, view.State.NextMovementId + view.State.NextReportId);
        Assert.Equal(0UL, view.State.RngState);
        Assert.Empty(view.State.BattleArrivals);
        Assert.All(view.State.Players, p => Assert.Null(p.Difficulty));
        Assert.All(view.State.Villages.Where(v => v.OwnerId != me), v =>
        {
            Assert.Equal(Resources.Zero, v.Resources);
            Assert.Empty(v.Buildings);
            Assert.Empty(v.Troops);
            Assert.Empty(v.BuildQueue);
            Assert.Empty(v.TrainingQueue);
        });
        Assert.All(view.State.Movements.Where(m => m.OwnerId != me), m =>
        {
            Assert.True(m.Type is MovementType.Attack or MovementType.Raid);
            Assert.Equal(me, state.Villages[m.ToVillageId!.Value].OwnerId);
            Assert.Empty(m.Troops);
            Assert.Equal(Resources.Zero, m.Loot);
            Assert.Null(m.CatapultTarget);
        });
        Assert.Equal(state.Movements.Count(m => m.OwnerId == me), view.State.Movements.Count(m => m.OwnerId == me));
        Assert.All(view.State.Reports, r => Assert.True(r.AttackerId == me || r.DefenderId == me));
        Assert.All(view.State.ScoutReports, r => Assert.Equal(me, r.SenderId));
        Assert.All(view.State.ScoutNotices, n => Assert.Equal(me, n.DefenderId));
        Assert.DoesNotContain(view.Sightings, s => s.Type == MovementType.Scout);
    }

    [Fact]
    public void AClientNeverReceivesHiddenDataAtAnyStageOfAMatch()
    {
        var difficulties = new[] { AiDifficulty.Hard, AiDifficulty.Hard, AiDifficulty.Normal, AiDifficulty.Normal };
        var match = new AiMatch(difficulties, seed: 5, minutes: 40, speed: 10, size: MapSize.Medium);
        var sawForeignMovement = false;
        var sawReport = false;

        while (!match.State.IsMatchOver)
        {
            match.Session.Tick(60);
            foreach (var player in match.State.Players.Keys)
            {
                AssertFollowsTheRules(match.State, player);
                AssertNothingPlantedShows(match.State, player);
                var view = View(match.State, player);
                sawForeignMovement |= view.State.Movements.Any(m => m.OwnerId != player) || view.Sightings.Count > 0;
                sawReport |= view.State.Reports.Count > 0;
            }
        }

        Assert.True(sawForeignMovement && sawReport, "The match should have exercised movements and reports.");
    }

    [Fact]
    public void TheSeedIsHashedTheSameForEveryone()
    {
        var state = TestMatch.Create(players: 3, seed: 42);

        Assert.NotEqual(42UL, View(state).State.Settings.Seed);
        Assert.Equal(View(state, 1).State.Settings.Seed, View(state, 2).State.Settings.Seed);
    }

    [Fact]
    public void OwnVillagesStayInFullAndOthersKeepOnlyWhatTheMapShows()
    {
        var state = TestMatch.Create(players: 2);
        state.Villages[Me].AddTroops(UnitType.Cavalry, 5);
        var view = View(state);

        Assert.Equal(5, view.State.Villages.Single(v => v.Id == Me).Troops[UnitType.Cavalry]);
        var other = state.Villages.Values.First(v => v.OwnerId == Other);
        var seen = view.State.Villages.Single(v => v.Id == other.Id);
        Assert.Equal((other.Name, other.Position, other.OwnerId), (seen.Name, seen.Position, seen.OwnerId));
    }

    [Fact]
    public void AFilteredViewRestoresIntoAWorkingState()
    {
        var state = TestMatch.Create(players: 2);
        var restored = SnapshotMapper.Restore(View(state).State);

        Assert.Equal(state.Villages.Count, restored.Villages.Count);
        Assert.Equal(0, restored.Villages.Values.First(v => v.OwnerId == Other).BuildingLevel(BuildingType.Wall));
    }

    // A new field must be judged here before it can reach another player.
    [Theory]
    [InlineData(typeof(GameStateSnapshot), "Settings,ElapsedTime,Players,Villages,Map,Movements,Reports,ScoutReports,ScoutNotices,BattleArrivals,NextMovementId,NextReportId,RngState,Outcome")]
    [InlineData(typeof(PlayerSnapshot), "Id,Name,Nation,IsAi,IsEliminated,HasSurrendered,CapitalVillageId,CapitalChoiceDeadline,CapturedVillageIds,Difficulty")]
    [InlineData(typeof(VillageSnapshot), "Id,OwnerId,Position,Resources,Buildings,BuildQueue,Troops,TrainingQueue,Name,BarbarianTier,NextTroopRegenTime,Loyalty")]
    [InlineData(typeof(Movement), "Id,OwnerId,FromVillageId,ToVillageId,Type,Troops,Loot,DepartureTime,ArrivalTime,CatapultTarget,TargetTile,FromTile,Carriers")]
    [InlineData(typeof(SightedMovement), "Id,Type,X,Y,VelocityX,VelocityY,Time")]
    [InlineData(typeof(PublicNotice), "Time,FromVillageName,TargetVillageId,TargetVillageName")]
    [InlineData(typeof(Clash), "Time,VillageId")]
    public void EveryFieldSentToAPlayerHasBeenJudged(Type type, string known)
    {
        var fields = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").Select(p => p.Name).ToHashSet();

        Assert.Equal(known.Split(',').ToHashSet(), fields);
    }
}
