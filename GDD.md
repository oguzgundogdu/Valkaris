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
4. **AI (`Valkaris.Ai`, a separate assembly with no access to Core's internals)**
   - AI players issue the same commands as human players through the same `CommandHandler`, and check the same previews first.
   - Decisions see the game only through `AiView`: what a player's own UI shows. It never exposes other players' garrisons, resources, buildings or loyalty, troop counts in incoming attacks, or whether a player is human. A guard test enforces this. The AI learns about targets from its own battle and scout reports, starting from beliefs in `data/ai.json`, which stand for experience and are separate from the real garrison data. It scouts its next target through the same command when its news is older than the profile allows, and allows for troops that were away when reading a player's scout report. Difficulty is data only: Easy keeps the smallest garrison share, builds no Wall, and has a limit on its army size, so it is the softest level to attack.
   - `LocalGameSession` runs agents (`IMatchAgent`) and stops at every event and decision, so what they do does not depend on the frame rate. Same seed, same match. AI memory lives on the host and is not in snapshots.
   - Difficulty (Easy, Normal, Hard) is one profile per level in `ai.json`, on one code path, with no rule or information advantage. A test confirms the AI code never branches on the level.
   - `tools/Valkaris.Sim` runs seeded AI-only matches headlessly and reports stats for balance work.

### 3.2 Rules
- Every player-initiated state change happens through a command (`BuildCommand`, `TrainCommand`, `SendTroopsCommand`, `SetCapitalCommand`, ...).
- Only three other things write to `GameState`, all inside Core: `Simulation` (time-based changes), `GameFactory` (initial state) and `SnapshotMapper` (a client restoring host state). Debug builds add a fourth, `DevScenario`, which is compiled out of `ExportRelease` (see section 4).
- Model classes have public getters, internal setters and read-only collection views, so code outside Core cannot mutate state.
- No balance values hard-coded in logic. Read them from `data/*.json`.
- Single player = local host + AI players. No separate code path.

### 3.3 Core Model (draft)

Shown as properties without accessors for brevity; see the rules in 3.2. Fields are added in the milestone that first uses them.

```csharp
public class GameState
{
    public GameSettings Settings;          // duration, map size, speed
    public double ElapsedTime;             // real seconds; not scaled by game speed
    public Dictionary<int, Player> Players;
    public Dictionary<int, Village> Villages;
    public List<Movement> Movements;       // troops on the way
    public List<BattleReport> Reports;     // immutable; both sides see the same report
    public List<ScoutReport> ScoutReports; // immutable; shares report ids; check VisibleTo before showing one
    public GameMap Map;                    // Width, Height, flat row-major list of MapTile (VillageId, Terrain)
    public ulong RngState;                 // seeded SplitMix64, saved so a restored snapshot continues identically
    public MatchOutcome Outcome;           // null until the match ends; IsMatchOver reads it
}

public class Player
{
    public int Id;
    public string Name;
    public Nation Nation;
    public bool IsAi;
    public AiDifficulty? Difficulty;       // null for humans
    public bool IsEliminated;
    public int? CapitalVillageId;          // null during a new capital choice and after elimination
    public double? CapitalChoiceDeadline;  // real time; set only during the choice
    public IReadOnlyCollection<int> CapturedVillageIds;   // each village counts once for the score
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
    public int? ToVillageId;               // null for settlers, who use TargetTile
    public TileCoord? TargetTile;
    public TileCoord? FromTile;            // a settler walking home starts from a tile
    public MovementType Type;              // Attack, Raid, Reinforce, Settle, Return, Transfer, CarrierReturn
    public int Carriers;                   // transfers only; Troops is empty and Loot holds the load
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

- Number of players: 2–4 (human or AI slots; limits in `data/game.json`). A single slot is not allowed, since a player with no opponents would win instantly. The start screen offers 1–3 AI opponents at one difficulty until the lobby exists.
- Match duration: configurable (e.g. 20 / 30 / 45 / 60 minutes)
- Game speed multiplier (optional). Scales production, build, training and travel speed and loyalty regeneration. It does not scale match duration: a 30-minute match lasts 30 real minutes.
- Map size (small / medium), set on the start screen until the lobby exists

**Pause (single player only):** a local session can pause with a top bar button or the `P` key, showing a dimmed overlay. Time and production stop, and commands are rejected until resumed. Pause belongs to the session, not `GameState`, so it never enters snapshots. Multiplayer sessions report `CanPause = false`.

**Dev overrides:** `--dev-speed=`, `--dev-duration=`, `--dev-idle-ai` and `--dev-scenario=` command-line arguments apply in debug builds only. Dev values never go in `data/`. `--dev-idle-ai` makes opponents stand still, for scripted tests and scenarios. `DevScenario` sets up late-game states for play and for the UI test (`villages`, `founding`, `endgame`, `laststand`, `empire`, `strayreturn`, `scouting`).

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
| Marketplace | Carriers that move resources to the player's other villages (section 7.1) |

Each building has levels with increasing cost and build time (defined in JSON).

- **Level curves:** value at level L = `base × growth^(L-1)`, rounded where it is a cost, time or capacity, with a `maxLevel` per building.
- **Build queue:** one builder per village. Items run one at a time in FIFO order, up to a queue length set in data.
- **Cost:** paid in full when queued. There is no cancel command in v1.
- **Main Building limit:** a data table gives the max level of other buildings for each Main Building level. Only the completed Main Building level counts, not a queued upgrade. The Main Building itself is not limited by the table.
- **Starting village:** Main Building, Woodcutter, Quarry, Farm and Warehouse at level 1; Barracks, Wall and Marketplace at level 0; starting resources from data.

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

**Training:** paid in full when queued, one batch at a time per village, FIFO, with a queue length from data and no cancel. A batch arrives all at once when it finishes. Barracks upgrades shorten training and unlock units; an upgrade finishing mid-batch does not change that batch. Settler and Lord are trained like other units (Barracks level 3 and 5).

**Movements:** troops leave the garrison at once and do not defend while away. Travel time uses Euclidean tile distance; the return trip uses the slowest survivor. Catapults and Lords cannot go on a Raid. Settlers cannot go on an Attack or Raid. Settlers and Lords can Reinforce.

### 7.1 Resource Transfers

- `SendResourcesCommand` moves whole amounts of resources between two villages of the same player. `SendResourcesPreview` is the single evaluation of the rules, shared by `CommandHandler` and the UI.
- **Carriers:** only the sending village needs a Marketplace. Its level sets the number of carriers (`carriersByMarketplaceLevel` in `buildings.json`). Capacity and speed are in `villages.json`, and game speed scales travel. A load uses as many carriers as it needs, rounded up, across all resource types.
- Carriers are not units. Free carriers are worked out from the Marketplace level and the movements under way, never below 0, so a lowered Marketplace simply has none free. A new owner's count ignores the old owner's trips.
- The resources leave at once. On arrival they are added up to the target's warehouse cap and the excess is lost. The UI warns before sending, using an estimate from the target's stock, its production until arrival and transfers arriving first. It does not block sending. Carriers then walk home empty and are free again only on arrival.
- **Lost villages:** if the target is lost, carriers bring the load home, capped by that warehouse. If the home village is lost, a delivery still completes, a load goes to the owner's nearest village (lowest id on a tie) and is deposited there, and those carriers then stop existing, as do empty carriers.
- Transfers cannot be attacked or looted, and only their owner sees them. An eliminated player's transfers are removed with their other movements.

### 7.2 Scouting

A minimal version, moved forward from v2 so a player can judge a target before attacking.

- **Scout** is a mission in the Send form, for any village that isn't yours, using Spearmen, Swordsmen or Cavalry. Catapults, Settlers and Lords can't scout. Scouts travel at the slowest unit's speed, so Cavalry are the fast choice and Spearmen the cheap one.
- **There is never a battle.** On arrival, scouts succeed if the garrison's head count is at most `scouts × troopsPerScout` (`data/combat.json`). An empty garrison always counts as a success. No random numbers are used.
- **Success:** all scouts return with no loot. The sender gets a report with the garrison, Wall level and resources at that moment, and the defender is told nothing. Troops away from the village aren't counted, so a report can understate a player's real strength.
- **Failure:** all scouts die, and the defender loses nothing. The sender gets a report with no intel, which still shows the garrison is larger than the limit. The defender gets a notice naming the sender and how many were caught.
- Incoming scouts are not shown to the target: no map marker, warning or AI alert. A successful scout would otherwise never go unnoticed.
- With a target picked, the Send form shows the sender's latest intel under the village's current owner, and with the Scout mission selected, the rule itself.
- A village the sender's side now holds merges the scouts in, and scouts whose home was lost go to the nearest village, as other movements do.

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
- "Better scouting" is deferred to v2 with the scouting unit and fog of war (section 12), even though v1 has a minimal Scout mission (7.2). A replacement bonus and all bonus values are set in `nations.json` in the Nations milestone.

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

**Hidden information:** other villages' garrisons and loyalty are not shown on the map. The map shows name, owner, tier, capital and distance; reports carry troop numbers and loyalty. Only your own movements and those heading for your villages are shown, except incoming scouts (7.2). Scouting (7.2) is how a player learns a target's strength before attacking.

**Two views:**
1. **World map (bird's-eye):** all villages, movements in progress, sending troops, selecting villages.
2. **Village view:** buildings, upgrades, troop training, queues.

Players must be able to manage their villages from both views.

**Village view layout:** a top bar (resources with cap and rate, hostile arrival warning, pause, match clock), the village ground with one clickable tile per building, and a side column of tabs: **Village** (selected building's details, build queue), **Troops** (garrison, training, training queue), **Send** (target, mission, troops, what is on the way) and **Reports** (with an unread count; a report opens in a modal). A disabled upgrade button always states why, including when a cost is above warehouse capacity and saving up alone can never pay it.

**World map layout:** the same top bar and side tabs, with the map in place of the village ground.
- Terrain is painted as a soft blended image with irregular shorelines, so it reads as a map rather than a grid. The grid only shows as a highlight on the hovered tile.
- Drag to pan, wheel or pinch to zoom towards the cursor, limited to the map's edges. It opens fitted to the whole map. Markers keep a constant size on screen.
- Clicking a village opens a popup: Send troops for other villages, Enter village and Reinforce for your own. Double-clicking your own village also enters it.
- When the current village has a Settler, clicking empty land opens a popup with the travel time and Send settler, or the reason it can't be settled. Otherwise, clicking empty land only closes the popup.
- Movements are dashed routes with moving markers, coloured for your own, hostile and returning troops, with mission and time left on hover.

**Switching views:** a `Village | World` toggle in the top bar and the `M` key, with a short fade. Both views stay alive, so the camera, selection and a half-filled Send form survive. The hostile arrival warning jumps to the map, centred on the village under attack.

**Sending troops:** the Send tab's target comes from the map, through a village's popup. The form has a "From" line, a target line and a button to the map.

**Sending resources:** the same Send tab. With one of your own villages as the target, Mission offers Reinforce and Send resources, and the form shows resource rows with carriers used and arrival time. It opens from the Send resources button in your other villages' map popup, or from the Marketplace in the Village tab. Transfers appear in the "On the way" list and on the map.

**Switching villages:** a strip of village chips above the main view, in both views, shows the capital's star and warns of threats. It is hidden with one village. `Q` and `E` cycle. Switching keeps the view, tab and Send target and clears the troop amounts; on the map the camera centres on the village, which has its own ring. If the current village is lost, the view moves to the capital or another village.

**Dialogs:** the new capital dialog lists your villages with a countdown and the penalty. It can be set aside: a countdown warning stays in the top bar and reopens it, and the timeout is unchanged. A player eliminated while others play on can keep watching, with only the map and Reports, or leave. The match end screen shows Victory, Defeat or Draw, the reason, and a ranked table of villages, population, captures and score.

**Scenes:** `Main` (screen switching), `StartScreen`, `Game` (`ResourceBar`, `VillageView` + `BuildingTile`, `WorldMapView` + `VillagePopup`, `VillageStrip`, `BuildingPanel`, `QueuePanel`, `TroopsPanel`, `SendPanel`, `ReportsPanel`, `ReportDialog`, `CapitalDialog`, `MatchEndScreen`, `Overlay`). `scenes/dev/` holds the UI test and must be excluded from exports; its script is left out of `ExportRelease` builds by `Valkaris.csproj`.

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
- **Hostile warning:** the top bar warns only of attacks and raids heading for your villages.
- **Movements around a change of owner:** troops returning to a lost village reroute to the owner's nearest village (lowest id on a tie) with their loot. Reinforcements heading for a lost village turn back the same way. An attack or raid reaching a village its owner now holds merges into the garrison with no battle.
- A battle report is generated for both sides. Both see the same full report, since v1 has no fog of war. Scout reports differ: a success is private to the sender (7.2). Read state is kept in the UI, not in game state.
- Reinforcements can only be sent to the player's own villages in v1. They merge into the village's garrison.

---

## 11. Founding and Capturing Villages

### 11.1 Founding
- Send one Settler to an empty land tile at least a data-defined distance from every existing village (`minFoundingDistance` in `map.json`). On arrival, a new village is created and the Settler is consumed.
- The tile and limit are checked again on arrival. If they no longer allow it, the Settler returns home. That includes while the player has no capital.
- Founded villages use their own starting values in data, and names from a list in data, numbered if the list runs out.
- Max village count is limited by the capital's Main Building level (`maxVillagesByCapitalMainLevel` in `buildings.json`). Owned villages plus Settlers in flight must be below the limit. The limit applies to founding only: captured villages, and villages already owned when the limit drops, are kept.

### 11.2 Loyalty
- Every village has Loyalty (0–100).
- If an attack is won and at least one Lord survives, loyalty drops once by a seeded 20–30, however many Lords survive. Lords are not consumed. Values are in `data/villages.json`.
- Catapults land first, so one wave can lower a capital's Main Building to the threshold and then its loyalty.
- At 0 loyalty the village changes owner. Its loyalty resets to a data-defined value, its queues are cleared with no refund and the surviving attackers return home. Stored resources and buildings stay.
- A captured barbarian village becomes a normal village: recovery stops, and it has no production until those buildings are built.
- Loyalty regenerates over time, on every village; a higher Main Building level means faster regeneration, and game speed scales it.

### 11.3 Capital Rule
- A capital cannot lose loyalty while its Main Building is above a threshold level.
  Attackers must first reduce the Main Building with Catapults, then send Lords.
- If a player's capital is captured, they choose one of their remaining villages as the new capital through `SetCapitalCommand`, which is valid only during this choice. The penalty lowers its Main Building to a data-defined level, and queued upgrades with it. If they do not choose within a data-defined time (real seconds, not scaled by game speed), the village with the highest Main Building level is picked automatically, then the lowest id. AI players use the same command at once, weighing the penalty against safety. The timeout also covers an AI or disconnected player that does not act.
- A player with no villages left is eliminated at once, and all their movements are removed, so an attack still travelling can never give them a village back.

### 11.4 Win Conditions
- Last player standing wins, and the match ends at that moment. Barbarians don't count.
- If time runs out: highest score among surviving players wins (villages + population + captured villages), with weights in `data/scoring.json`. A tie is a draw between the tied leaders.
- Population is the sum of a village's building levels, weighted by data.
- A village counts as a capture at most once per player, so trading a village back and forth can't farm score. Barbarian villages count, and captures are kept if the village is lost again.

---

## 12. Roadmap

**v1 (this scope)**
- Everything above.

**v2 (later)**
- Nation-specific units (e.g. Valdren: Shield Guard, Armoured Rider; Karuk: Axe Warrior, Wolf Rider; Solari: Archer, Wind Cavalry).
- A dedicated scouting unit and fog of war. v1 has only the Scout mission (7.2).
- Internet play without port forwarding (e.g. GodotSteam).

---

## 13. Milestones

1. **Core logic:** `GameState`, data loading, resources, building queue, simulation tick. Unit tests.
2. **Village view:** build/upgrade, single player, no networking.
3. **Troops and combat:** barracks, training, movements, combat resolver, battle reports.
4. **World map:** tile map, villages, movements, sending troops from the map.
5. **Villages:** settlers, loyalty, Lord, capital rule, elimination, win conditions.
5.5. **Resource transfers:** Marketplace, carriers, sending resources between your own villages.
6. **AI:** rule-based AI playing the full game, difficulty levels, and a headless simulation tool.
6.5. **Scouting and an easier Easy:** the Scout mission, AI scouting, and Easy as the softest target to attack.
7. **Multiplayer:** lobby (host/join), command RPCs, state sync, up to 4 players.
8. **Nations:** passive bonuses.
9. **Polish:** visuals, animations, sounds, UI. Includes filling the side bars on wide windows (e.g. water or fog beyond the map edge).
