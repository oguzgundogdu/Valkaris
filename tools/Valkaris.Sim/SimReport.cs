using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valkaris.Core.Model;

namespace Valkaris.Sim;

public sealed record Spread(double Median, double P10, double P90, int Count);

/// <summary>Means per seat. Values at the end leave out eliminated players, who have no villages to measure.</summary>
public sealed record DefenceStats(
    double Spent,
    double SpentPerVillage,
    double WallAtMid,
    double WallAtEnd,
    double HomeDefenceAtMid,
    double HomeDefenceAtEnd,
    double AttackWinShare);

/// <summary>Means per seat, and shares of all seats' scouting and attacks.</summary>
public sealed record ScoutingStats(
    double SentPerSeat,
    double SuccessShare,
    double TroopsLostPerSeat,
    double AttacksWithIntelShare);

public sealed record DifficultyStats(
    AiDifficulty Difficulty,
    int Seats,
    int Wins,
    int Draws,
    double WinShare,
    Spread? Villages,
    Spread? Score,
    Spread? FirstBarracks,
    double BarracksShare,
    Spread? FirstSettler,
    double SettlerShare,
    Spread? FirstLord,
    double LordShare,
    Spread? FirstFounded,
    double FoundedShare,
    Spread? FirstCapture,
    double CaptureShare,
    DefenceStats Defence,
    ScoutingStats Scouting,
    IReadOnlyDictionary<UnitType, double> UnitMix);

public sealed record AttackSpread(
    double MeanAttacksPerMatch,
    double MostAttackedShare,
    double EvenShare,
    double MatchesWithOneTargetOverTwiceEven)
{
    /// <summary>The most-attacked player's share, as a multiple of an even split.</summary>
    public double MultipleOfEven => EvenShare <= 0 ? 0 : MostAttackedShare / EvenShare;
}

