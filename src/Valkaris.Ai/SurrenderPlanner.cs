using Valkaris.Core.Commands;
using Valkaris.Core.Model;

namespace Valkaris.Ai;

/// <summary>
/// Gives up when the position is hopeless: a capital lost or nothing else left, most villages gone, and far weaker than
/// what its reports have shown. It judges only from its own villages and reports, as a player would.
/// </summary>
public sealed class SurrenderPlanner
{
    private readonly AiView _view;
    private readonly AiMemory _memory;
    private readonly Forces _forces;
    private bool _lostCapital;
    private int _mostVillages;
    private double? _hopelessSince;

    public SurrenderPlanner(AiView view, AiMemory memory)
    {
        _view = view;
        _memory = memory;
        _forces = new Forces(view);
    }

    public void NoteCapitalLost() => _lostCapital = true;

    /// <summary>Returns true if it surrendered.</summary>
    public bool Plan()
    {
        var villages = _view.OwnVillages;
        _mostVillages = Math.Max(_mostVillages, villages.Count);

        if (!IsHopeless(villages))
        {
            _hopelessSince = null;
            return false;
        }

        _hopelessSince ??= _view.Now;
        if ((_view.Now - _hopelessSince.Value) * _view.GameSpeed < _view.Profile.SurrenderHoldSeconds)
            return false;

        return _view.Send(new SurrenderCommand());
    }

    private bool IsHopeless(IReadOnlyList<Village> villages)
    {
        // With only one village left, losing the capital is one battle away.
        if ((!_lostCapital && villages.Count > 1) || villages.Count > _mostVillages * _view.Profile.SurrenderVillageShare)
            return false;

        var own = villages.Sum(v => _forces.AttackOf(v.Troops)) + _view.OwnMovements.Sum(m => _forces.AttackOf(m.Troops));
        return own < _memory.StrongestEnemySeen * _view.Profile.SurrenderStrengthRatio;
    }
}
