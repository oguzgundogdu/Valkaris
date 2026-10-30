namespace Valkaris.Core.Model;

public sealed class Player
{
    internal Player(int id, string name, Nation nation, bool isAi)
    {
        Id = id;
        Name = name;
        Nation = nation;
        IsAi = isAi;
    }

    public int Id { get; }
    public string Name { get; }
    public Nation Nation { get; }
    public bool IsAi { get; }
    public bool IsEliminated { get; internal set; }
    public int CapitalVillageId { get; internal set; }
}
