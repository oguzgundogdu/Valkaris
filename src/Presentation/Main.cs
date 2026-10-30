using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>
/// Milestone 1 engine smoke test: proves Core and res://data work inside Godot.
/// Milestone 2 replaces this with the real boot flow.
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        var data = GameData.Load(new GodotDataSource());
        var settings = new GameSettings(
            data.Game.MatchDurationsMinutes[0] * 60,
            data.Game.SpeedMultipliers[0],
            MapSize.Small);
        var slots = new[]
        {
            new PlayerSlot("Human", Nation.Valdren, IsAi: false),
            new PlayerSlot("AI", Nation.Karuk, IsAi: true),
        };

        var state = GameFactory.CreateMatch(settings, slots, data);
        var handler = new CommandHandler(state, data);
        var simulation = new Simulation(state, data);
        var village = state.Villages[1];

        var result = handler.Handle(1, new BuildCommand(village.Id, BuildingType.Woodcutter));
        var finishTime = village.BuildQueue[0].FinishTime;
        GD.Print($"[smoke] build accepted={result.Success} finishTime={finishTime} wood={village.Resources.Wood}");

        var events = simulation.Advance(finishTime);
        GD.Print($"[smoke] events={events.Count} woodcutterLevel={village.BuildingLevel(BuildingType.Woodcutter)} wood={village.Resources.Wood} elapsed={state.ElapsedTime}");
    }
}
