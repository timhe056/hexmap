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
├── project.godot              # Godot 4.5, C#, Forward Plus renderer
├── test.csproj                # .NET 8.0/9.0
├── icon.svg
├── node_2d.tscn               # Empty default scene
│
├── src/
│   ├── hexmap/                # Hex grid terrain system
│   │   ├── HexGrid.cs        # Grid manager — creates cells, chunks, save/load, visibility
│   │   ├── HexCell.cs        # Pure data class — elevation, rivers, roads, walls, water, visibility
│   │   ├── HexCoordinates.cs # Cube coordinates (x,y,z where x+y+z=0), FromOffsetCoordinates, FromPosition
│   │   ├── HexMetrics.cs     # Geometry constants, corner positions, terrace lerp, bridge calc
│   │   ├── HexDirection.cs   # NE/E/SE/SW/W/NW enum + extensions (Opposite, Previous, Next)
│   │   ├── HexMeshBuilder.cs # Triangulation of terrain/rivers/roads/water/walls/features
│   │   ├── HexGridChunk.cs   # Per-chunk mesh + feature management
│   │   ├── HexFeatureManager.cs # Urban/farm/plant/special prefab + wall placement
│   │   ├── HexMapGenerator.cs # Procedural land generation (Parts 23–25)
│   │   ├── HexCellShaderData.cs # Global cell data texture (R=vis, G=explored, B=map data, A=terrain)
│   │   ├── HexUnit.cs        # Movable units with vision range
│   │   ├── HexCamera.cs      # Camera zoom/pan/rotate
│   │   ├── HexMapEditor.cs   # Runtime editor UI
│   │   ├── NewMapMenu.cs     # New map dialog
│   │   ├── SaveLoadMenu.cs   # Save/load dialog
│   │   ├── HexMapDemo.tscn   # 3D scene with HexGrid node, camera, directional light
│   │   └── HEXMAP_KNOWLEDGE.md  # Tutorial reference notes (older)
│   │
│   └── NewScript.cs          # Empty placeholder
│
└── addons/
    └── (texture_array_wizard removed in 3.0.0)
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
| `HexGrid` | `src/hexmap/HexGrid.cs` | Top-level manager. Creates cells/chunks, visibility, save/load. |
| `HexCell` | `src/hexmap/HexCell.cs` | Data per cell: coordinates, elevation, rivers, roads, walls, water, features, visibility. |
| `HexCoordinates` | `src/hexmap/HexCoordinates.cs` | Cube coordinates, distance, world→cube conversion. |
| `HexMetrics` | `src/hexmap/HexMetrics.cs` | Constants (radii, steps, noise scale) + geometry helpers. |
| `HexDirection` | `src/hexmap/HexDirection.cs` | `NE/E/SE/SW/W/NW` + `Opposite()`, `Previous()`, `Next()`. |
| `HexMeshBuilder` | `src/hexmap/HexMeshBuilder.cs` | Triangulation of terrain, rivers, roads, water, walls. |
| `HexGridChunk` | `src/hexmap/HexGridChunk.cs` | Per-chunk mesh instances + feature placement. |
| `HexFeatureManager` | `src/hexmap/HexFeatureManager.cs` | Feature and wall placement. |
| `HexMapGenerator` | `src/hexmap/HexMapGenerator.cs` | Procedural map generation (budget flood fill, regions, erosion, climate). |
| `HexCellShaderData` | `src/hexmap/HexCellShaderData.cs` | Global `ImageTexture` for cell shader data. |
| `HexFlags` | `src/hexmap/HexFlags.cs` | `[Flags]` enum packing roads/rivers/walls/exploration into one integer. |
| `HexUnit` | `src/hexmap/HexUnit.cs` | Movable unit with vision range. |
| `HexCamera` | `src/hexmap/HexCamera.cs` | Camera controller. |
| `HexMapEditor` | `src/hexmap/HexMapEditor.cs` | Runtime editor UI. |

## Progress

> **流程**：每完成一个新 part 且用户验收测试通过后，更新下方进度表并同步修改本节备注。

