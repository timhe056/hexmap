# CODEBUDDY.md This file provides guidance to CodeBuddy when working with code in this repository.

## Commands

```bash
dotnet build                  # Build the C# project
dotnet clean && dotnet build  # Clean rebuild
```

The project runs in Godot 4.5 Mono editor. Main scene: `src/hexmap/HexMapDemo.tscn` (not the default `node_2d.tscn`).

## Architecture

Godot 4.5 Mono C# hex map terrain system (Catlike Coding Unity tutorial Parts 1-20 port). Editable 3D hex terrain with rivers, roads, walls, water, features, A* pathfinding, fog of war.

### Data Pipeline (read this first — it's the key to the whole project)

```
Cell property change
  → HexCell setter calls Chunk.Refresh()
    → HexGridChunk._Process detects _needsRefresh flag (deferred batching)
      → HexMeshBuilder.BuildMeshes(cellIndices, grid, ...) triangulates 6 mesh types
        → ArrayMesh assigned to each MeshInstance3D child
```

**HexCell is NOT a Godot Node.** It's a plain C# class. Set a property, the chunk re-triangulates. Never call Refresh() manually.

**Part 3.1.0 packed values**: `HexCell` stores `TerrainTypeIndex`, `Elevation`, `WaterLevel`, `UrbanLevel`, `FarmLevel`, `PlantLevel`, and `SpecialIndex` in a single `HexValues` struct (32 bits). Save/load serialization lives in `HexValues`/`HexFlags`, not in `HexCell`.

**Fog-of-war bypasses mesh triangulation**: `TerrainTypeIndex` and `Visibility` write directly to `HexCellShaderData`'s byte array. The GPU reads it via `hex_cell_data` global uniform. Don't triangulate for visibility changes.

### Why This Architecture Exists

**Why chunks instead of one big mesh**: Each 5×5 chunk has independent meshes. Changing one cell only re-triangulates its chunk, not the whole map. Editor mode triangulates immediately; runtime defers to `_Process` to batch multiple edits.

**Why static HexMeshBuilder**: Triangulation is pure math — no Godot Node state needed. All mesh data lives in `MeshData` inner class (vertices/triangles/UV/color lists). This makes it testable and prevents accidental Node lifecycle bugs.

**Why deterministic hash grid for features**: `HexMetrics.SampleHashGrid(position)` returns same random value for same position. Features regenerate identically after map rebuild. Don't use `GD.Randf()` — it breaks on map reload.

### Key Constraints You Must Follow

- **HexGrid is `[Tool]`**: Changing any `[Export]` property in the inspector triggers `CreateMap()` which destroys and rebuilds ALL children. Be careful adding expensive operations to CreateMap.
- **Cell array is flat**: `_cells[X + Z * CellCountX]`, row-major. Chunk `(ci, cj)` maps to cells `(ci*5, cj*5)` through `(ci*5+4, cj*5+4)`.
- **Cube coordinates**: `HexCoordinates(X, Z)` with derived `Y = -X - Z`. Pointy-top. Neighbors: NE(0) clockwise to NW(5).
- **Elevation/WaterLevel are integers** representing step counts, not world units. Multiply by `HexMetrics.ElevationStep` for world-space Y.
- **River/Road/Wall use bit flags per direction**: Check with `cell.HasRiver(direction)`, not `cell.Rivers[direction]`.
- **Editor vs runtime behavior differs**: `Engine.IsEditorHint()` guards exist. Editor triangulates immediately; runtime defers. Don't assume one mode's timing in the other.
- **Maps serialize via BinaryWriter/BinaryReader** to `maps/*.map`, not Godot Resources. Header: version + dimensions, then per-cell binary fields.