/// <summary>The summary of a run. It holds only match results, so it is the same at any thread count.</summary>
public sealed record SimReport(
    int Matches,
    int Failed,
    Spread Length,
    double LastPlayerStandingShare,
    double UnfinishedShare,
    double EndedWithinTwoHoursShare,
    double DrawShare,
    IReadOnlyList<DifficultyStats> ByDifficulty,
    IReadOnlyList<double> WinShareBySeat,
    double DecidedShare,
    double AtCapShare,
    double ProductionLostShare,
    Spread Battles,
    double LootFromBarbariansShare,
    Spread LootFromBarbarians,
    Spread LootFromPlayers,
    double FoundedShareOfMatches,
    double CaptureShareOfMatches,
    double TransferShareOfMatches,
    Spread Founded,
    Spread BarbarianCaptures,
    Spread PlayerCaptures,
    Spread CapitalCaptures,
    Spread Eliminations,
    Spread Transfers,
    Spread ResourcesMoved,
    Spread ResourcesLostInTransfer,
    AttackSpread Attacks,
    double AttackerLostPerDestroyed,
    double DefenderLostPerDestroyed,
    double EliminationShareOfMatches,
    double SurrenderShareOfMatches,
    double PlayerOutShareOfMatches,
    Spread Surrenders,
    int Commands,
    int Rejected,
    double RejectedShare,
    IReadOnlyDictionary<string, int> Rejections,
    IReadOnlyList<MatchFailure> Failures)
{
    private const double TwoHours = 2 * 60 * 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SimReport Build(IReadOnlyList<MatchResult> results, IReadOnlyList<MatchFailure> failures)
    {
        Spread Of(Func<MatchResult, double> selector) => SpreadOf(results.Select(selector));
        double Share(Func<MatchResult, bool> test) => results.Count == 0 ? 0 : (double)results.Count(test) / results.Count;

        var seats = results.Count == 0 ? 0 : results.Max(r => r.Seats.Count);
        var decided = results.Where(r => !r.IsDraw && r.Seats.Any(s => s.Won)).ToList();
        var bySeat = Enumerable.Range(0, seats)
            .Select(i => decided.Count == 0 ? 0 : (double)decided.Count(r => i < r.Seats.Count && r.Seats[i].Won) / decided.Count)
            .ToList();

        var totalLootBarbarians = results.Sum(r => r.LootFromBarbarians);
        var totalLoot = totalLootBarbarians + results.Sum(r => r.LootFromPlayers);
        var rejections = results.SelectMany(r => r.Rejections)
            .GroupBy(r => r.Key)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Value));
        var commands = results.Sum(r => r.Commands);
        var rejected = rejections.Values.Sum();

        return new SimReport(
            results.Count,
            failures.Count,
            Of(r => r.LengthSeconds),
            Share(r => r.Reason == MatchEndReason.LastPlayerStanding),
            Share(r => r.Unfinished),
            Share(r => !r.Unfinished && r.LengthSeconds <= TwoHours),
            Share(r => r.IsDraw),
            Enum.GetValues<AiDifficulty>().Select(d => ForDifficulty(d, results)).Where(s => s.Seats > 0).ToList(),
            bySeat,
            results.Count == 0 ? 0 : (double)decided.Count / results.Count,
            Safe(results.Sum(r => r.SamplesAtCap), results.Sum(r => r.ResourceSamples)),
            Safe(results.Sum(r => r.ProductionLost), results.Sum(r => r.ProductionTotal)),
            Of(r => r.Battles),
            Safe(totalLootBarbarians, totalLoot),
            Of(r => r.LootFromBarbarians),
            Of(r => r.LootFromPlayers),
            Share(r => r.VillagesFounded > 0),
            Share(r => r.BarbarianCaptures + r.PlayerCaptures > 0),
            Share(r => r.Transfers > 0),
            Of(r => r.VillagesFounded),
            Of(r => r.BarbarianCaptures),
            Of(r => r.PlayerCaptures),
            Of(r => r.CapitalCaptures),
            Of(r => r.Eliminations),
            Of(r => r.Transfers),
            Of(r => r.ResourcesMoved),
            Of(r => r.ResourcesLostInTransfer),
            AttacksFrom(results),
            Safe(results.Sum(r => r.AttackerResourcesLost), results.Sum(r => r.DefenderResourcesLost)),
            Safe(results.Sum(r => r.DefenderResourcesLost), results.Sum(r => r.AttackerResourcesLost)),
            Share(r => r.Eliminations > 0),
            Share(r => r.Surrenders > 0),
            Share(r => r.Eliminations + r.Surrenders > 0),
            Of(r => r.Surrenders),
            commands,
            rejected,
            Safe(rejected, commands),
            rejections,
            failures);
    }

    private static DifficultyStats ForDifficulty(AiDifficulty difficulty, IReadOnlyList<MatchResult> results)
    {
        var seats = results.SelectMany(r => r.Seats.Where(s => s.Difficulty == difficulty).Select(s => (Match: r, Seat: s))).ToList();

        (Spread?, double) Time(Func<SeatResult, double?> selector)
        {
            var times = seats.Select(s => selector(s.Seat)).OfType<double>().ToList();
            return (times.Count == 0 ? null : SpreadOf(times), seats.Count == 0 ? 0 : (double)times.Count / seats.Count);
        }

        var wins = seats.Count(s => s.Seat.Won && !s.Match.IsDraw);
        var (barracks, barracksShare) = Time(s => s.FirstBarracks);
        var (settler, settlerShare) = Time(s => s.FirstSettler);
        var (lord, lordShare) = Time(s => s.FirstLord);
        var (founded, foundedShare) = Time(s => s.FirstFounded);
        var (capture, captureShare) = Time(s => s.FirstCapture);

        return new DifficultyStats(
            difficulty,
            seats.Count,
            wins,
            seats.Count(s => s.Seat.Won && s.Match.IsDraw),
            seats.Count == 0 ? 0 : (double)wins / seats.Count,
            seats.Count == 0 ? null : SpreadOf(seats.Select(s => (double)s.Seat.Villages)),
            seats.Count == 0 ? null : SpreadOf(seats.Select(s => s.Seat.Score)),
            barracks, barracksShare, settler, settlerShare, lord, lordShare, founded, foundedShare, capture, captureShare,
            DefenceOf(seats.Select(s => s.Seat).ToList()),
            ScoutingOf(seats.Select(s => s.Seat).ToList()),
            UnitMixOf(seats.Select(s => s.Seat).ToList()));
    }

    /// <summary>Each unit's share of the resources spent on military units.</summary>
    private static IReadOnlyDictionary<UnitType, double> UnitMixOf(IReadOnlyList<SeatResult> seats)
    {
        var spending = seats.Where(s => s.UnitSpending is not null).SelectMany(s => s.UnitSpending!).ToList();
        var total = spending.Sum(s => s.Value);
        return spending.GroupBy(s => s.Key).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => Safe(g.Sum(s => s.Value), total));
    }

    private static ScoutingStats ScoutingOf(IReadOnlyList<SeatResult> seats)
    {
        var measured = seats.Where(s => s.Scouting is not null).ToList();
        return new ScoutingStats(
            Safe(measured.Sum(s => s.Scouting!.Sent), measured.Count),
            Safe(measured.Sum(s => s.Scouting!.Succeeded), measured.Sum(s => s.Scouting!.Sent)),
            Safe(measured.Sum(s => s.Scouting!.TroopsLost), measured.Count),
            Safe(measured.Sum(s => s.Scouting!.AttacksWithIntel), measured.Sum(s => s.AttacksSent)));
    }

    private static DefenceStats DefenceOf(IReadOnlyList<SeatResult> seats)
    {
        static double Mean(IEnumerable<double> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? 0 : list.Average();
        }

        var measured = seats.Where(s => s.Defence is not null).ToList();
        var standing = measured.Where(s => !s.Eliminated).ToList();
        return new DefenceStats(
            Mean(measured.Select(s => s.Defence!.Spent)),
            Mean(measured.Select(s => s.Defence!.Spent / Math.Max(1, s.Villages))),
            Mean(measured.Select(s => s.Defence!.WallAtMid).OfType<double>()),
            Mean(standing.Select(s => s.Defence!.WallAtEnd)),
            Mean(measured.Select(s => s.Defence!.HomeDefenceAtMid).OfType<double>()),
            Mean(standing.Select(s => s.Defence!.HomeDefenceAtEnd)),
            Safe(measured.Sum(s => s.Defence!.AttacksWon), measured.Sum(s => s.AttacksSent)));
    }

    /// <summary>How attacks fell on players, to show ganging up: the most-attacked player's share of each match, against an even split.</summary>
    private static AttackSpread AttacksFrom(IReadOnlyList<MatchResult> results)
    {
        var shares = new List<double>();
        var overTwice = 0;
        var evens = new List<double>();
        foreach (var result in results)
        {
            var received = result.Seats.Select(s => s.AttacksReceived).ToList();
            var attacks = received.Sum();
            if (attacks == 0)
                continue;

            var share = (double)received.Max() / attacks;
            var even = 1.0 / result.Seats.Count;
            shares.Add(share);
            evens.Add(even);
            if (share > even * 2)
                overTwice++;
        }

        return new AttackSpread(
            results.Count == 0 ? 0 : results.Average(r => r.Seats.Sum(s => s.AttacksSent)),
            shares.Count == 0 ? 0 : shares.Average(),
            evens.Count == 0 ? 0 : evens.Average(),
            shares.Count == 0 ? 0 : (double)overTwice / shares.Count);
    }

    private static double Safe(double part, double whole) => whole <= 0 ? 0 : part / whole;

    public static Spread SpreadOf(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return new Spread(0, 0, 0, 0);

        double At(double share) => sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(share * sorted.Count))];
        return new Spread(At(0.5), At(0.1), At(0.9), sorted.Count);
    }

    /// <summary>A readable summary for the terminal.</summary>
    public string ToTable()
    {
        static string Min(double seconds) => $"{seconds / 60:0.0}m";
        static string Mins(Spread? s) => s is null ? "-" : $"{Min(s.Median)} ({Min(s.P10)}-{Min(s.P90)})";
        static string Num(Spread s) => $"{s.Median:0.#} ({s.P10:0.#}-{s.P90:0.#})";
        static string Pct(double share) => $"{share * 100:0.0}%";

        var text = new StringBuilder();
        void Line(string name, string value) => text.AppendLine($"  {name,-30} {value}");

        text.AppendLine($"Matches: {Matches}, failed: {Failed}");
        text.AppendLine("Match");
        Line("Length", Mins(Length));
        Line("Last player standing", Pct(LastPlayerStandingShare));
        Line("Ended within two hours", Pct(EndedWithinTwoHoursShare));
        Line("Unfinished at the tool's cap", Pct(UnfinishedShare));
        Line("Draws", Pct(DrawShare));
        Line("Win share by seat", string.Join("  ", WinShareBySeat.Select(Pct)));
        foreach (var d in ByDifficulty)
        {
            text.AppendLine($"{d.Difficulty} ({d.Seats} seats)");
            Line("Wins", $"{d.Wins} ({Pct(d.WinShare)}), draws {d.Draws}");
            Line("Villages at end", d.Villages is null ? "-" : Num(d.Villages));
            Line("Score", d.Score is null ? "-" : Num(d.Score));
            Line("First Barracks", $"{Mins(d.FirstBarracks)} in {Pct(d.BarracksShare)}");
            Line("First Settler", $"{Mins(d.FirstSettler)} in {Pct(d.SettlerShare)}");
            Line("First Lord", $"{Mins(d.FirstLord)} in {Pct(d.LordShare)}");
            Line("First founded village", $"{Mins(d.FirstFounded)} in {Pct(d.FoundedShare)}");
            Line("First capture", $"{Mins(d.FirstCapture)} in {Pct(d.CaptureShare)}");
            Line("Defence spent", $"{d.Defence.Spent:0}, {d.Defence.SpentPerVillage:0} per village");
            Line("Wall level (mid, end)", $"{d.Defence.WallAtMid:0.0}, {d.Defence.WallAtEnd:0.0}");
            Line("Home defence (mid, end)", $"{d.Defence.HomeDefenceAtMid:0}, {d.Defence.HomeDefenceAtEnd:0}");
            Line("Attacks on players won", Pct(d.Defence.AttackWinShare));
            Line("Scouting per seat", $"{d.Scouting.SentPerSeat:0.#} sent, {Pct(d.Scouting.SuccessShare)} succeed, {d.Scouting.TroopsLostPerSeat:0.#} troops lost");
            Line("Attacks with fresh intel", Pct(d.Scouting.AttacksWithIntelShare));
            Line("Unit mix", string.Join("  ", d.UnitMix.Select(u => $"{u.Key} {Pct(u.Value)}")));
        }
        text.AppendLine("Economy");
        Line("Resources at the cap", Pct(AtCapShare));
        Line("Production lost to the cap", Pct(ProductionLostShare));
        text.AppendLine("War");
        Line("Battles", Num(Battles));
        Line("Loot from barbarians", $"{Pct(LootFromBarbariansShare)} of all loot");
        Line("Barbarian loot per match", Num(LootFromBarbarians));
        Line("Player loot per match", Num(LootFromPlayers));
        Line("Barbarian captures", Num(BarbarianCaptures));
        Line("Player captures", Num(PlayerCaptures));
        Line("Capital captures", Num(CapitalCaptures));
        Line("Eliminations", Num(Eliminations));
        Line("Surrenders", Num(Surrenders));
        Line("Matches with an elimination", Pct(EliminationShareOfMatches));
        Line("Matches with a surrender", Pct(SurrenderShareOfMatches));
        Line("Matches with either", Pct(PlayerOutShareOfMatches));
        Line("Lost per resource destroyed", $"attackers {AttackerLostPerDestroyed:0.00}, defenders {DefenderLostPerDestroyed:0.00}");
        text.AppendLine("Expansion and transfers");
        Line("Matches with a founded village", Pct(FoundedShareOfMatches));
        Line("Matches with a capture", Pct(CaptureShareOfMatches));
        Line("Matches with a transfer", Pct(TransferShareOfMatches));
        Line("Villages founded", Num(Founded));
        Line("Transfers", Num(Transfers));
        Line("Resources moved", Num(ResourcesMoved));
        Line("Lost in transfer", Num(ResourcesLostInTransfer));
        text.AppendLine("Attack spread");
        Line("Attacks on players per match", $"{Attacks.MeanAttacksPerMatch:0.#}");
        Line("Most-attacked player's share", $"{Pct(Attacks.MostAttackedShare)} (even split {Pct(Attacks.EvenShare)})");
        Line("Multiple of an even share", $"{Attacks.MultipleOfEven:0.00}x");
        Line("One target over twice even", Pct(Attacks.MatchesWithOneTargetOverTwiceEven));
        text.AppendLine("Health");
        Line("Commands", $"{Commands}, rejected {Rejected} ({Pct(RejectedShare)})");
        foreach (var (reason, count) in Rejections)
            Line($"  {reason}", $"{count}");
        foreach (var failure in Failures.Take(5))
            text.AppendLine($"FAILED seed {failure.Seed}: {failure.Error}");
        return text.ToString();
    }
}