| Part | Status | Notes |
|------|--------|-------|
| 1–5  | ✅ Done | Grid, colors, elevation, larger maps |
| 6–8  | ✅ Done | Rivers, roads, water (water/waterShore/estuary) |
| 9–10 | ✅ Done | Features, walls |
| 11–14| ✅ Done | Save/load (header v2), map data |
| 15   | ✅ Done | Terrain types |
| 16   | ✅ Done | A* pathfinding (`HexCellPriorityQueue`) |
| 17–19| ✅ Done | Units, movement, Bezier travel animation |
| 20   | ✅ Done | Units with vision range |
| 21   | ✅ Done | Exploration (IsExplored, unexplored = black) |
| 22   | ✅ Done | Advanced Fog of War (elevation LoS, ViewElevation) |
| 23   | ✅ Done | Generating Land (budget-driven flood fill) |
| 24   | ✅ Done | Regions and Erosion |
| 25   | ✅ Done | Climate, Moisture, and Biomes |
| 26   | ✅ Done | Rivers and Temperature-based Biomes |
| 27   | ✅ Done | Wrapping (east-west map wrap with Column repositioning) |
| 3.0.0| ✅ Done | Architecture cleanup: index-based hot-reload state, centralized shader data, Texture2DArray atlas import |
| 3.1.0| ✅ Done | HexValues struct packs 7 cell values into 32 bits; save/load moved to HexValues/HexFlags; removed SetMapData |

**Unity reference projects** (local):
- Tutorial source: `E:\codes\game\unityProj\catlike-coding-hexmap` — original Built-in RP tutorial project.
- Modernized 2.0.0 source: `E:\codes\game\unityProj\hexmap\hex-map-project` — URP/Shader Graph version used for rendering alignment.
- 后续对照直接读这两个路径。

### Key Godot-specific decisions

