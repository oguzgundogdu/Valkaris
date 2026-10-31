using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Dev;

/// <summary>
/// Sets up late-game states for trying and testing the Villages rules while the rival is idle.
/// Not in release builds. Player 1 is the local player and player 2 the rival.
/// </summary>
public static class DevScenario
{
    /// <summary>For the UI test's pictures of every building stage.</summary>
    public static void SetLevel(Village village, BuildingType building, int level) => village.SetBuildingLevel(building, level);

    /// <summary>For the UI test: someone passing through the player's sight, a battle between others, and one at the player's own village.</summary>
    public static void AddThirdPartyActivity(GameState state, int playerId, int otherVillageId)
    {
        var near = state.Villages.Values.First(v => v.OwnerId == playerId);
        var other = state.Villages[otherVillageId];
        var passing = new TileCoord(near.Position.X + 1, near.Position.Y);
        var troops = new Dictionary<UnitType, int> { [UnitType.Spearman] = 9 };
        state.AddMovement(new Movement(state.NextMovementId++, other.OwnerId, other.Id, null, MovementType.Settle, troops, Resources.Zero,
            state.ElapsedTime, state.ElapsedTime + 600, null, passing, passing));
        state.AddMovement(new Movement(state.NextMovementId++, other.OwnerId, other.Id, near.Id, MovementType.Raid, troops, Resources.Zero,
            state.ElapsedTime, state.ElapsedTime + 600, null));
        state.AddBattleArrival(new BattleArrival(state.ElapsedTime, other.OwnerId, 99, other.Name, other.Id, other.Name));
        state.AddBattleArrival(new BattleArrival(state.ElapsedTime, other.OwnerId, playerId, other.Name, near.Id, near.Name));
    }

    /// <summary>Takes back the movements added above.</summary>
    public static void RemoveMovementsOf(GameState state, int ownerId)
    {
        foreach (var movement in state.Movements.Where(m => m.OwnerId == ownerId).ToList())
            state.RemoveMovement(movement);
    }

    public const string Villages = "villages";
    public const string Founding = "founding";
    public const string Endgame = "endgame";
    public const string LastStand = "laststand";
    public const string Empire = "empire";
    public const string StrayReturn = "strayreturn";
    public const string Scouting = "scouting";
    public const string CounterScouting = "counterscouting";

    private const int Local = 1;
    private const int Rival = 2;

