using Valkaris.Ai;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

/// <summary>A match with real AI players. Player ids listed as idle get no agent, so they stand still.</summary>
internal sealed class AiMatch
{
    public AiMatch(AiDifficulty[] difficulties, ulong seed = 1, double minutes = 60, double speed = 1, MapSize size = MapSize.Small, GameData? data = null, int[]? idle = null)
    {
        Data = data ?? TestData.Shipped;
        var slots = difficulties.Select((d, i) => new PlayerSlot($"AI {i + 1}", Nation.Valdren, true, d)).ToArray();
        State = GameFactory.CreateMatch(new GameSettings(minutes * 60, speed, size, seed), slots, Data);
        Session = new LocalGameSession(State, Data, 1);

        foreach (var player in State.Players.Keys.Except(idle ?? Array.Empty<int>()))
        {
            var id = player;
            var view = new AiView(State, Data, id, command => Session.SendAs(id, command));
            Views[id] = view;
            Session.AddAgent(new AiPlayer(view));
        }
    }

    public GameData Data { get; }
    public GameState State { get; }
    public LocalGameSession Session { get; }
    public Dictionary<int, AiView> Views { get; } = new();

    public void Run(double seconds, double step = 5)
    {
        for (var elapsed = 0.0; elapsed < seconds && !State.IsMatchOver; elapsed += step)
            Session.Tick(step);
    }

    public int RejectedCommands => Views.Values.Sum(v => v.RejectedCommands);
}
