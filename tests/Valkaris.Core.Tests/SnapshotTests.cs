using System.Reflection;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Tests;

internal static class SnapshotAssert
{
    /// <summary>State -> snapshot -> JSON -> snapshot -> restored state must lose nothing.</summary>
    public static GameState RoundTrips(GameState state)
    {
        var json = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(state));
        var restored = SnapshotMapper.Restore(SnapshotMapper.FromJson(json));

        Assert.Equal(json, SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(restored)));
        return restored;
    }
}

public class SnapshotTests
{
    // Computed from other state, so deliberately absent from snapshots.
    private static readonly HashSet<string> DerivedProperties = new() { $"{nameof(GameState)}.{nameof(GameState.IsMatchOver)}", $"{nameof(Village)}.{nameof(Village.IsBarbarian)}", $"{nameof(Village)}.{nameof(Village.IsAbandoned)}" };

    private static bool IsRecord(Type type) =>
        type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    private static IEnumerable<string> PropertiesMissingFromSnapshot(Type model, Type snapshot)
    {
        var snapshotProperties = snapshot.GetProperties().Select(p => p.Name).ToHashSet();
        return model.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !snapshotProperties.Contains(name) && !DerivedProperties.Contains($"{model.Name}.{name}"));
    }

    private static GameState BusyState()
    {
        var state = TestMatch.Create(players: 3);
        state.ElapsedTime = 123.456;
        state.Players[2].IsEliminated = true;
        state.Players[3].CapitalVillageId = 2;
        state.Players[1].CapitalVillageId = null;
        state.Players[1].CapitalChoiceDeadline = 300.5;
        state.Players[1].RecordCapture(7);
        state.Players[2].Difficulty = AiDifficulty.Hard;
        state.Outcome = new MatchOutcome(MatchEndReason.TimeUp, 123.456, new[] { 1, 3 }, new[] { new PlayerScore(1, 2, 30, 1, 155.5) });

        var village = state.Villages[1];
        village.OwnerId = 3;
        village.Resources = new Resources(1.5, 2.25, 1.0 / 3.0);
        village.SetBuildingLevel(BuildingType.Wall, 4);
        village.EnqueueBuild(new BuildQueueItem(BuildingType.Farm, 2, 100.25, 200.5));
        village.EnqueueBuild(new BuildQueueItem(BuildingType.Farm, 3, 200.5, 300.75));
        village.AddTroops(UnitType.Cavalry, 7);
        village.Name = "Brennhold";
        village.Loyalty = 62.5;
        village.BarbarianTier = 1;
        village.NextTroopRegenTime = 77.5;
        state.Map.Tiles[5].Terrain = Terrain.Water;
        village.EnqueueTraining(new TrainingQueueItem(UnitType.Swordsman, 5, 100, 250));

        var troops = new Dictionary<UnitType, int> { [UnitType.Cavalry] = 3, [UnitType.Catapult] = 1 };
        state.AddMovement(new Movement(4, 1, 1, 2, MovementType.Attack, troops, Resources.Zero, 100, 150.5, BuildingType.Wall));
        state.AddMovement(new Movement(5, 1, 2, 1, MovementType.Return, troops, new Resources(1, 2, 3), 100, 151, null));
        state.AddMovement(new Movement(6, 1, 1, null, MovementType.Settle, troops, Resources.Zero, 100, 152, null, new TileCoord(3, 4)));
        state.AddMovement(new Movement(7, 1, 1, 2, MovementType.Transfer, new Dictionary<UnitType, int>(), new Resources(100, 0, 50), 100, 153, null, Carriers: 2));
        state.AddReport(new BattleReport(
            2, 90.5, MovementType.Raid, 1, 2, 1, 2, troops, troops, troops, troops, 3, 120.5, 80.25, true,
            new Resources(10, 20, 30), new CatapultDamage(BuildingType.Wall, 3, 2), new LoyaltyChange(30, 0, true), CapitalProtected: true));
        state.NextMovementId = 8;
        state.NextReportId = 3;
        state.NextRandom();
        return state;
    }

    [Fact]
    public void NewMatchRoundTrips() => SnapshotAssert.RoundTrips(TestMatch.Create(players: 4));

    [Fact]
    public void BusyStateRoundTrips() => SnapshotAssert.RoundTrips(BusyState());

    [Fact]
    public void RestoredStateHasTheOriginalValues()
    {
        var original = BusyState();

        var restored = SnapshotAssert.RoundTrips(original);

        Assert.Equal(original.Settings, restored.Settings);
        Assert.Equal(123.456, restored.ElapsedTime);
        Assert.True(restored.Players[2].IsEliminated);
        Assert.Equal(2, restored.Players[3].CapitalVillageId);
        Assert.Equal("Player 1", restored.Players[1].Name);

        var village = restored.Villages[1];
        Assert.Equal(3, village.OwnerId);
        Assert.Equal(original.Villages[1].Position, village.Position);
        Assert.Equal(original.Villages[1].Resources, village.Resources);
        Assert.Equal(4, village.BuildingLevel(BuildingType.Wall));
        Assert.Equal(original.Villages[1].BuildQueue, village.BuildQueue);
        Assert.Equal(original.Villages[1].TroopCount(UnitType.Cavalry), village.TroopCount(UnitType.Cavalry));
        Assert.Equal(original.Villages[1].TrainingQueue, village.TrainingQueue);
        Assert.Equal("Brennhold", village.Name);
        Assert.Equal(1, village.BarbarianTier);
        Assert.Equal(77.5, village.NextTroopRegenTime);
        Assert.Equal(Terrain.Water, restored.Map.Tiles[5].Terrain);
        Assert.Equal(4, restored.Movements.Count);
        Assert.Equal(2, restored.Movements[3].Carriers);
        Assert.Equal(new Resources(100, 0, 50), restored.Movements[3].Loot);
        Assert.Null(restored.Players[1].CapitalVillageId);
        Assert.Equal(300.5, restored.Players[1].CapitalChoiceDeadline);
        Assert.Equal(new[] { 7 }, restored.Players[1].CapturedVillageIds);
        Assert.Equal(AiDifficulty.Hard, restored.Players[2].Difficulty);
        Assert.Null(restored.Players[1].Difficulty);
        Assert.Equal(new[] { 1, 3 }, restored.Outcome!.WinnerIds);
        Assert.Equal(155.5, restored.Outcome.Scores[0].Score);
        Assert.Equal(62.5, village.Loyalty);
        Assert.Null(restored.Movements[2].ToVillageId);
        Assert.Equal(new TileCoord(3, 4), restored.Movements[2].TargetTile);
        Assert.Equal(new LoyaltyChange(30, 0, true), restored.Reports[0].Loyalty);
        Assert.True(restored.Reports[0].CapitalProtected);
        Assert.Equal(BuildingType.Wall, restored.Movements[0].CatapultTarget);
        Assert.Equal(3, restored.Movements[1].Troops[UnitType.Cavalry]);
        Assert.Equal(new CatapultDamage(BuildingType.Wall, 3, 2), restored.Reports[0].CatapultDamage);
        Assert.Equal(8, restored.NextMovementId);
        Assert.Equal(3, restored.NextReportId);
        Assert.Equal(original.RngState, restored.RngState);
        Assert.Equal(village.Id, restored.Map.TileAt(village.Position).VillageId);
    }

    [Fact]
    public void TileCountMismatchIsRejected()
    {
        var snapshot = SnapshotMapper.ToSnapshot(TestMatch.Create());
        var broken = snapshot with { Map = snapshot.Map with { Tiles = new List<MapTileSnapshot>() } };

        Assert.Throws<ArgumentException>(() => SnapshotMapper.Restore(broken));
    }

    [Fact]
    public void EveryModelClassHasASnapshotCoveringItsProperties()
    {
        var core = typeof(GameState).Assembly;
        var modelClasses = core.GetTypes()
            .Where(t => t.Namespace == typeof(GameState).Namespace && t.IsClass && t.IsPublic && !IsRecord(t))
            .ToList();
        Assert.NotEmpty(modelClasses);

        foreach (var model in modelClasses)
        {
            var snapshot = core.GetType($"{typeof(GameStateSnapshot).Namespace}.{model.Name}Snapshot");
            Assert.True(snapshot is not null, $"{model.Name} has no {model.Name}Snapshot.");

            var missing = PropertiesMissingFromSnapshot(model, snapshot!).ToList();
            Assert.True(missing.Count == 0, $"{model.Name}Snapshot is missing: {string.Join(", ", missing)}");
        }
    }

    private sealed class ModelWithExtraProperty
    {
        public int Id { get; set; }
        public int NewField { get; set; }
    }

    private sealed record SnapshotWithoutIt(int Id);

    [Fact]
    public void GuardDetectsAMissingProperty() =>
        Assert.Equal(
            new[] { nameof(ModelWithExtraProperty.NewField) },
            PropertiesMissingFromSnapshot(typeof(ModelWithExtraProperty), typeof(SnapshotWithoutIt)));
}
