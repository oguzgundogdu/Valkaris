namespace Valkaris.Core.Model;

public sealed class Player
{
    private readonly SortedSet<int> _capturedVillageIds = new();

    internal Player(int id, string name, Nation nation, bool isAi)
    {
        Id = id;
        Name = name;
        Nation = nation;
        IsAi = isAi;
        CapturedVillageIds = _capturedVillageIds;
    }

    public int Id { get; }
    public string Name { get; }
    public Nation Nation { get; }
    public bool IsAi { get; }

    /// <summary>True for anyone out of the match, whether beaten or by surrender.</summary>
    public bool IsEliminated { get; internal set; }

    public bool HasSurrendered { get; internal set; }

    /// <summary>Null for human players.</summary>
    public AiDifficulty? Difficulty { get; internal set; }

    /// <summary>Null while the player has lost their capital and has not chosen a new one.</summary>
    public int? CapitalVillageId { get; internal set; }

    /// <summary>When a new capital is picked automatically. Set only during the choice.</summary>
    public double? CapitalChoiceDeadline { get; internal set; }

    /// <summary>Each village counts once for the score, however often it changes hands.</summary>
    public IReadOnlyCollection<int> CapturedVillageIds { get; }

    internal void RecordCapture(int villageId) => _capturedVillageIds.Add(villageId);
}
