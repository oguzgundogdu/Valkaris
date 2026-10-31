using Valkaris.Core.Model;

namespace Valkaris.Ai;

/// <summary>Works out which troops a village can spare and how strong they are.</summary>
public sealed class Forces
{
    private readonly AiView _view;

    public Forces(AiView view) => _view = view;

    public double AttackOf(IReadOnlyDictionary<UnitType, int> troops) =>
        troops.Sum(t => t.Value * _view.Units.Units[t.Key].Attack);

    /// <summary>Fighting troops beyond the garrison the profile keeps home. Spearmen stay first, and Swordsmen make up any shortfall.</summary>
    public Dictionary<UnitType, int> Spare(Village village)
    {
        var spears = village.TroopCount(UnitType.Spearman);
        var swords = village.TroopCount(UnitType.Swordsman);
        var cavalry = village.TroopCount(UnitType.Cavalry);
        var keep = (int)Math.Ceiling((spears + swords + cavalry) * _view.Profile.GarrisonShare);

        var spare = new Dictionary<UnitType, int>
        {
            [UnitType.Cavalry] = cavalry,
            [UnitType.Swordsman] = Math.Max(0, swords - Math.Max(0, keep - spears)),
            [UnitType.Spearman] = Math.Max(0, spears - keep),
        };
        return spare.Where(t => t.Value > 0).ToDictionary(t => t.Key, t => t.Value);
    }

    /// <summary>This many troops from those available, fastest first. Null if there aren't enough.</summary>
    public Dictionary<UnitType, int>? PickFastest(IReadOnlyDictionary<UnitType, int> available, int count)
    {
        var group = new Dictionary<UnitType, int>();
        var remaining = count;
        foreach (var (unit, present) in available.OrderByDescending(t => _view.Units.Units[t.Key].Speed).ThenBy(t => t.Key))
        {
            var used = Math.Min(present, remaining);
            if (used > 0)
                group[unit] = used;
            remaining -= used;
        }
        return remaining > 0 ? null : group;
    }

    /// <summary>The smallest group from these troops with at least the given attack, strongest units first. Null if they fall short.</summary>
    public Dictionary<UnitType, int>? Pick(IReadOnlyDictionary<UnitType, int> available, double attack)
    {
        var group = new Dictionary<UnitType, int>();
        var remaining = attack;
        foreach (var (unit, count) in available.OrderByDescending(t => _view.Units.Units[t.Key].Attack).ThenBy(t => t.Key))
        {
            if (remaining <= 0)
                break;

            var power = _view.Units.Units[unit].Attack;
            var used = Math.Min(count, (int)Math.Ceiling(remaining / power));
            group[unit] = used;
            remaining -= used * power;
        }
        return remaining > 0 ? null : group;
    }
}
