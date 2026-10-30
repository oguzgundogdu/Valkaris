# Game Design Document – Valkaris

Valkaris is the name of both the game and the world in which the three nations live.

A simple, Travian-inspired real-time strategy game for 1–4 players, built in Godot 4 with C#.
Session-based: a match starts, runs for a configurable duration and ends. Nothing persists between matches.

---

## 1. Goals and Non-Goals

**Goals**
- Travian-like simplicity: resources, buildings, troops, attacks with travel time.
- Real-time (no turns), but low event frequency. No unit micro-management.
- Single player (vs. AI) and multiplayer (up to 4 players) using the same code path.
- Not visually flat: clean 2D/2.5D look with light animations and effects.

**Non-Goals (v1)**
- Persistent world, accounts, matchmaking servers.
- Unit pathfinding or on-map combat animation. Battles are resolved by calculation.
- Nation-specific units (planned for v2, see section 12).

---

## 2. Tech Stack

- **Engine:** Godot 4 (.NET / C#)
- **Target:** Desktop (macOS, Linux, Windows)
- **Networking:** Godot high-level multiplayer (ENet), host-authoritative
- **Data:** Balance values in JSON files under `data/`

---

## 3. Architecture

### 3.1 Layers
1. **Core (pure C#, no Godot node dependencies)**
   - `GameState`, `Player`, `Village`, `Movement`, `MapTile`
   - `CommandHandler`: validates and applies commands
   - `Simulation`: advances time (resource production, queues, movements, loyalty)
   - `CombatResolver`: resolves battles
   - Must be unit-testable without running Godot.
2. **Network**
   - Clients send commands to the host via RPC.
   - Host applies commands, runs the simulation and sends state updates / events to clients.
3. **Presentation (Godot scenes)**
   - Only reads state and sends commands. Never mutates `GameState` directly.
4. **AI**
   - AI players issue the same commands as human players through the same `CommandHandler`.

### 3.2 Rules
- Every state change happens through a command (`BuildCommand`, `TrainCommand`, `SendTroopsCommand`, `SetCapitalCommand`, ...).
- No balance values hard-coded in logic. Read them from `data/*.json`.
- Single player = local host + AI players. No separate code path.

### 3.3 Core Model (draft)

```csharp
public class GameState
{
    public GameSettings Settings;          // duration, map size, speed
    public float ElapsedTime;
    public Dictionary<int, Player> Players;
    public Dictionary<int, Village> Villages;
    public List<Movement> Movements;       // troops on the way
    public MapTile[,] Map;
}

public class Player
{
    public int Id;
    public string Name;
    public Nation Nation;
    public bool IsAi;
    public bool IsEliminated;
    public int CapitalVillageId;
}

public class Village
{
    public int Id;
    public int OwnerId;                    // -1 for barbarian villages
    public Vector2I Position;
    public Resources Resources;            // wood, stone, food
    public Dictionary<BuildingType, int> Buildings;   // type -> level
    public Dictionary<UnitType, int> Troops;
    public List<QueueItem> BuildQueue;
    public List<QueueItem> TrainingQueue;
    public float Loyalty = 100f;
}

public class Movement
{
    public int OwnerId;
    public int FromVillageId;
    public int ToVillageId;                // or target tile for settlers
    public MovementType Type;              // Attack, Raid, Reinforce, Settle, Return
    public Dictionary<UnitType, int> Troops;
    public Resources Loot;
    public float ArrivalTime;
}
```

---

## 4. Match Settings (chosen in lobby)

- Number of players: 1–4 (human or AI slots)
- Match duration: configurable (e.g. 20 / 30 / 45 / 60 minutes)
- Game speed multiplier (optional)
- Map size (small / medium)

---

## 5. Resources

- **Wood, Stone, Food**
- Produced continuously at a fixed rate per second, increased by building level.
- Capped by Warehouse capacity.

---

## 6. Buildings

| Building | Purpose |
|---|---|
| Main Building | Limits max level of other buildings; in the capital it limits max village count; speeds up loyalty regeneration |
| Woodcutter / Quarry / Farm | Resource production |
| Warehouse | Resource cap |
| Barracks | Trains troops |
| Wall | Defence bonus |

Each building has levels with increasing cost and build time (defined in JSON).

---

## 7. Units (v1: shared by all nations)

| Unit | Role |
|---|---|
| Spearman | Strong defence |
| Swordsman | Strong attack |
| Cavalry | Fast, high loot capacity |
| Catapult | Damages buildings (slow) |
| Settler | Founds a new village on an empty tile |
| Lord | Reduces loyalty of the target village |

Each unit has: attack, defence, speed, loot capacity, cost, training time.

---

## 8. Nations (v1: passive bonuses only)

Three invented nations. In v1 they share units and differ only by passive bonuses.

**Valdren – Defensive / Economy**
- Higher wall bonus, +25% warehouse capacity
- Slower units

**Karuk – Aggressive / Raiders**
- Cheaper and faster troop training, higher loot capacity
- Weaker wall bonus

**Solari – Speed / Flexibility**
- Faster units, shorter build times, better scouting
- More expensive units

---

## 9. Map and Views

- Small tile grid. Each player starts with one village (their capital).
- Neutral barbarian villages are scattered across the map and can be captured.
- Travel time = distance / slowest unit speed in the group.

**Two views:**
1. **World map (bird's-eye):** all villages, movements in progress, sending troops, selecting villages.
2. **Village view:** buildings, upgrades, troop training, queues.

Players must be able to manage their villages from both views.

---

## 10. Combat

- Resolved instantly on arrival with a single calculation:
  total attack power vs. total defence power (+ wall bonus, + nation bonus).
- Both sides lose troops proportionally to the outcome.
- If the attacker wins a raid/attack, it loots resources up to its loot capacity.
- Catapults that survive reduce a building's level.
- Survivors return home (Return movement).
- A battle report is generated for both sides.

---

## 11. Founding and Capturing Villages

### 11.1 Founding
- Send a Settler to an empty tile. On arrival, a new village is created.
- Max village count is limited by the capital's Main Building level.

### 11.2 Loyalty
- Every village has Loyalty (0–100).
- If an attack is won and at least one Lord survives, loyalty drops by 20–30.
- At 0 loyalty the village changes owner.
- Loyalty regenerates over time; a higher Main Building level means faster regeneration.

### 11.3 Capital Rule
- A capital cannot lose loyalty while its Main Building is above a threshold level.
  Attackers must first reduce the Main Building with Catapults, then send Lords.
- If a player's capital is captured, they choose one of their remaining villages as the new capital,
  with a penalty (e.g. Main Building reset).
- A player with no villages left is eliminated.

### 11.4 Win Conditions
- Last player standing wins.
- If time runs out: highest score wins (villages + population + captured villages).

---

## 12. Roadmap

**v1 (this scope)**
- Everything above.

**v2 (later)**
- Nation-specific units (e.g. Valdren: Shield Guard, Armoured Rider; Karuk: Axe Warrior, Wolf Rider; Solari: Archer, Wind Cavalry).
- Scouting unit and fog of war.
- Internet play without port forwarding (e.g. GodotSteam).

---

## 13. Milestones

1. **Core logic:** `GameState`, data loading, resources, building queue, simulation tick. Unit tests.
2. **Village view:** build/upgrade, single player, no networking.
3. **Troops and combat:** barracks, training, movements, combat resolver, battle reports.
4. **World map:** tile map, villages, movements, sending troops from the map.
5. **Villages:** settlers, loyalty, Lord, capital rule, elimination, win conditions.
6. **AI:** simple rule-based AI (build economy → train → attack weakest neighbour).
7. **Multiplayer:** lobby (host/join), command RPCs, state sync, up to 4 players.
8. **Nations:** passive bonuses.
9. **Polish:** visuals, animations, sounds, UI.
