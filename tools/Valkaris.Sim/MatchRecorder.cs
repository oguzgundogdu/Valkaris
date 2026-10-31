using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Sim;

/// <summary>Watches a match from outside, with full access to the state, and turns it into a MatchResult.</summary>
public sealed class MatchRecorder
{
    private readonly GameState _state;
    private readonly GameData _data;
    private readonly Dictionary<int, double> _firstBarracks = new();
    private readonly Dictionary<int, double> _firstSettler = new();
    private readonly Dictionary<int, double> _firstLord = new();
    private readonly Dictionary<int, double> _firstFounded = new();
    private readonly Dictionary<int, double> _firstCapture = new();
    private readonly Dictionary<string, int> _rejections = new();
    private readonly Dictionary<int, double> _defenceSpent = new();
    private readonly Dictionary<int, Dictionary<UnitType, double>> _unitSpending = new();
    private readonly Dictionary<int, (double Wall, double Home)> _atMid = new();
    private double _lastSample;
    private double _samples;
    private double _atCap;
    private double _productionLost;
    private double _productionTotal;
    private int _founded;
    private int _barbarianCaptures;
    private int _playerCaptures;
    private int _capitalCaptures;
    private int _eliminations;
    private int _transfers;
    private double _moved;
    private double _lostInTransfer;
    private int _commands;

    public MatchRecorder(GameState state, GameData data)
    {
        _state = state;
        _data = data;
    }

    public void Attach(LocalGameSession session) => session.EventRaised += OnEvent;

    public void RecordCommand(CommandResult result)
    {
        _commands++;
        if (result.Rejection is { } rejection)
            _rejections[rejection.ToString()] = _rejections.GetValueOrDefault(rejection.ToString()) + 1;
    }

    private void OnEvent(GameEvent gameEvent)
    {
        if (gameEvent is TroopsTrained batch)
            AddUnitSpending(_state.Villages[batch.VillageId].OwnerId, batch.Unit, batch.Count);

        switch (gameEvent)
        {
            case BuildingCompleted { Building: BuildingType.Wall } wall:
                AddDefenceSpent(_state.Villages[wall.VillageId].OwnerId, BuildingRules.UpgradeCost(_data, BuildingType.Wall, wall.NewLevel));
                break;
            case TroopsTrained { Unit: UnitType.Spearman } spears:
                AddDefenceSpent(_state.Villages[spears.VillageId].OwnerId, _data.Units.Units[UnitType.Spearman].Cost * spears.Count);
                break;
            case BuildingCompleted { Building: BuildingType.Barracks } built:
                _firstBarracks.TryAdd(_state.Villages[built.VillageId].OwnerId, built.Time);
                break;
            case TroopsTrained { Unit: UnitType.Settler } trained:
                _firstSettler.TryAdd(_state.Villages[trained.VillageId].OwnerId, trained.Time);
                break;
            case TroopsTrained { Unit: UnitType.Lord } trained:
                _firstLord.TryAdd(_state.Villages[trained.VillageId].OwnerId, trained.Time);
                break;
            case VillageFounded founded:
                _founded++;
                _firstFounded.TryAdd(_state.Villages[founded.VillageId].OwnerId, founded.Time);
                break;
            case VillageCaptured captured:
                _firstCapture.TryAdd(captured.NewOwnerId, captured.Time);
                if (captured.PreviousOwnerId == Village.BarbarianOwnerId)
                    _barbarianCaptures++;
                else
                    _playerCaptures++;
                break;
            case CapitalChoiceRequired:
                _capitalCaptures++;
                break;
            case PlayerEliminated:
                _eliminations++;
                break;
            case ResourcesDelivered delivered:
                _transfers++;
                _moved += Total(delivered.Delivered);
                _lostInTransfer += Total(delivered.Lost);
                break;
            case CarriersReturned returned:
                _moved += Total(returned.Delivered);
                _lostInTransfer += Total(returned.Lost);
                break;
        }
    }

    /// <summary>Settlers aren't military, so they stay out of the unit mix.</summary>
    private void AddUnitSpending(int playerId, UnitType unit, int count)
    {
        if (unit == UnitType.Settler)
            return;

        if (!_unitSpending.TryGetValue(playerId, out var spending))
            _unitSpending[playerId] = spending = new Dictionary<UnitType, double>();
        spending[unit] = spending.GetValueOrDefault(unit) + Total(_data.Units.Units[unit].Cost) * count;
    }

    private double ValueOf(IReadOnlyDictionary<UnitType, int> troops) =>
        troops.Sum(t => Total(_data.Units.Units[t.Key].Cost) * t.Value);

    private void AddDefenceSpent(int playerId, Resources cost) =>
        _defenceSpent[playerId] = _defenceSpent.GetValueOrDefault(playerId) + Total(cost);

    /// <summary>The mean Wall level and defence power per village: what an attacker meets, with the attacker's mix unknown.</summary>
    private (double Wall, double Home) MeasureDefence(int playerId)
    {
        var villages = _state.Villages.Values.Where(v => v.OwnerId == playerId).ToList();
        if (villages.Count == 0)
            return (0, 0);

        double HomePower(Village village)
        {
            var troops = village.Troops.Sum(t => t.Value * (_data.Units.Units[t.Key].DefenceVsInfantry + _data.Units.Units[t.Key].DefenceVsCavalry) / 2);
            return troops * (1 + _data.Combat.WallBonusPerLevel * village.BuildingLevel(BuildingType.Wall));
        }

        return (villages.Average(v => v.BuildingLevel(BuildingType.Wall)), villages.Average(HomePower));
    }

