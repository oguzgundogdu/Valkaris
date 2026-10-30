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
   - Talks only to `IGameSession` (state, data, `Send`, `Tick`, events). `Send` returns nothing and rejections arrive as an event, because a command sent over the network cannot answer immediately. Views read `State` fresh each frame, because a snapshot may replace it.
   - `LocalGameSession` runs everything in-process. Multiplayer adds another implementation of the same interface.
   - `BuildPreview` in Core is the single evaluation of the build rules, used by both `CommandHandler` and the UI, so rules are never duplicated in Presentation.
4. **AI**
   - AI players issue the same commands as human players through the same `CommandHandler`.

### 3.2 Rules
- Every player-initiated state change happens through a command (`BuildCommand`, `TrainCommand`, `SendTroopsCommand`, `SetCapitalCommand`, ...).
- Only three other things write to `GameState`, all inside Core: `Simulation` (time-based changes), `GameFactory` (initial state) and `SnapshotMapper` (a client restoring host state).
- Model classes have public getters, internal setters and read-only collection views, so code outside Core cannot mutate state.
- No balance values hard-coded in logic. Read them from `data/*.json`.
- Single player = local host + AI players. No separate code path.

### 3.3 Core Model (draft)

Shown as properties without accessors for brevity; see the rules in 3.2. Fields are added in the milestone that first uses them (`Loyalty` is not in the code yet).

```csharp
public class GameState
{
    public GameSettings Settings;          // duration, map size, speed
    public double ElapsedTime;             // real seconds; not scaled by game speed
    public Dictionary<int, Player> Players;
    public Dictionary<int, Village> Villages;
    public List<Movement> Movements;       // troops on the way
    public List<BattleReport> Reports;     // immutable; both sides see the same report
    public GameMap Map;                    // Width, Height, flat row-major list of MapTile (VillageId, Terrain)
    public ulong RngState;                 // seeded SplitMix64, saved so a restored snapshot continues identically
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
    public string Name;
    public int OwnerId;                    // -1 for barbarian villages
    public int? BarbarianTier;             // index into the tiers in data/map.json
    public double? NextTroopRegenTime;     // barbarians only: when the next troop returns
    public TileCoord Position;             // Core's own struct; Vector2I would need a Godot reference
    public Resources Resources;            // wood, stone, food (double)
    public Dictionary<BuildingType, int> Buildings;   // type -> level
    public Dictionary<UnitType, int> Troops;
    public List<BuildQueueItem> BuildQueue;           // building, target level, finish time
    public List<TrainingQueueItem> TrainingQueue;
    public double Loyalty = 100;
}

public record Movement                     // immutable
{
    public int Id;
    public int OwnerId;
    public int FromVillageId;
    public int ToVillageId;                // or target tile for settlers
    public MovementType Type;              // Attack, Raid, Reinforce, Settle (M5), Return
    public Dictionary<UnitType, int> Troops;
    public Resources Loot;
    public double DepartureTime;
    public double ArrivalTime;
    public BuildingType? CatapultTarget;
}
```

### 3.4 State Serialization

The model is never serialized directly. `SnapshotMapper` converts `GameState` to plain snapshot records (`GameStateSnapshot`, `VillageSnapshot`, ...) for `System.Text.Json`, and rebuilds a `GameState` from them. Immutable records (`Resources`, `TileCoord`, `GameSettings`, queue items, `Movement`, `BattleReport`) are used directly in snapshots. Every model class needs a matching `<Name>Snapshot`; a guard test fails if a class or property is not covered.

---

## 4. Match Settings (chosen in lobby)

- Number of players: 2–4 (human or AI slots; limits in `data/game.json`). A single slot is not allowed, since a player with no opponents would win instantly.
- Match duration: configurable (e.g. 20 / 30 / 45 / 60 minutes)
- Game speed multiplier (optional). Scales production, build, training and travel speed and loyalty regeneration. It does not scale match duration: a 30-minute match lasts 30 real minutes.
- Map size (small / medium), set on the start screen until the lobby exists

**Pause (single player only):** a local session can pause with a top bar button or the `P` key, showing a dimmed overlay. Time and production stop, and commands are rejected until resumed. Pause belongs to the session, not `GameState`, so it never enters snapshots. Multiplayer sessions report `CanPause = false`.

**Dev overrides:** `--dev-speed=`, `--dev-duration=` and `--dev-rival-attack=` command-line arguments apply in debug builds only. Dev values never go in `data/`. The rival attack exists because the rival is idle until the AI milestone; it goes through `LocalGameSession.SendAs`, the path the AI will use. `SendAs` is not on `IGameSession`, so the UI can never act as another player.

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

Each unit has: class (infantry or cavalry), attack, defence vs infantry, defence vs cavalry, speed, loot capacity, cost, training time, required Barracks level (`data/units.json`).

**Rock-paper-scissors:** Spearman is strong against Cavalry, Swordsman is the best infantry attacker, and Cavalry is fast and good against infantry-light defence such as Swordsmen. Catapults count as infantry. Food is the largest cost of every unit.

**Training:** paid in full when queued, one batch at a time per village, FIFO, with a queue length from data and no cancel. A batch arrives all at once when it finishes. Barracks upgrades shorten training and unlock units; an upgrade finishing mid-batch does not change that batch. Settler and Lord are added with the Villages milestone, since until then they would do nothing.

