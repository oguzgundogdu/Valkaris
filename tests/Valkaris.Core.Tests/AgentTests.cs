using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

public class AgentTests
{
    private sealed class ScriptedAgent : IMatchAgent
    {
        private readonly LocalGameSession _session;
        private readonly double _interval;

        public ScriptedAgent(LocalGameSession session, double interval)
        {
            _session = session;
            _interval = interval;
            NextDecisionTime = interval;
        }

        public double NextDecisionTime { get; private set; }
        public List<double> DecidedAt { get; } = new();
        public List<GameEvent> Events { get; } = new();
        public int WakeOn { get; init; } = -1;

        public void Decide()
        {
            DecidedAt.Add(_session.State.ElapsedTime);
            NextDecisionTime += _interval;
        }

        public void OnEvent(GameEvent gameEvent)
        {
            Events.Add(gameEvent);
            if (gameEvent is BuildingCompleted)
                NextDecisionTime = gameEvent.Time;
        }
    }

    private sealed class StuckAgent : IMatchAgent
    {
        public double NextDecisionTime => 1;
        public void Decide() { }
        public void OnEvent(GameEvent gameEvent) { }
    }

    private static LocalGameSession NewSession(double duration = TestMatch.OneHour) =>
        new(TestMatch.Create(duration: duration), TestData.Shipped, 1);

    [Theory]
    [InlineData(600)]
    [InlineData(1)]
    [InlineData(0.25)]
    public void DecisionsHappenAtExactTimesAtAnyStepSize(double step)
    {
        var session = NewSession();
        var agent = new ScriptedAgent(session, 15);
        session.AddAgent(agent);

        for (var elapsed = 0.0; elapsed < 600; elapsed += step)
            session.Tick(step);

        Assert.Equal(Enumerable.Range(1, 40).Select(i => i * 15.0), agent.DecidedAt);
    }

    [Fact]
    public void AgentsDecideInTimeOrderAndTiesGoToTheFirstAdded()
    {
        var session = NewSession();
        var order = new List<string>();
        var first = new OrderedAgent(order, "first", 10);
        var second = new OrderedAgent(order, "second", 5);
        session.AddAgent(first);
        session.AddAgent(second);

        session.Tick(10);

        Assert.Equal(new[] { "second", "first", "second" }, order);
    }

    private sealed class OrderedAgent : IMatchAgent
    {
        private readonly List<string> _order;
        private readonly string _name;
        private readonly double _interval;

        public OrderedAgent(List<string> order, string name, double interval)
        {
            _order = order;
            _name = name;
            _interval = interval;
            NextDecisionTime = interval;
        }

        public double NextDecisionTime { get; private set; }

        public void Decide()
        {
            _order.Add(_name);
            NextDecisionTime += _interval;
        }

        public void OnEvent(GameEvent gameEvent) { }
    }

    [Fact]
    public void PauseStopsAgents()
    {
        var session = NewSession();
        var agent = new ScriptedAgent(session, 15);
        session.AddAgent(agent);

        session.SetPaused(true);
        session.Tick(600);

        Assert.Empty(agent.DecidedAt);
        Assert.Equal(0, session.State.ElapsedTime);
    }

    [Fact]
    public void NoDecisionsAfterTheMatchEnds()
    {
        var session = NewSession(duration: 30);
        var agent = new ScriptedAgent(session, 15);
        session.AddAgent(agent);

        session.Tick(600);
        session.Tick(600);

        Assert.Equal(new[] { 15.0 }, agent.DecidedAt);
        Assert.True(session.State.IsMatchOver);
    }

    [Fact]
    public void AgentsAndTheUiBothHearEvents()
    {
        var session = NewSession();
        var agent = new ScriptedAgent(session, 1000);
        session.AddAgent(agent);
        var heard = new List<GameEvent>();
        session.EventRaised += heard.Add;
        session.Send(new BuildCommand(1, BuildingType.Farm));

        session.Tick(600);

        Assert.Single(heard.OfType<BuildingCompleted>());
        Assert.Single(agent.Events.OfType<BuildingCompleted>());
    }

    [Fact]
    public void AnEventCanWakeAnAgentEarly()
    {
        var session = NewSession();
        var agent = new ScriptedAgent(session, 1000);
        session.AddAgent(agent);
        session.Send(new BuildCommand(1, BuildingType.Farm));
        var finish = session.State.Villages[1].BuildQueue[0].FinishTime;

        session.Tick(600);

        Assert.Equal(finish, agent.DecidedAt[0]);
    }

    [Fact]
    public void AnAgentThatDoesNotMoveOnIsAnError()
    {
        var session = NewSession();
        session.AddAgent(new StuckAgent());

        Assert.Throws<InvalidOperationException>(() => session.Tick(10));
    }
}
