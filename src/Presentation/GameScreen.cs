using Godot;
using Valkaris.Core.Hosting;

namespace Valkaris.Presentation;

/// <summary>Owns the session and drives it every frame.</summary>
public partial class GameScreen : Control
{
    private IGameSession _session = null!;

    public void Init(IGameSession session) => _session = session;

    public override void _Process(double delta) => _session.Tick(delta);
}
