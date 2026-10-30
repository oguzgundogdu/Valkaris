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
    public const string Villages = "villages";
    public const string Founding = "founding";
    public const string Endgame = "endgame";
    public const string LastStand = "laststand";

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
            default:
                return false;
        }
    }

    /// <summary>Several villages and a big army against a protected capital, while your own capital is weak and nearly disloyal.</summary>
    private static void ApplyVillages(GameState state, GameData data)
    {
        var capital = state.Villages[Local];
        // Slow regeneration and no loyalty to speak of, so one attack takes it even after the Lords' long walk.
        capital.SetBuildingLevel(BuildingType.MainBuilding, 1);
        capital.SetBuildingLevel(BuildingType.Barracks, 5);
        capital.Loyalty = 1;
        SetTroops(capital, (UnitType.Spearman, 5));

        var army = Take(state, data, Local, 4);
        SetTroops(army, (UnitType.Swordsman, 150), (UnitType.Cavalry, 40), (UnitType.Catapult, 20), (UnitType.Lord, 3), (UnitType.Settler, 2));
        var reserve = Take(state, data, Local, 2);
        SetTroops(reserve, (UnitType.Spearman, 30));

        var rivalCapital = state.Villages[Rival];
        rivalCapital.SetBuildingLevel(BuildingType.MainBuilding, data.Villages.Capital.LockedAboveMainLevel + 2);
        SetTroops(rivalCapital, (UnitType.Spearman, 10));
        var strikeForce = Take(state, data, Rival, 2);
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
