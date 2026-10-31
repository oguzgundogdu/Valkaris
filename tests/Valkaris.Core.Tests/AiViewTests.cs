using System.Reflection;
using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class AiViewTests
{
    private const int Self = 2;
    private const int Other = 1;

    private static readonly Type[] Forbidden =
    {
        typeof(GameState), typeof(Player), typeof(GameData), typeof(GameMap), typeof(MapConfig),
        typeof(IGameSession), typeof(LocalGameSession), typeof(Simulation), typeof(CommandHandler),
    };

    private static readonly string[] ForbiddenNames = { "IsAi", "LocalPlayerId", "Difficulty", "Difficulties" };

    private readonly GameState _state = TestMatch.Create(players: 3);
    private readonly List<ICommand> _sent = new();
    private readonly AiView _view;

    public AiViewTests()
    {
        _state.Players[Self].Difficulty = AiDifficulty.Hard;
        _view = new AiView(_state, TestData.Shipped, Self, command =>
        {
            _sent.Add(command);
            return new CommandHandler(_state, TestData.Shipped).Handle(Self, command);
        });
    }

    private static IEnumerable<Type> MemberTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var field in type.GetFields(All))
            yield return field.FieldType;
        foreach (var property in type.GetProperties(All))
            yield return property.PropertyType;
        foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
        {
            if (method is MethodInfo info)
                yield return info.ReturnType;
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }
    }

    private static IEnumerable<Type> Expand(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Expand).Prepend(type) : new[] { type };

    /// <summary>What a type could use to reach hidden information.</summary>
    private static List<string> Violations(IEnumerable<Type> types) =>
        types.SelectMany(type => MemberTypes(type).SelectMany(Expand)
                .Where(Forbidden.Contains)
                .Select(found => $"{type.Name} uses {found.Name}"))
            .Distinct()
            .ToList();

    private static List<string> PublicSurfaceViolations(Type type) =>
        type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => ForbiddenNames.Contains(m.Name))
            .Select(m => $"{type.Name}.{m.Name}")
            .ToList();

    private static IEnumerable<Type> DecisionTypes() =>
        typeof(AiPlayer).Assembly.GetTypes().Where(t => t != typeof(AiView) && t.DeclaringType != typeof(AiView));

    // The guard.

    [Fact]
    public void DecisionCodeCannotReachHiddenInformation() => Assert.Empty(Violations(DecisionTypes()));

    [Fact]
    public void TheViewDoesNotHandOutGameState()
    {
        var publicTypes = typeof(AiView)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .SelectMany(m => m switch
            {
                PropertyInfo p => new[] { p.PropertyType },
                MethodInfo i => new[] { i.ReturnType },
                _ => Array.Empty<Type>(),
            })
            .SelectMany(Expand);

        Assert.Empty(publicTypes.Where(Forbidden.Contains));
    }

    [Fact]
    public void TheViewDoesNotSayWhoIsHuman()
    {
        Assert.Empty(PublicSurfaceViolations(typeof(AiView)));
        Assert.Empty(PublicSurfaceViolations(typeof(KnownVillage)));
        Assert.Empty(PublicSurfaceViolations(typeof(IncomingAttack)));
    }

    private sealed class PlantedLeak
    {
        public GameState? State { get; set; }
        public void Peek(IReadOnlyList<Player> players) { }
    }

    private sealed class PlantedCheck
    {
        public bool IsAi => true;
        public int LocalPlayerId => 1;
    }

    [Fact]
    public void TheGuardDetectsAPlantedLeak()
    {
        var found = Violations(new[] { typeof(PlantedLeak) });

        Assert.Contains("PlantedLeak uses GameState", found);
        Assert.Contains("PlantedLeak uses Player", found);
    }

    [Fact]
    public void TheGuardDetectsAPlantedHumanCheck() =>
        Assert.Equal(2, PublicSurfaceViolations(typeof(PlantedCheck)).Count);

    [Fact]
    public void AiCodeNeverBranchesOnTheDifficulty()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Valkaris.sln")))
            root = root.Parent!;
        var offenders = Directory.GetFiles(Path.Combine(root.FullName, "src", "Valkaris.Ai"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) != nameof(AiView) + ".cs")
            .Where(f => File.ReadAllText(f).Contains("AiDifficulty"))
            .ToList();

        Assert.Empty(offenders);
    }

    // What it shows.

    [Fact]
    public void ProfileComesFromThePlayersDifficulty() =>
        Assert.Same(TestData.Shipped.Ai.Difficulties[AiDifficulty.Hard], _view.Profile);

    [Fact]
    public void OwnVillagesAreOnlyItsOwn() => Assert.All(_view.OwnVillages, v => Assert.Equal(Self, v.OwnerId));

    [Fact]
    public void KnownVillagesCoverTheMapWithPublicFacts()
    {
        Assert.Equal(_state.Villages.Count, _view.KnownVillages.Count);
        var other = _view.KnownVillages.Single(v => v.Id == 1);
        Assert.Equal(Other, other.OwnerId);
        Assert.True(other.IsCapital);
        Assert.False(other.IsBarbarian);

        var camp = _view.KnownVillages.First(v => v.IsBarbarian);
        Assert.False(camp.IsCapital);
        Assert.Contains(camp.BarbarianTier, TestData.Shipped.Map.Barbarians.Tiers.Select(t => t.Name));
    }

    [Fact]
    public void OnlyOwnMovementsAndAttacksOnItAreVisible()
    {
        var handler = new CommandHandler(_state, TestData.Shipped);
        var spear = new Dictionary<UnitType, int> { [UnitType.Spearman] = 1 };
        Assert.True(handler.Handle(Self, new SendTroopsCommand(2, 1, MovementType.Raid, spear)).Success);
        Assert.True(handler.Handle(Other, new SendTroopsCommand(1, 2, MovementType.Attack, spear)).Success);
        Assert.True(handler.Handle(Other, new SendTroopsCommand(1, 3, MovementType.Attack, spear)).Success);

        Assert.Single(_view.OwnMovements);
        var incoming = Assert.Single(_view.Incoming);
        Assert.Equal(Other, incoming.OwnerId);
        Assert.Equal(2, incoming.TargetVillageId);
        Assert.Equal(MovementType.Attack, incoming.Type);
    }

    [Fact]
    public void AnEnemyReturningToAVillageItNowOwnsIsNotAnAttack()
    {
        _state.AddMovement(new Movement(90, Other, 1, 2, MovementType.Return, new Dictionary<UnitType, int> { [UnitType.Spearman] = 1 }, Resources.Zero, 0, 100, null));

        Assert.Empty(_view.Incoming);
    }

    [Fact]
    public void OnlyItsOwnReportsAreVisible()
    {
        var none = new Dictionary<UnitType, int>();
        BattleReport Report(int id, int attacker, int defender) => new(id, 1, MovementType.Raid, attacker, defender, 1, 2, none, none, none, none, 0, 1, 0, true, Resources.Zero, null);
        _state.AddReport(Report(1, Self, 3));
        _state.AddReport(Report(2, 3, Self));
        _state.AddReport(Report(3, 1, 3));

        Assert.Equal(new[] { 1, 2 }, _view.ReportsSince(0).Select(r => r.Id));
        Assert.Equal(new[] { 2 }, _view.ReportsSince(1).Select(r => r.Id));
    }

    // How it acts.

    [Fact]
    public void CommandsGoThroughTheHandlerAndRejectionsAreCounted()
    {
        Assert.True(_view.Send(new BuildCommand(2, BuildingType.Farm)));
        Assert.False(_view.Send(new BuildCommand(1, BuildingType.Farm)));

        Assert.Equal(2, _sent.Count);
        Assert.Equal(1, _view.RejectedCommands);
        Assert.Single(_state.Villages[2].BuildQueue);
        Assert.Empty(_state.Villages[1].BuildQueue);
    }

    [Fact]
    public void PreviewsMatchTheSharedRules()
    {
        Assert.Equal(CommandRejection.NotOwner, _view.PreviewBuild(1, BuildingType.Farm).Rejection);
        Assert.Equal(CommandRejection.BarracksLevelTooLow, _view.PreviewTrain(2, UnitType.Lord, 1).Rejection);
        Assert.Equal(CommandRejection.NotOwner, _view.PreviewSend(new SendTroopsCommand(1, 2, MovementType.Raid, new Dictionary<UnitType, int>())).Rejection);
        Assert.Equal(CommandRejection.NotOwner, _view.PreviewFound(1, new TileCoord(0, 0)).Rejection);
        Assert.Equal(CommandRejection.NotOwner, _view.PreviewSendResources(new SendResourcesCommand(1, 2, 1, 0, 0)).Rejection);
    }

    private static ScoutReport Scouted(int id, int sender, int defender, bool succeeded)
    {
        var none = new Dictionary<UnitType, int>();
        return new ScoutReport(id, 0, sender, defender, sender, defender, none, succeeded, succeeded ? none : null, succeeded ? 0 : null, succeeded ? Resources.Zero : null);
    }

    [Fact]
    public void OnlyItsOwnScoutReportsAreVisible()
    {
        _state.AddScoutReport(Scouted(1, Self, Other, succeeded: true));
        _state.AddScoutReport(Scouted(2, Self, Other, succeeded: false));
        _state.AddScoutReport(Scouted(3, Other, Self, succeeded: true));
        _state.AddScoutReport(Scouted(4, Other, Self, succeeded: false));
        _state.AddScoutReport(Scouted(5, Other, 3, succeeded: true));

        Assert.Equal(new[] { 1, 2 }, _view.ScoutReportsSince(0).Select(r => r.Id));
        Assert.Equal(new[] { 2 }, _view.ScoutReportsSince(1).Select(r => r.Id));
    }

    [Fact]
    public void OnlyNoticesAddressedToItAreVisible()
    {
        _state.AddScoutNotice(new ScoutNotice(1, 0, Other, Self, Self, true, 3, 0));
        _state.AddScoutNotice(new ScoutNotice(2, 0, Self, Other, Other, true, 3, 0));
        _state.AddScoutNotice(new ScoutNotice(3, 0, Other, 3, 3, false, null, 0));

        Assert.Equal(new[] { 1 }, _view.ScoutNoticesSince(0).Select(n => n.Id));
    }

    [Fact]
    public void IncomingScoutsAreNotListedAsIncoming()
    {
        _state.Villages[Other].AddTroops(UnitType.Scout, 1);
        Assert.True(new CommandHandler(_state, TestData.Shipped).Handle(Other, new SendTroopsCommand(Other, Self, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = 1 })).Success);

        Assert.Empty(_view.Incoming);
    }
}
