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
- **Data:** Balance values in JSON files under `data/`. Export presets must include `data/*.json`, since Godot does not export plain JSON files by default.

---

## 3. Architecture

### 3.1 Layers
1. **Core (pure C#, separate assembly with no reference to Godot at all)**
   - `GameState`, `Player`, `Village`, `Movement`, `MapTile`
   - `CommandHandler`: validates and applies commands
   - `Simulation`: advances time (resource production, queues, movements, loyalty). `Advance` splits each step at every completion, so results do not depend on the host's tick rate.
   - `CombatResolver`: resolves battles
   - `SnapshotMapper`: converts `GameState` to and from its wire format (see 3.4)
   - Must be unit-testable without running Godot.
2. **Network**
   - Clients send commands to the host via RPC. The host takes the issuer from the RPC sender, never from the command payload.
   - Host applies commands, runs the simulation and sends periodic full state snapshots plus events to clients.
   - A disconnected player's slot is taken over by AI. If the host leaves, the match ends.
3. **Presentation (Godot scenes)**
   - Only reads state and sends commands. Never mutates `GameState` directly.
4. **AI**
   - AI players issue the same commands as human players through the same `CommandHandler`.

### 3.2 Rules
- Every player-initiated state change happens through a command (`BuildCommand`, `TrainCommand`, `SendTroopsCommand`, `SetCapitalCommand`, ...).
- Only three other things write to `GameState`, all inside Core: `Simulation` (time-based changes), `GameFactory` (initial state) and `SnapshotMapper` (a client restoring host state).
- Model classes have public getters, internal setters and read-only collection views, so code outside Core cannot mutate state.
- No balance values hard-coded in logic. Read them from `data/*.json`.
- Single player = local host + AI players. No separate code path.

### 3.3 Core Model (draft)

Shown as properties without accessors for brevity; see the rules in 3.2. Fields are added in the milestone that first uses them (`Movements`, `Troops`, `TrainingQueue` and `Loyalty` are not in the code yet).

```csharp
public class GameState
{
    public GameSettings Settings;          // duration, map size, speed
    public double ElapsedTime;             // real seconds; not scaled by game speed
    public Dictionary<int, Player> Players;
    public Dictionary<int, Village> Villages;
    public List<Movement> Movements;       // troops on the way
    public GameMap Map;                    // Width, Height, flat row-major list of MapTile
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
    public TileCoord Position;             // Core's own struct; Vector2I would need a Godot reference
    public Resources Resources;            // wood, stone, food (double)
    public Dictionary<BuildingType, int> Buildings;   // type -> level
    public Dictionary<UnitType, int> Troops;
    public List<BuildQueueItem> BuildQueue;           // building, target level, finish time
    public List<TrainingQueueItem> TrainingQueue;
    public double Loyalty = 100;
}

public class Movement
{
    public int OwnerId;
    public int FromVillageId;
    public int ToVillageId;                // or target tile for settlers
    public MovementType Type;              // Attack, Raid, Reinforce, Settle, Return
    public Dictionary<UnitType, int> Troops;
    public Resources Loot;
    public double ArrivalTime;
}
```

### 3.4 State Serialization

The model is never serialized directly. `SnapshotMapper` converts `GameState` to plain snapshot records (`GameStateSnapshot`, `VillageSnapshot`, ...) for `System.Text.Json`, and rebuilds a `GameState` from them. Immutable value records (`Resources`, `TileCoord`, `GameSettings`, `BuildQueueItem`) are used directly in snapshots. Every model class needs a matching `<Name>Snapshot`; a guard test fails if a class or property is not covered.

---

## 4. Match Settings (chosen in lobby)

- Number of players: 2–4 (human or AI slots; limits in `data/game.json`). A single slot is not allowed, since a player with no opponents would win instantly.
- Match duration: configurable (e.g. 20 / 30 / 45 / 60 minutes)
- Game speed multiplier (optional). Scales production, build, training and travel speed and loyalty regeneration. It does not scale match duration: a 30-minute match lasts 30 real minutes.
- Map size (small / medium)

---

## 5. Resources

- **Wood, Stone, Food**
- Produced continuously at a fixed rate per second, increased by building level.
- Capped per resource by Warehouse capacity. Production above the cap is lost.
- Food is mainly a troop-training resource: units cost significantly more food than wood or stone. There is no troop upkeep or starvation in v1.

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

- **Level curves:** value at level L = `base × growth^(L-1)`, rounded where it is a cost, time or capacity, with a `maxLevel` per building.
- **Build queue:** one builder per village. Items run one at a time in FIFO order, up to a queue length set in data.
- **Cost:** paid in full when queued. There is no cancel command in v1.
- **Main Building limit:** a data table gives the max level of other buildings for each Main Building level. Only the completed Main Building level counts, not a queued upgrade. The Main Building itself is not limited by the table.
- **Starting village:** Main Building, Woodcutter, Quarry, Farm and Warehouse at level 1; Barracks and Wall at level 0; starting resources from data.

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
- Faster units, shorter build times
- More expensive units
- "Better scouting" is deferred to v2 with the scouting unit and fog of war (section 12), since it has no effect in v1. A replacement bonus and all bonus values are set in `nations.json` in the Nations milestone.

---

## 9. Map and Views

- Small tile grid. Each player starts with one village (their capital).
- Neutral barbarian villages are scattered across the map and can be captured. They have a static garrison and resources from data, start at loyalty 100 and do not produce.
- Travel time = distance / slowest unit speed in the group.

**Two views:**
1. **World map (bird's-eye):** all villages, movements in progress, sending troops, selecting villages.
2. **Village view:** buildings, upgrades, troop training, queues.

Players must be able to manage their villages from both views.

---

## 10. Combat

- Resolved instantly on arrival with a single calculation:
  total attack power vs. total defence power (+ wall bonus, + nation bonus).
- Both sides lose troops proportionally to the outcome, using a Travian-style ratio formula with parameters in `combat.json`.
- If the attacker wins a raid/attack, it loots resources up to its loot capacity.
- **Raid:** lower casualties, loot only, no Catapult or Lord effects. **Attack:** full effects.
- Catapults that survive reduce a building's level. The attacker selects the target building when sending; if none is selected, it is random.
- Survivors return home (Return movement).
- A battle report is generated for both sides.
- Reinforcements can only be sent to the player's own villages in v1. They merge into the village's garrison.

---

## 11. Founding and Capturing Villages

### 11.1 Founding
- Send a Settler to an empty tile. On arrival, a new village is created and the Settler is consumed.
- If the tile was taken while the Settler was travelling, the Settler returns home.
- Founded villages use their own starting values in data.
- Max village count is limited by the capital's Main Building level. The limit applies to founding only: captured villages, and villages already owned when the limit drops, are kept.

### 11.2 Loyalty
- Every village has Loyalty (0–100).
- If an attack is won and at least one Lord survives, loyalty drops by 20–30.
- At 0 loyalty the village changes owner. Its loyalty resets to a data-defined value, its queues are cleared and the surviving attackers return home.
- Loyalty regenerates over time; a higher Main Building level means faster regeneration.

### 11.3 Capital Rule
- A capital cannot lose loyalty while its Main Building is above a threshold level.
  Attackers must first reduce the Main Building with Catapults, then send Lords.
- If a player's capital is captured, they choose one of their remaining villages as the new capital,
  with a penalty (e.g. Main Building reset). If they do not choose within a data-defined time, the village with the highest Main Building level is picked automatically. AI players choose immediately.
- A player with no villages left is eliminated.

### 11.4 Win Conditions
- Last player standing wins.
- If time runs out: highest score wins (villages + population + captured villages), with weights in JSON. A tie is a draw between the tied leaders.
- Population is the sum of a village's building levels, weighted by data.

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
