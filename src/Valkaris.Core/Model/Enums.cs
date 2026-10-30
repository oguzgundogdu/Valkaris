namespace Valkaris.Core.Model;

public enum BuildingType
{
    MainBuilding,
    Woodcutter,
    Quarry,
    Farm,
    Warehouse,
    Barracks,
    Wall,
    Marketplace,
}

public enum UnitType
{
    Spearman,
    Swordsman,
    Cavalry,
    Catapult,
    Settler,
    Lord,
}

public enum UnitClass
{
    Infantry,
    Cavalry,
}

public enum MovementType
{
    Attack,
    Raid,
    Reinforce,
    Settle,
    Return,
    Transfer,
    CarrierReturn,
    Scout,
}

public enum ResourceType
{
    Wood,
    Stone,
    Food,
}

public enum Nation
{
    Valdren,
    Karuk,
    Solari,
}

public enum Terrain
{
    Grass,
    Forest,
    Hills,
    Water,
}

public enum MapSize
{
    Small,
    Medium,
}

public enum MatchEndReason
{
    LastPlayerStanding,
    TimeUp,
}

public enum AiDifficulty
{
    Easy,
    Normal,
    Hard,
}
