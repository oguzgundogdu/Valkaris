# Valkaris

A simple, Travian-inspired real-time strategy game for 1–4 players, built with Godot 4 and C#.
Build a village, train troops, raid barbarians, found and capture villages, and outlast your rivals before the clock runs out. Matches are session-based: nothing persists between them.

## Features

- Resources, buildings, troops and attacks with travel time, in real time with low event frequency
- A world map with terrain, barbarian camps that slowly recover, and villages you can found or capture
- Settlers, Lords and loyalty, with a protected capital and a timed choice of a new one if it falls
- Timed or Unlimited matches, with surrender for when a position is lost
- Scouting, so you can check a target's garrison, Wall and resources before you attack
- Resource transfers between your own villages through a Marketplace
- Rule-based AI opponents (Easy, Normal, Hard) that play by the same rules and see only what a player sees
- A headless AI-vs-AI simulation tool for balance work

Multiplayer and nation bonuses are planned. See [GDD.md](GDD.md) for the full design and the milestone list.

## Requirements

- [Godot 4.7 .NET](https://godotengine.org/download) (the Mono build)
- .NET 8 SDK

## Run

```sh
godot-mono --path .
```

All balance values assume the default 1× speed. Debug builds also accept `--dev-speed=`, `--dev-duration=`, `--dev-idle-ai` and `--dev-scenario=`, for example:

```sh
godot-mono --path . -- --dev-speed=5
```

## Test

```sh
dotnet test Valkaris.sln                                         # Core, AI and tool tests
godot-mono --headless --build-solutions --quit                   # build the game
godot-mono --headless --path . res://scenes/dev/UiTest.tscn      # UI test, prints [uitest] PASS
```

## Simulate AI matches

Runs seeded AI-only matches in parallel and reports match length, winners, growth times, overflow, loot sources, captures, transfers and attack spread:

```sh
dotnet run -c Release --project tools/Valkaris.Sim -- \
  --matches=200 --players=4 --difficulty=Normal --minutes=30 --out=report.json
```

Use `--minutes=unlimited` for Unlimited matches, which stop at `--cap-minutes` (240 by default) and count as unfinished if still going. `--efficiency=true` prints the combat cost table instead of running matches.

## Project layout

| Path | What it holds |
|---|---|
| `src/Valkaris.Core` | Game rules and state, with no Godot dependency |
| `src/Valkaris.Ai` | The AI, which can only act through commands |
| `src/Presentation` | Godot scenes' scripts |
| `scenes/`, `assets/` | Godot scenes and placeholder art |
| `data/` | All balance values, as JSON |
| `tools/Valkaris.Sim` | The headless simulation tool |
| `tests/` | Unit tests |

Balance values live only in `data/*.json`, and the design lives in [GDD.md](GDD.md), which is the source of truth.

## License

Valkaris is free software under the [GNU General Public License v3.0](LICENSE). You can use, change and share it for any purpose, but anything you distribute that is based on it must also be open source under the same license.
