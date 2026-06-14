shader_type spatial;
render_mode cull_disabled;

#include "hex_cell_data.gdshaderinc"
#include "hex_metrics.gdshaderinc"

uniform sampler2DArray terrain_textures : hint_default_black;
uniform bool grid_on = false;
uniform vec4 background_color : source_color = vec4(0.40784314, 0.38039216, 0.35686275, 1.0);
global uniform bool debug_always_visible = false;

varying vec3 v_world_pos;
varying vec4 v_terrain;
varying vec4 v_visibility;
varying vec4 v_color;

void vertex() {
    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    /* Part 20: cellIndices 从 UV/UV2 读取（避免 Godot 4 Custom0 兼容问题） */
    vec3 cellIndices;
    cellIndices.x = UV.x;
    cellIndices.y = UV.y;
    cellIndices.z = UV2.x;

    vec4 data0 = GetCellData(cellIndices.x);
    vec4 data1 = GetCellData(cellIndices.y);
    vec4 data2 = GetCellData(cellIndices.z);

    /* Part 2.2: Terrain.xyz = 三个 cell 的地形类型索引，w = 最大水面高度 */
    v_terrain.x = data0.a;
    v_terrain.y = data1.a;
    v_terrain.z = data2.a;
    v_terrain.w = max(max(data0.b, data1.b), data2.b) * 30.0;

    vec3 raw_vis = vec3(data0.r, data1.r, data2.r);
    vec3 raw_explored = vec3(data0.g, data1.g, data2.g);
    if (debug_always_visible) {
        raw_vis = vec3(1.0);
        raw_explored = vec3(1.0);
    }
    v_visibility.xyz = mix(vec3(0.25), vec3(1.0), raw_vis);
    v_visibility.w = raw_explored.x * COLOR.r + raw_explored.y * COLOR.g + raw_explored.z * COLOR.b;
    v_color = COLOR;
}

vec4 GetTerrainColor(int index) {
    vec3 uvw = vec3(v_world_pos.xz * (2.0 * TILING_SCALE), v_terrain[index]);
    float weight;
    if (index == 0) weight = v_color.r;
    else if (index == 1) weight = v_color.g;
    else weight = v_color.b;
    return texture(terrain_textures, uvw) * (weight * v_visibility[index]);
}

/* Part 2.2: 基于淹没深度的蓝色滤镜 */
vec3 ColorizeSubmergence(vec3 baseColor, float surfaceY, float waterY) {
    float submergence = waterY - max(surfaceY, 0.0);
    vec3 colorFilter = vec3(0.25, 0.25, 0.75);
    float filterRange = 1.0 / 15.0;
    return baseColor * mix(vec3(1.0), colorFilter, clamp(submergence * filterRange, 0.0, 1.0));
}

/* Part 2.2: 解析型网格线 */
vec3 ApplyGrid(vec3 baseColor, HexGridData h) {
    return baseColor * (0.2 + 0.8 * Smoothstep10(h, 0.965));
}

/* Part 2.2: 笔刷高亮 */
vec3 ApplyHighlight(vec3 baseColor, HexGridData h) {
    return clamp(SmoothstepRange(h, 0.68, 0.8) + baseColor.rgb, 0.0, 1.0);
}

void fragment() {
    vec4 c = GetTerrainColor(0) + GetTerrainColor(1) + GetTerrainColor(2);
    float explored = v_visibility.w;

    vec3 baseColor = ColorizeSubmergence(c.rgb, v_world_pos.y, v_terrain.w);

    HexGridData hgd = GetHexGridData(v_world_pos.xz);
    if (grid_on) {
        baseColor = ApplyGrid(baseColor, hgd);
    }
    if (IsHighlighted(hgd)) {
        baseColor = ApplyHighlight(baseColor, hgd);
    }

    ALBEDO = baseColor * explored;
    METALLIC = 0.0;
    ROUGHNESS = 0.8;
    SPECULAR = 0.2 * explored;
    AO = explored;
    EMISSION = background_color.rgb * (1.0 - explored);
}
