using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>
/// Which player this AI is at war with. At the start, every AI works out the same pairing from the match seed and
/// start positions, which anyone can see, so that each player is the rival of exactly one other.
/// </summary>
public sealed class RivalPlanner
{
    private readonly AiView _view;
    private readonly AiMemory _memory;
    private readonly Forces _forces;
    private readonly AiRandom _random;
    private double _chosenAt;

    public RivalPlanner(AiView view, AiMemory memory)
    {
        _view = view;
        _memory = memory;
        _forces = new Forces(view, memory);
        _random = new AiRandom(view.Seed, view.PlayerId);
        Current = Assignment(view)[view.PlayerId];
    }

    public int Current { get; private set; }

    public void Update()
    {
        var enemies = Enemies();
        if (enemies.Count == 0)
            return;

        var gone = !enemies.Contains(Current);
        if (gone || (enemies.Count > 1 && HasProvedMuchStronger()))
            Choose(enemies);
    }

    private List<int> Enemies() =>
        _view.KnownVillages.Where(v => !v.IsBarbarian && v.OwnerId != _view.PlayerId).Select(v => v.OwnerId).Distinct().OrderBy(id => id).ToList();

    private bool HasProvedMuchStronger()
    {
        if ((_view.Now - _chosenAt) * _view.GameSpeed < _view.Config.RivalSwitchCooldownSeconds)
            return false;

        var own = _view.OwnVillages.Sum(v => _forces.AttackOf(v.Troops)) + _view.OwnMovements.Sum(m => _forces.AttackOf(m.Troops));
        return _memory.StrongestSeenOf(Current) > Math.Max(own, 1) * _view.Profile.RivalStrongerRatio;
    }

    /// <summary>A new rival, more likely if near. The current one only if there is no other.</summary>
    private void Choose(List<int> enemies)
    {
        var others = enemies.Where(id => id != Current).ToList();
        var candidates = others.Count > 0 ? others : enemies;
        var own = _view.OwnVillages;
        var weights = candidates
            .Select(id => Weight(_view.Config.RivalDistanceExponent, own.Min(v => _view.KnownVillages.Where(k => k.OwnerId == id).Min(k => UnitRules.Distance(v.Position, k.Position)))))
            .ToList();

        Current = candidates[Pick(weights, _random.NextDouble())];
        _chosenAt = _view.Now;
    }

    private static double Weight(double exponent, double distance) => Math.Pow(Math.Max(distance, 1), -exponent);

    private static int Pick(IReadOnlyList<double> weights, double roll)
    {
        var point = roll * weights.Sum();
        for (var i = 0; i < weights.Count; i++)
        {
            point -= weights[i];
            if (point <= 0)
                return i;
        }
        return weights.Count - 1;
    }

    /// <summary>
    /// The same answer for every AI: a seeded pick among the ways to give each player exactly one other, favouring nearby pairs.
    /// Independent picks would often send two AIs at one player.
    /// </summary>
    public static IReadOnlyDictionary<int, int> Assignment(AiView view)
    {
        var starts = view.KnownVillages
            .Where(v => !v.IsBarbarian)
            .GroupBy(v => v.OwnerId)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(v => v.Id).First())
            .ToList();

        var options = Derangements(starts.Count).ToList();
        var weights = options
            .Select(option => Enumerable.Range(0, starts.Count).Aggregate(1.0, (product, i) =>
                product * Weight(view.Config.RivalDistanceExponent, UnitRules.Distance(starts[i].Position, starts[option[i]].Position))))
            .ToList();

        var chosen = options[Pick(weights, new AiRandom(view.Seed, 0).NextDouble())];
        return Enumerable.Range(0, starts.Count).ToDictionary(i => starts[i].OwnerId, i => starts[chosen[i]].OwnerId);
    }

    /// <summary>Every way to give each index a different one, in a fixed order.</summary>
    private static IEnumerable<int[]> Derangements(int count)
    {
        IEnumerable<int[]> From(int[] partial, int position)
        {
            if (position == count)
            {
                yield return partial.ToArray();
                yield break;
            }

            for (var value = 0; value < count; value++)
            {
                if (value == position || partial.Take(position).Contains(value))
                    continue;

                partial[position] = value;
                foreach (var result in From(partial, position + 1))
                    yield return result;
            }
        }

        return From(new int[count], 0);
    }
}
