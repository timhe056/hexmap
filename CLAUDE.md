# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A **Godot 4.5 C#** hex map terrain generation project, ported from Catlike Coding's Unity hex map tutorial series to Godot.

## Build

```bash
dotnet build
```

Run in Godot 4.5 Mono editor. The main scene is `node_2d.tscn` (empty). Open `src/hexmap/HexMapDemo.tscn` to view the hex grid.

## Project Structure

```
├── project.godot              # Godot 4.5, C#, GL Compatibility renderer
├── test.csproj                # .NET 8.0/9.0
├── icon.svg
├── node_2d.tscn               # Empty default scene
│
├── src/
│   ├── hexmap/                # Hex grid terrain system
│   │   ├── HexGrid.cs        # [Tool] Grid manager — creates cells, generates mesh via SurfaceTool
│   │   ├── HexCell.cs        # Pure data class — elevation, rivers, roads, walls, water
│   │   ├── HexCoordinates.cs # Cube coordinates (x,y,z where x+y+z=0), FromOffsetCoordinates, FromPosition
│   │   ├── HexMetrics.cs     # Geometry constants, corner positions, terrace lerp, bridge calc
│   │   ├── HexDirection.cs   # NE/E/SE/SW/W/NW enum + extensions (Opposite, Previous, Next)
│   │   ├── HexMapData.cs     # [GlobalClass] Resource for save/load
│   │   ├── HEXMAP_KNOWLEDGE.md  # Tutorial reference notes
│   │   └── HexMapDemo.tscn   # 3D scene with HexGrid node, camera, directional light
│   │
│   └── NewScript.cs          # Empty placeholder
│
└── addons/
    └── corenet_tester/        # ⚠️ Orphaned EditorPlugin (CoreNet was deleted)
        ├── plugin.cfg
        ├── corenet_tester.gd
        └── CoreNetTesterDock.tscn
```

## Architecture

### Hex Map System

- **Pure data cells**: `HexCell` is a plain C# class (not a Godot Node), stored in a flat array. The `HexGrid` Node3D manages all cells and generates a combined mesh via `SurfaceTool`.
- **Inspector-driven regeneration**: `HexGrid` is `[Tool]`-annotated. Changing any `[Export]` property triggers `Regenerate()` → recreate cells → rebuild mesh. Works in-editor.
- **Cube coordinates**: `HexCoordinates(X, Z)` with derived `Y = -X - Z`. Pointy-top layout. `FromOffsetCoordinates(x, z)` converts from even-r row-offset grid coordinates. `FromPosition(Vector3)` does world-to-hex lookup using cube rounding.
- **Edge types**: `Flat` (same elevation), `Slope` (diff=1), `Cliff` (diff>1) — used for terrace generation.
- **Serialization**: `HexMapData` is a `Resource` subclass (`[GlobalClass]`) — save/load with Godot's `ResourceSaver`/`ResourceLoader`.
- **Tutorial reference**: `HEXMAP_KNOWLEDGE.md` documents the original Unity code from Catlike Coding's tutorial (Parts 1-10+ covering grid, colors, elevation, rivers, roads, water, walls, features, fog of war).

### Key Classes

| Class | File | Role |
|-------|------|------|
| `HexGrid` | `src/hexmap/HexGrid.cs` | Top-level manager. Creates cells, triangulates mesh, save/load. `[Tool]`. |
| `HexCell` | `src/hexmap/HexCell.cs` | Data per cell: coordinates, elevation, rivers, roads, walls, water, features. |
| `HexCoordinates` | `src/hexmap/HexCoordinates.cs` | Cube coordinates, distance, world→cube conversion. |
| `HexMetrics` | `src/hexmap/HexMetrics.cs` | Constants (radii, steps, noise scale) + geometry helpers. |
| `HexDirection` | `src/hexmap/HexDirection.cs` | `NE/E/SE/SW/W/NW` + `Opposite()`, `Previous()`, `Next()`. |
| `HexMapData` | `src/hexmap/HexMapData.cs` | `Resource` for persistence. |

## Notes

- The `addons/corenet_tester/` editor plugin is orphaned (CoreNet library was removed). It can be deleted if not needed.
- `test.csproj` still references `Steamworks.NET` — was required by CoreNet. Can be removed if no other code uses it.
