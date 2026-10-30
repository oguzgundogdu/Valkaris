using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class LocalGameSessionTests
{
    private const int LocalPlayer = 1;

    private static readonly GameData Data = TestData.Shipped;
    private static readonly BuildCommand BuildWoodcutter = new(1, BuildingType.Woodcutter);

    private readonly LocalGameSession _session = new(TestMatch.Create(), Data, LocalPlayer);
    private readonly List<GameEvent> _events = new();
    private readonly List<CommandRejection> _rejections = new();

    public LocalGameSessionTests()
    {
        _session.EventRaised += _events.Add;
        _session.CommandRejected += _rejections.Add;
    }

    private string Json() => SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(_session.State));

    [Fact]
    public void SendAppliesAsTheLocalPlayer()
    {
        _session.Send(BuildWoodcutter);

        Assert.Single(_session.State.Villages[1].BuildQueue);
        Assert.Empty(_rejections);
    }

    [Fact]
    public void CommandsCannotActAsAnotherPlayer()
    {
        var before = Json();

        _session.Send(new BuildCommand(2, BuildingType.Woodcutter));

        Assert.Equal(new[] { CommandRejection.NotOwner }, _rejections);
        Assert.Equal(before, Json());
    }

    [Fact]
    public void RejectionRaisesTheEventAndLeavesStateUnchanged()
    {
        _session.State.Villages[1].Resources = Resources.Zero;
        var before = Json();

        _session.Send(BuildWoodcutter);

        Assert.Equal(new[] { CommandRejection.InsufficientResources }, _rejections);
        Assert.Equal(before, Json());
    }

    [Fact]
    public void TickAdvancesTimeAndRaisesEventsInOrder()
    {
        _session.Send(new BuildCommand(1, BuildingType.Woodcutter));
        _session.Send(new BuildCommand(1, BuildingType.Quarry));

        _session.Tick(TestMatch.OneHour / 2);

        Assert.Equal(TestMatch.OneHour / 2, _session.State.ElapsedTime);
        Assert.Equal(
            new[] { BuildingType.Woodcutter, BuildingType.Quarry },
            _events.Cast<BuildingCompleted>().Select(e => e.Building));
    }

    [Fact]
    public void AfterMatchEndCommandsAreRejected()
    {
        _session.Tick(TestMatch.OneHour * 2);

        _session.Send(BuildWoodcutter);

        Assert.True(_session.State.IsMatchOver);
        Assert.Equal(new[] { CommandRejection.MatchOver }, _rejections);
    }

    [Fact]
    public void LocalSessionCanPause() => Assert.True(_session.CanPause);

    [Fact]
    public void PausedTickChangesNothing()
    {
        _session.Send(BuildWoodcutter);
        _session.SetPaused(true);
        var before = Json();

        _session.Tick(1000);

        Assert.True(_session.IsPaused);
        Assert.Equal(before, Json());
        Assert.Empty(_events);
    }

    [Fact]
    public void PausedCommandsAreRejected()
    {
        _session.SetPaused(true);
        var before = Json();

        _session.Send(BuildWoodcutter);

        Assert.Equal(new[] { CommandRejection.GamePaused }, _rejections);
        Assert.Equal(before, Json());
    }

    [Fact]
    public void ResumeContinuesExactlyWhereItStopped()
    {
        var uninterrupted = new LocalGameSession(TestMatch.Create(), Data, LocalPlayer);
        uninterrupted.Send(BuildWoodcutter);
        uninterrupted.Tick(100);

        _session.Send(BuildWoodcutter);
        _session.Tick(40);
        _session.SetPaused(true);
        _session.Tick(5000);
        _session.SetPaused(false);
        _session.Tick(60);

        Assert.Equal(
            SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(uninterrupted.State)),
            Json());
        Assert.Single(_events);
    }

    [Fact]
    public void PauseIsNotPartOfGameState()
    {
        var before = Json();

        _session.SetPaused(true);

        Assert.Equal(before, Json());
    }
}