- **Renderer**: Forward Plus (was GL Compatibility). Enables `CUSTOM0`–`CUSTOM3` vertex attributes.
- **Part 20 / 2.2.0 cell data**: Global `ImageTexture` (`hex_cell_data`) sampled by shaders via `RenderingServer.GlobalShaderParameterSet`. R=visibility, G=explored, B=water surface height (2.2.0), A=terrain type index.
- **Vertex attributes**: Terrain uses `UV+UV2+COLOR`; Water/WaterShore/Estuary use `CUSTOM0` (indices) + `COLOR` (weights) for 3-cell visibility blending.
- **LateUpdate equivalent**: `_Process` + manual `UpdateTexture` call.
- **`GetVisibleCells`**: Uses `_searchFrontier` priority queue with `_searchFrontierPhase` (shared with A* pathfinding).
- **Normal direction**: `HexMeshBuilder.MeshData.ToMesh()` flips normals after `SurfaceTool.GenerateNormals()` to compensate for Godot's right-handed coordinate system vs Unity's left-handed system.
- **Map generation**: Budget-driven chunk-based flood fill → region constraints → erosion smoothing → 40-cycle climate simulation → rivers → temperature×moisture biome matrix.
- **River generation**: `CreateRivers()` picks origins based on moisture × elevation weight (more moisture/elevation = more entries). `CreateRiver()` does downhill pathfinding with weighted direction selection (downhill=3×, avoid U-turns, prefer not reversing). Forms lakes at dead ends or via `ExtraLakeProbability` on flat terrain.
- **Temperature**: `DetermineTemperature()` = latitude-based baseline (adjusted by Hemisphere mode) + altitude cooling (higher = colder) + noise jitter. 4 bands (cold/temperate/warm/hot) × 4 moisture bands = 16-cell biome matrix.
- **Biome matrix** (`_biomes[16]`): cold→hot rows, dry→soaked columns. Sand(0), Grass(1), Mud(2), Stone(3), Snow(4). High elevation rock desert → Stone(3). Max elevation → Snow(4). Rivers boost plant level +1.
- **Highlight resource sharing**: To avoid RID exhaustion on large maps, `HexGridChunk` shares one `Shader`, one `PlaneMesh`, one base `ShaderMaterial`, and the outline texture across all per-cell highlight `MeshInstance3D`s. Each highlight material is created via `Duplicate()` so color can be set independently.
- **Terrain texture array (3.0.0)**: A single atlas `assets/textures/terrain/Terrain Texture Array.png` (2560×512, 5×1 slices) is imported as a `CompressedTexture2DArray` (`importer="2d_array_texture"`, `slices/horizontal=5`, `slices/vertical=1`). `assets/materials/terrain.tres` references the atlas directly, replacing the old wizard-generated `TerrainTextureArray.tres`.
- **Chunk cleanup**: `HexGrid.ClearChunks()` uses `Free()` instead of `QueueFree()` so old resources are released immediately when switching maps.
- **Part 27 Wrapping**: `HexGrid` creates `Node3D` column parents (`_columns[]`) and places chunks under them. `CenterMap(float x)` shifts columns by ±one map width so the world appears to loop east-west. `HexCoordinates` normalizes X to `[0, wrapSize)` and `DistanceTo` computes the shortest wrapped distance for A* / visibility. Water-shore and estuary triangulation offsets neighbor positions across the seam via `HexMeshBuilder.GetNeighborPosition`. Units reparent to the correct column while traveling and offset Bezier control points across seams. Save format upgraded to header v5 with a `wrapping` bool.
- **Shader tiling for wrap**: `assets/shaders/hex_metrics.gdshaderinc` defines `TILING_SCALE`; road/water shaders use it so noise patterns align when columns wrap. Cell data texture sampler uses `repeat_enable` for correct sampling at the east-west seam.
- **2.0.0 URP-aligned feature fog-of-war**: Ported Unity Hex Map Project 2.0.0's `Feature.hlsl` to `assets/shaders/feature.gdshader` + `assets/materials/feature.tres`. `HexFeatureManager.ApplyFeatureMaterial` recursively applies the shader to all `MeshInstance3D` children of urban/farm/plant/special features, wall towers, and bridges, preserving their original albedo color. Features now dim in unexplored cells and switch to the background emissive color, matching terrain behavior.
- **2.0.0 URP-aligned terrain tiling**: `terrain.gdshader` now uses `2.0 * TILING_SCALE` (≈0.01732) instead of the hard-coded `tiling = 0.02`, matching the URP Shader Graph value.
- **2.1.0 code cleanup / default map**: `HexFeatureCollection` is now a nested type inside `HexFeatureManager`. `HexGrid.CellCountX`/`CellCountZ` are no longer `[Export]` inspector fields; the initial map is hard-coded to `CreateMap(20, 15, false)` in `_Ready`, matching Unity 2.1.0's default non-wrapping startup map.
- **2.2.0 Cell Visuals Upgrade**: Repurposed cell data B channel from transition flag to water surface height; transitions now tracked by `bool[]` in `HexCellShaderData`. Added analytical hex grid data (`HexGridData`), submergence colorization, and brush cell highlighting in `terrain.gdshader`. Replaced texture-based `Grid.png` grid with an analytical grid derived from world position. `feature.gdshader` now uses the same analytical grid data. Added `cell_highlighting` global shader parameter (vec4) set by `HexMapEditor` from the active brush/hovered cell.
- **2.3.0 Leaner Cells**: Introduced `[Flags]` enum `HexFlags` to pack roads, incoming/outgoing rivers, walled, explored, and explorable states into a single integer, removing per-cell `bool[] Roads`, `HexCell[] Neighbors`, and separate river/wall/explored fields. `HexCell` now holds a back-reference to `HexGrid` and looks up neighbors on demand via `HexCoordinates.Step(HexDirection)`. Added `HexGrid.TryGetCell` and `HexCell.TryGetNeighbor` for safer neighbor access. Updated `HexMeshBuilder`, `HexMapGenerator`, `HexGrid.Search`, `HexGrid.GetVisibleCells`, and `HexGrid.EditCell` to use the new APIs. Save/load format remains compatible.
- **3.0.0 Index-based Refactor**: Converted all hot-reload-critical state from `HexCell` references to `int` cell indices: `HexGrid` path/drag state (`_currentPathFromIndex`, `_currentPathToIndex`, `_previousCellIndex`), `HexGridChunk` `_cellIndices`, `HexCellShaderData` `_transitioningCellIndices`, and `HexUnit` location/travel/path indices. Removed `HexCell.ShaderData`; all shader data access now goes through `HexGrid.ShaderData`. `HexCell.NextWithSamePriority` is marked `[field: NonSerialized]`. Modernized terrain texture array import to a single atlas PNG imported as `CompressedTexture2DArray`, removed `TerrainTextureArray.tres` and the `texture_array_wizard` plugin.
- **3.1.0 Packed Cell Values**: Introduced `HexValues` struct that packs `TerrainTypeIndex`, `Elevation`, `WaterLevel`, `UrbanLevel`, `FarmLevel`, `PlantLevel`, and `SpecialIndex` into one 32-bit integer (`TTTTTTTT SSSSSSSS PPFFUUWW WWWEEEEE`). `HexCell` no longer stores these as separate fields; properties read/write through `_values`. Save/load serialization moved into `HexValues.Save`/`Load` and `HexFlags.Save`/`Load` extension methods. Removed `HexCell.SetMapData` and `HexCellShaderData.SetMapData`. Made `RemoveIncomingRiver`, `RemoveOutgoingRiver`, and `GetElevationDifference` private; removed the `GetEdgeType(HexDirection)` overload. Feature/road/river/wall changes now only refresh their own chunk (no neighbor chunk refresh or unit validation). Elevation save encoding aligned with Unity: stored as `byte(elevation + 127)` for header >= 4.

## Notes

- `HexCellPriorityQueue.Change` 保留了防御性代码。
- `Steamworks.NET` 引用已从 `test.csproj` 移除。
- **RID exhaustion fix**: Large maps (4800 cells) previously crashed with `Element limit reached` because each cell created a new `Shader` + `ShaderMaterial` + `PlaneMesh`. Fixed by sharing the shader/mesh/texture and using `Free()` for chunk cleanup.