    /// <summary>Returns false for an unknown name, leaving the state alone.</summary>
    public static bool Apply(GameState state, GameData data, string name)
    {
        switch (name)
        {
            case Villages:
                ApplyVillages(state, data);
                return true;
            case Founding:
                ApplyFounding(state);
                return true;
            case Endgame:
                ApplyEndgame(state);
                return true;
            case LastStand:
                ApplyLastStand(state);
                return true;
            case StrayReturn:
                ApplyStrayReturn(state);
                return true;
            case Scouting:
                ApplyScouting(state);
                return true;
            case CounterScouting:
                ApplyCounterScouting(state);
                return true;
            case Empire:
                while (state.Villages.Values.Any(v => v.IsBarbarian))
                    Take(state, data, Local, 2);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A rival with no Scouts yet, with both sides able to train more and scout each other. Each side has few Scouts to start with.</summary>
    private static void ApplyScouting(GameState state)
    {
        var rival = state.Villages[Rival];
        SetTroops(rival, (UnitType.Spearman, 3), (UnitType.Cavalry, 2));
        rival.SetBuildingLevel(BuildingType.Wall, 2);

        var home = state.Villages[Local];
        SetTroops(home, (UnitType.Spearman, 4), (UnitType.Scout, 2));

        foreach (var village in new[] { rival, home })
        {
            village.SetBuildingLevel(BuildingType.Barracks, 3);
            village.SetBuildingLevel(BuildingType.Warehouse, 8);
            village.Resources = new Resources(5000, 5000, 5000);
        }
    }

    /// <summary>A rival with Scouts and a Watchtower, with plenty of your own Scouts to try different margins against it.</summary>
    private static void ApplyCounterScouting(GameState state)
    {
        ApplyScouting(state);
        var rival = state.Villages[Rival];
        rival.AddTroops(UnitType.Scout, 20);
        rival.SetBuildingLevel(BuildingType.Watchtower, 2);
        state.Villages[Local].AddTroops(UnitType.Scout, 300);
    }

    /// <summary>Several villages and a big army against a protected capital, while your own capital is weak and nearly disloyal.</summary>
    private static void ApplyVillages(GameState state, GameData data)
    {
        var capital = state.Villages[Local];
        // Slow regeneration and no loyalty to speak of, so one attack takes it even after the Lords' long walk.
        capital.SetBuildingLevel(BuildingType.MainBuilding, 1);
        capital.SetBuildingLevel(BuildingType.Barracks, 5);
        capital.Loyalty = 1;
        capital.SetBuildingLevel(BuildingType.Marketplace, 3);
        SetTroops(capital, (UnitType.Spearman, 5));

        var army = Take(state, data, Local, 4);
        SetTroops(army, (UnitType.Swordsman, 150), (UnitType.Cavalry, 40), (UnitType.Catapult, 20), (UnitType.Lord, 3), (UnitType.Settler, 2));
        army.SetBuildingLevel(BuildingType.Marketplace, 3);
        var reserve = Take(state, data, Local, 2);
        SetTroops(reserve, (UnitType.Spearman, 30));

        var rivalCapital = state.Villages[Rival];
        rivalCapital.SetBuildingLevel(BuildingType.MainBuilding, data.Villages.Capital.LockedAboveMainLevel + 2);
        SetTroops(rivalCapital, (UnitType.Spearman, 10));
        var strikeForce = Take(state, data, Rival, 2);
        strikeForce.SetBuildingLevel(BuildingType.Marketplace, 1);
        SetTroops(strikeForce, (UnitType.Swordsman, 200), (UnitType.Lord, 2));
    }

    private static void ApplyFounding(GameState state)
    {
        var capital = state.Villages[Local];
        capital.SetBuildingLevel(BuildingType.MainBuilding, 10);
        capital.SetBuildingLevel(BuildingType.Barracks, 5);
        SetTroops(capital, (UnitType.Swordsman, 50), (UnitType.Settler, 3));
    }

    /// <summary>One attack with the Lord takes the rival's only village.</summary>
    private static void ApplyEndgame(GameState state)
    {
        SetTroops(state.Villages[Local], (UnitType.Swordsman, 100), (UnitType.Lord, 1));
        SetTroops(state.Villages[Rival]);
        state.Villages[Rival].Loyalty = 1;
    }

    /// <summary>One attack with the rival's Lord takes your only village.</summary>
    private static void ApplyLastStand(GameState state)
    {
        SetTroops(state.Villages[Local]);
        state.Villages[Local].Loyalty = 1;
        SetTroops(state.Villages[Rival], (UnitType.Swordsman, 100), (UnitType.Lord, 1));
    }

    /// <summary>The rival's troops walking home to a village that is yours by now. Only a battle after a capture can cause this.</summary>
    private static void ApplyStrayReturn(GameState state)
    {
        var troops = new Dictionary<UnitType, int> { [UnitType.Swordsman] = 5 };
        state.AddMovement(new Movement(state.NextMovementId++, Rival, Rival, Local, MovementType.Return, troops, Resources.Zero, 0, 600, null));
    }

    /// <summary>Hands the barbarian village nearest the player's capital to them, as a normal village.</summary>
    private static Village Take(GameState state, GameData data, int playerId, int mainLevel)
    {
        var capital = state.Villages[playerId];
        var village = state.Villages.Values
            .Where(v => v.IsBarbarian)
            .OrderBy(v => UnitRules.Distance(v.Position, capital.Position))
            .ThenBy(v => v.Id)
            .First();

        village.OwnerId = playerId;
        village.BarbarianTier = null;
        village.NextTroopRegenTime = null;
        foreach (var (type, level) in data.Game.FoundedVillage.Buildings)
            village.SetBuildingLevel(type, level);
        village.SetBuildingLevel(BuildingType.MainBuilding, mainLevel);
        return village;
    }

    private static void SetTroops(Village village, params (UnitType Unit, int Count)[] troops)
    {
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
        foreach (var (unit, count) in troops)
            village.AddTroops(unit, count);
    }
}
