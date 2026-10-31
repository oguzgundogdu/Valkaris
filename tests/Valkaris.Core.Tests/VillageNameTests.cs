using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class VillageNameTests
{
    private const int Owner = 1;
    private const int Other = 2;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state = TestMatch.Create(players: 2);
    private readonly CommandHandler _handler;

    public VillageNameTests() => _handler = new CommandHandler(_state, Data);

    private CommandResult Rename(string name, int issuer = Owner, int village = 1) =>
        _handler.Handle(issuer, new RenameVillageCommand(village, name));

    [Theory]
    [InlineData("  Yeni   Köy  ", "Yeni Köy")]
    [InlineData("Abc", "Abc")]
    [InlineData("Twenty Characters Ok", "Twenty Characters Ok")]
    [InlineData("Işıklı Köy", "Işıklı Köy")]
    [InlineData("ğşıİçöü ĞŞÇÖÜ", "ğşıİçöü ĞŞÇÖÜ")]
    [InlineData("O'Brien-2", "O'Brien-2")]
    public void GoodNamesAreCleanedAndKept(string typed, string kept)
    {
        Assert.True(Rename(typed).Success);
        Assert.Equal(kept, _state.Villages[1].Name);
    }

    [Theory]
    [InlineData("", CommandRejection.VillageNameTooShort)]
    [InlineData("  a  ", CommandRejection.VillageNameTooShort)]
    [InlineData("Ab", CommandRejection.VillageNameTooShort)]
    [InlineData("Twenty One Characters", CommandRejection.VillageNameTooLong)]
    [InlineData("ui.start.language", CommandRejection.VillageNameInvalid)]
    [InlineData("Bad<b>Name", CommandRejection.VillageNameInvalid)]
    [InlineData("Город", CommandRejection.VillageNameInvalid)]
    [InlineData("123", CommandRejection.VillageNameInvalid)]
    [InlineData("---", CommandRejection.VillageNameInvalid)]
    [InlineData("A×B", CommandRejection.VillageNameInvalid)]
    public void BadNamesAreRejected(string typed, CommandRejection expected)
    {
        var before = _state.Villages[1].Name;

        Assert.Equal(expected, Rename(typed).Rejection);
        Assert.Equal(before, _state.Villages[1].Name);
    }

    [Fact]
    public void NoTwoVillagesShareAName()
    {
        Assert.True(Rename("Yeni Köy").Success);
        Assert.Equal(CommandRejection.VillageNameTaken, Rename("yeni köy", Other, 2).Rejection);
        Assert.Equal(CommandRejection.VillageNameTaken, Rename(" YENİ  KÖY ", Other, 2).Rejection);
    }

    [Theory]
    [InlineData("Işık", "ışık")]
    [InlineData("Işık", "isik")]
    [InlineData("Işık", "İşık")]
    [InlineData("Ilgın", "ilgin")]
    [InlineData("Kırık", "KIRIK")]
    public void AllFourTurkishFormsOfICountAsOneLetter(string first, string second)
    {
        Assert.Equal(VillageNameRules.Folded(first).Replace('ş', 's'), VillageNameRules.Folded(second).Replace('ş', 's'));
    }

    [Fact]
    public void CapitalIWithADotMatchesPlainI()
    {
        Assert.Equal(VillageNameRules.Folded("Işıl"), VillageNameRules.Folded("iŞil"));
        Assert.Equal(VillageNameRules.Folded("İzmir"), VillageNameRules.Folded("Izmir"));
    }

    [Theory]
    [InlineData("Ashfort")]
    [InlineData("ashFORT")]
    [InlineData("Eastmarch")]
    [InlineData("Eastmarch 2")]
    [InlineData("Karvik")]
    [InlineData("Old Ridge")]
    public void NamesOnTheDataListsAreReserved(string typed)
    {
        Assert.Equal(CommandRejection.VillageNameTaken, Rename(typed).Rejection);
    }

    [Fact]
    public void ANumberAloneDoesNotReserveAName()
    {
        Assert.True(Rename("Eastmarch Road").Success);
        Assert.True(Rename("Yeni Köy 2").Success);
    }

    [Fact]
    public void ANameItAlreadyHasIsTakenButItsCaseCanChange()
    {
        Assert.True(Rename("Yeni Köy").Success);

        Assert.Equal(CommandRejection.VillageNameTaken, Rename("Yeni Köy").Rejection);
        Assert.True(Rename("YENİ KÖY").Success);
        Assert.Equal("YENİ KÖY", _state.Villages[1].Name);
    }

    [Fact]
    public void AnotherPlayersVillageCannotBeRenamed()
    {
        Assert.Equal(CommandRejection.NotOwner, Rename("Yeni Köy", Other).Rejection);
        Assert.Equal(CommandRejection.VillageNotFound, Rename("Yeni Köy", Owner, 999).Rejection);
    }

    [Fact]
    public void ACapturedVillageKeepsItsNameUntilTheNewOwnerRenamesIt()
    {
        var name = _state.Villages[2].Name;
        _state.Villages[2].OwnerId = Owner;

        Assert.Equal(name, _state.Villages[2].Name);
        Assert.True(Rename("Yeni Köy", Owner, 2).Success);
        Assert.Equal(CommandRejection.NotOwner, Rename("Eski Köy", Other, 2).Rejection);
    }

    [Fact]
    public void RenamingIsRejectedWhilePausedAndAfterTheMatch()
    {
        var session = new LocalGameSession(_state, Data, Owner);
        session.SetPaused(true);
        Assert.Equal(CommandRejection.GamePaused, session.SendAs(Owner, new RenameVillageCommand(1, "Yeni Köy")).Rejection);
        session.SetPaused(false);

        _state.Outcome = new MatchOutcome(MatchEndReason.TimeUp, 0, new[] { Owner }, Array.Empty<PlayerScore>());
        Assert.Equal(CommandRejection.MatchOver, session.SendAs(Owner, new RenameVillageCommand(1, "Yeni Köy")).Rejection);
    }

    [Fact]
    public void TurkishNamesSurviveASnapshot()
    {
        Assert.True(Rename("ğşıİçöü ĞŞÇÖÜ").Success);

        Assert.Equal("ğşıİçöü ĞŞÇÖÜ", SnapshotAssert.RoundTrips(_state).Villages[1].Name);
    }

    private static PlayerSlot[] Slots(string? name) => new[]
    {
        new PlayerSlot("One", Nation.Valdren, IsAi: false, VillageName: name),
        new PlayerSlot("Two", Nation.Karuk, IsAi: true),
    };

    private static GameState Match(string? name) =>
        GameFactory.CreateMatch(new GameSettings(3600, 1, MapSize.Small, 7), Slots(name), Data);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptyStartNameChangesNothing(string? name)
    {
        var plain = GameFactory.CreateMatch(new GameSettings(3600, 1, MapSize.Small, 7), new[]
        {
            new PlayerSlot("One", Nation.Valdren, IsAi: false),
            new PlayerSlot("Two", Nation.Karuk, IsAi: true),
        }, Data);

        Assert.Equal(SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(plain)), SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(Match(name))));
    }

    [Fact]
    public void AStartNameChangesOnlyThatName()
    {
        var plain = Match(null);
        var named = Match("  Yeni Köy ");
        Assert.Equal("Yeni Köy", named.Villages[1].Name);

        named.Villages[1].Name = plain.Villages[1].Name;
        Assert.Equal(SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(plain)), SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(named)));
    }

    [Theory]
    [InlineData("Ab")]
    [InlineData("Ashfort")]
    public void ABadStartNameIsRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => Match(name));
    }

    [Fact]
    public void FoundingNeverReusesARenamedVillagesName()
    {
        Assert.Equal(CommandRejection.VillageNameTaken, Rename(Data.Map.FoundedVillageNames[0]).Rejection);
    }

    [Fact]
    public void ListedNamesThatBreakTheRulesAreCaught()
    {
        var source = new MutatedDataSource(GameData.MapFile, root => root["foundedVillageNames"]![0] = "No");

        Assert.Throws<GameDataException>(() => GameData.Load(source));
    }

    [Fact]
    public void TheAiNeverRenamesVillages()
    {
        var folder = Path.Combine(Catalogue.Root, "src", "Valkaris.Ai");
        var sources = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        Assert.All(sources, source => Assert.DoesNotContain("Rename", File.ReadAllText(source)));
    }
}