**Movements:** troops leave the garrison at once and do not defend while away. Travel time uses Euclidean tile distance; the return trip uses the slowest survivor. Catapults cannot go on a Raid.

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
- Travel time = distance / slowest unit speed in the group. Troops travel in straight lines across any terrain, since there is no pathfinding.

**Generation (`data/map.json`, all from the match seed):**
- Each tile has a terrain: Grass, Forest, Hills or Water, from seeded noise with thresholds in data. Terrain has no gameplay effect in v1 except that villages are never placed on water.
- Players start one per quadrant with a small capped jitter, on land.
- Barbarian villages are placed with a minimum spacing, which relaxes only if a seed cannot fit them all, so the count per map size is always met.
- Village names come from lists in data.

**Barbarian villages:**
- Neutral villages that can be attacked, raided and hit by catapults under the normal rules, and captured from the Villages milestone. They start at loyalty 100.
- Tiers in data (Camp, Stronghold) set the garrison, resources and buildings, chosen by seeded weight.
- They have no production buildings, training or attacks. Instead, resources and garrison slowly regenerate back up to their tier's starting values, at separate rates in data, so a looted camp is worth raiding again after a few minutes. Game speed scales both. Buildings damaged by catapults do not recover.
- Resources regenerate continuously. Troops return one at a time as scheduled simulation events, so results are exact at any tick rate; at the same moment, regeneration runs before an arriving attack.

**Hidden information:** other villages' garrisons are not shown on the map. The map shows name, owner, tier and distance; reports carry troop numbers. Only your own movements and those heading for your villages are shown.

**Two views:**
1. **World map (bird's-eye):** all villages, movements in progress, sending troops, selecting villages.
2. **Village view:** buildings, upgrades, troop training, queues.

Players must be able to manage their villages from both views.

**Village view layout:** a top bar (resources with cap and rate, hostile arrival warning, pause, match clock), the village ground with one clickable tile per building, and a side column of tabs: **Village** (selected building's details, build queue), **Troops** (garrison, training, training queue), **Send** (target, mission, troops, what is on the way) and **Reports** (with an unread count; a report opens in a modal). A disabled upgrade button always states why, including when a cost is above warehouse capacity and saving up alone can never pay it.

**World map layout:** the same top bar and side tabs, with the map in place of the village ground.
- Terrain is painted as a soft blended image with irregular shorelines, so it reads as a map rather than a grid. The grid only shows as a highlight on the hovered tile.
- Drag to pan, wheel or pinch to zoom towards the cursor, limited to the map's edges. It opens fitted to the whole map. Markers keep a constant size on screen.
- Clicking a village opens a popup: Send troops for other villages, Enter village for your own. Double-clicking your own village also enters it.
- Movements are dashed routes with moving markers, coloured for your own, hostile and returning troops, with mission and time left on hover.

**Switching views:** a `Village | World` toggle in the top bar and the `M` key, with a short fade. Both views stay alive, so the camera, selection and a half-filled Send form survive. The hostile arrival warning jumps to the map, centred on the village under attack.

**Sending troops:** the Send tab's target comes from the map, through a village's popup. The form has a target line and a button to the map.

**Scenes:** `Main` (screen switching), `StartScreen`, `Game` (`ResourceBar`, `VillageView` + `BuildingTile`, `WorldMapView` + `VillagePopup`, `BuildingPanel`, `QueuePanel`, `TroopsPanel`, `SendPanel`, `ReportsPanel`, `ReportDialog`, `Overlay`). `scenes/dev/` holds the UI test and must be excluded from exports; its script is left out of `ExportRelease` builds by `Valkaris.csproj`.

---

## 10. Combat

- Resolved instantly on arrival with a single calculation (`CombatResolver`, pure and deterministic), with parameters in `combat.json`:
  - `Ainf`, `Acav`: attack from infantry and from cavalry; `A = Ainf + Acav`.
  - `Dinf`, `Dcav`: the defenders' total defence against each class.
  - `D = (Dinf × Ainf / A + Dcav × Acav / A) × (1 + wall bonus per level × wall level)`, plus a nation bonus in the Nations milestone.
  - The attacker wins only if `A > D`; a tie goes to the defender.
- Defence is weighted by the attacker's composition, so the same total attack with a different mix can give a different result.
- Both sides lose troops according to `x = (loser power / winner power) ^ casualty exponent`:
  - **Attack:** the winner loses `x` of each unit; the loser loses everything.
  - **Raid:** the winner loses `x / (1 + x)`; the loser loses `1 / (1 + x)`.
- If the attacker wins a raid/attack, it loots resources up to its surviving troops' loot capacity. Loot is split in proportion to what is lootable, travels on the Return movement and is added on arrival, capped by the home warehouse.
- Defenders keep a protected share of each resource that cannot be looted: a percentage of warehouse capacity, defined in `combat.json` (start: 20%).
- **Raid:** lower casualties, loot only, no Catapult or Lord effects. **Attack:** full effects.
- Catapults that survive a won Attack reduce a building by `max(1, floor(survivors / catapults per level of damage))` levels, down to 0. The attacker selects the target building when sending; if none is selected, it is random among standing buildings (seeded). Targeting an unbuilt building does nothing.
- A damaged building's queued upgrades have their target levels lowered by the same amount. A reduced Main Building does not lower buildings already above its limit. A reduced Warehouse caps resources from the next production step.
- Survivors return home (Return movement).
- A battle report is generated for both sides. Both see the same full report, since v1 has no fog of war. Read state is kept in the UI, not in game state.
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