    /// <summary>Looks at every player's village and resource. Call this at regular times.</summary>
    public void Sample()
    {
        if (_atMid.Count == 0 && _state.ElapsedTime >= _state.Settings.MatchDurationSeconds / 2)
        {
            foreach (var id in _state.Players.Keys)
                _atMid[id] = MeasureDefence(id);
        }

        var elapsed = _state.ElapsedTime - _lastSample;
        _lastSample = _state.ElapsedTime;
        var gameSeconds = elapsed * _state.Settings.SpeedMultiplier;

        foreach (var village in _state.Villages.Values.Where(v => !v.IsBarbarian))
        {
            var capacity = BuildingRules.VillageCapacity(_data, village);
            var production = BuildingRules.VillageProduction(_data, village);
            foreach (var resource in Enum.GetValues<ResourceType>())
            {
                _samples++;
                _productionTotal += production.Get(resource) * gameSeconds;
                if (village.Resources.Get(resource) >= capacity)
                {
                    _atCap++;
                    _productionLost += production.Get(resource) * gameSeconds;
                }
            }
        }
    }

    public MatchResult Finish(ulong seed)
    {
        var outcome = _state.Outcome ?? throw new InvalidOperationException("The match has not ended.");
        var battles = _state.Reports;

        bool IsAttackOnPlayer(BattleReport r) => r.DefenderId != Village.BarbarianOwnerId;

        var seats = _state.Players.Values.OrderBy(p => p.Id).Select(player =>
        {
            var score = outcome.Scores.Single(s => s.PlayerId == player.Id);
            var sent = battles.Where(r => r.AttackerId == player.Id && IsAttackOnPlayer(r)).ToList();
            var atEnd = MeasureDefence(player.Id);
            var hasMid = _atMid.TryGetValue(player.Id, out var atMid);
            return new SeatResult(
                player.Id,
                player.Difficulty ?? AiDifficulty.Normal,
                outcome.WinnerIds.Contains(player.Id),
                player.IsEliminated,
                score.Villages,
                score.Score,
                _firstBarracks.TryGetValue(player.Id, out var barracks) ? barracks : null,
                _firstSettler.TryGetValue(player.Id, out var settler) ? settler : null,
                _firstLord.TryGetValue(player.Id, out var lord) ? lord : null,
                _firstFounded.TryGetValue(player.Id, out var founded) ? founded : null,
                _firstCapture.TryGetValue(player.Id, out var capture) ? capture : null,
                sent.Count,
                battles.Count(r => r.DefenderId == player.Id),
                sent.GroupBy(r => r.DefenderId).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                new SeatDefence(
                    _defenceSpent.GetValueOrDefault(player.Id),
                    hasMid ? atMid.Wall : null,
                    atEnd.Wall,
                    hasMid ? atMid.Home : null,
                    atEnd.Home,
                    sent.Count(r => r.AttackerWon)),
                ScoutingOf(player, sent),
                _unitSpending.TryGetValue(player.Id, out var spending) ? spending.OrderBy(s => s.Key).ToDictionary(s => s.Key, s => s.Value) : new Dictionary<UnitType, double>());
        }).ToList();

        return new MatchResult(
            seed,
            outcome.Reason,
            outcome.Time,
            outcome.WinnerIds.Count > 1,
            seats,
            _samples,
            _atCap,
            _productionLost,
            _productionTotal,
            battles.Count,
            battles.Where(r => r.DefenderId == Village.BarbarianOwnerId).Sum(r => Total(r.Loot)),
            battles.Where(IsAttackOnPlayer).Sum(r => Total(r.Loot)),
            _founded,
            _barbarianCaptures,
            _playerCaptures,
            _capitalCaptures,
            _eliminations,
            _transfers,
            _moved,
            _lostInTransfer,
            _commands,
            _rejections.OrderBy(r => r.Key).ToDictionary(r => r.Key, r => r.Value),
            battles.Where(IsAttackOnPlayer).Sum(r => ValueOf(r.AttackerLost)),
            battles.Where(IsAttackOnPlayer).Sum(r => ValueOf(r.DefenderLost)));
    }

    private SeatScouting ScoutingOf(Player player, IReadOnlyList<BattleReport> attacks)
    {
        var scouted = _state.ScoutReports.Where(r => r.SenderId == player.Id).ToList();
        var maxAge = _data.Ai.Difficulties[player.Difficulty ?? AiDifficulty.Normal].ScoutMaxAgeSeconds / _state.Settings.SpeedMultiplier;

        return new SeatScouting(
            scouted.Count,
            scouted.Count(r => r.Succeeded),
            scouted.Where(r => !r.Succeeded).Sum(r => r.Sent.Values.Sum()),
            attacks.Count(attack => scouted.Any(r => r.Succeeded
                && r.TargetVillageId == attack.DefenderVillageId
                && r.Time <= attack.Time
                && attack.Time - r.Time <= maxAge + TravelAllowance(attack))));
    }

    /// <summary>News can be at its limit when the army leaves, and the army still has to walk there.</summary>
    private double TravelAllowance(BattleReport attack) =>
        UnitRules.TravelSeconds(_data, _state, _state.Villages[attack.AttackerVillageId].Position, _state.Villages[attack.DefenderVillageId].Position, attack.AttackerSent);

    private static double Total(Resources resources) => resources.Wood + resources.Stone + resources.Food;
}
