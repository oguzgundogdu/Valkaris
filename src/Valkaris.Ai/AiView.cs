using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>
/// Everything the AI may know, and the only way it can act. It shows what a player's own UI shows, and no more:
/// never other players' garrisons, resources or loyalty, and never whether another player is human.
/// </summary>
public sealed class AiView
{
    private readonly GameState _state;
    private readonly GameData _data;
    private readonly Func<ICommand, CommandResult> _send;

    public AiView(GameState state, GameData data, int playerId, Func<ICommand, CommandResult> send)
    {
        _state = state;
        _data = data;
        _send = send;
        PlayerId = playerId;
        Profile = data.Ai.Difficulties[state.Players[playerId].Difficulty ?? AiDifficulty.Normal];
    }

    public int PlayerId { get; }
    public AiProfile Profile { get; }
    public int RejectedCommands { get; private set; }

    // The rules of the game, which any player can read in the UI. The map's garrison data is deliberately left out.
    public AiConfig Config => _data.Ai;
    public BuildingsConfig Buildings => _data.Buildings;
    public UnitsConfig Units => _data.Units;
    public VillagesConfig VillageRules => _data.Villages;
    public CombatConfig Combat => _data.Combat;
    public double MinFoundingDistance => _data.Map.MinFoundingDistance;

    public double Now => _state.ElapsedTime;
    public double GameSpeed => _state.Settings.SpeedMultiplier;
    public ulong Seed => _state.Settings.Seed;
    public int MapWidth => _state.Map.Width;
    public int MapHeight => _state.Map.Height;

    public bool IsEliminated => _state.Players[PlayerId].IsEliminated;
    public int? CapitalVillageId => _state.Players[PlayerId].CapitalVillageId;
    public bool MustChooseCapital => _state.Players[PlayerId].CapitalChoiceDeadline is not null;

    /// <summary>Lowest id first, so choices don't depend on dictionary order.</summary>
    public IReadOnlyList<Village> OwnVillages =>
        _state.Villages.Values.Where(v => v.OwnerId == PlayerId).OrderBy(v => v.Id).ToList();

    public IReadOnlyList<KnownVillage> KnownVillages =>
        _state.Villages.Values.OrderBy(v => v.Id).Select(ToKnown).ToList();

    public IReadOnlyList<Movement> OwnMovements =>
        _state.Movements.Where(m => m.OwnerId == PlayerId).ToList();

    public IReadOnlyList<IncomingAttack> Incoming =>
        _state.Movements
            .Where(m => m.OwnerId != PlayerId && m.Type is MovementType.Attack or MovementType.Raid)
            .Where(m => _state.Villages[m.ToVillageId!.Value].OwnerId == PlayerId)
            .Select(m => new IncomingAttack(m.OwnerId, m.FromVillageId, m.ToVillageId!.Value, m.Type, m.ArrivalTime))
            .ToList();

    /// <summary>Reports this player is part of, oldest first. Both sides of a battle get the same report.</summary>
    public IEnumerable<BattleReport> ReportsSince(int reportId) =>
        _state.Reports.Where(r => r.Id > reportId && (r.AttackerId == PlayerId || r.DefenderId == PlayerId));

    /// <summary>Reports from this player's own Scouts.</summary>
    public IEnumerable<ScoutReport> ScoutReportsSince(int reportId) =>
        _state.ScoutReports.Where(r => r.Id > reportId && r.SenderId == PlayerId);

    /// <summary>What this player noticed of others scouting them.</summary>
    public IEnumerable<ScoutNotice> ScoutNoticesSince(int reportId) =>
        _state.ScoutNotices.Where(r => r.Id > reportId && r.DefenderId == PlayerId);

    public Terrain TerrainAt(TileCoord tile) => _state.Map.TileAt(tile).Terrain;

    public BuildOption PreviewBuild(int villageId, BuildingType building) =>
        BuildPreview.Evaluate(_state, _data, PlayerId, villageId, building);

    public TrainOption PreviewTrain(int villageId, UnitType unit, int count) =>
        TrainPreview.Evaluate(_state, _data, PlayerId, villageId, unit, count);

    public SendOption PreviewSend(SendTroopsCommand command) =>
        SendPreview.Evaluate(_state, _data, PlayerId, command);

    public FoundOption PreviewFound(int fromVillageId, TileCoord tile) =>
        FoundPreview.Evaluate(_state, _data, PlayerId, fromVillageId, tile);

    public SendResourcesOption PreviewSendResources(SendResourcesCommand command) =>
        SendResourcesPreview.Evaluate(_state, _data, PlayerId, command);

    public int FreeCarriers(Village own) => CarrierRules.Free(_state, _data, own);

    public double CapacityOf(Village own) => BuildingRules.VillageCapacity(_data, own);

    public Resources ProductionOf(Village own) => BuildingRules.VillageProduction(_data, own);

    public bool Send(ICommand command)
    {
        var result = _send(command);
        if (!result.Success)
            RejectedCommands++;
        return result.Success;
    }

    private KnownVillage ToKnown(Village village) => new(
        village.Id,
        village.OwnerId,
        village.Position,
        village.Name,
        village.BarbarianTier is { } tier ? _data.Map.Barbarians.Tiers[tier].Name : null,
        LoyaltyRules.IsCapital(_state, village));
}
