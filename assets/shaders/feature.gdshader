shader_type spatial;
render_mode cull_disabled;

#include "hex_cell_data.gdshaderinc"

uniform sampler2D albedo_texture : hint_default_white;
uniform vec4 feature_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 background_color : source_color = vec4(0.40784314, 0.38039216, 0.35686275, 1.0);
global uniform bool debug_always_visible = false;

varying float v_visibility_factor;
varying float v_explored;

void vertex() {
    vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    /* Part 2.2: 用解析型 hex grid 数据反查 cell，替代 Grid Coordinates 纹理 */
    HexGridData hgd = GetHexGridData(world_pos.xz);
    vec4 cell_data = GetCellData(hgd.cellOffsetCoordinates);

    float raw_vis = debug_always_visible ? 1.0 : cell_data.r;
    float raw_explored = debug_always_visible ? 1.0 : cell_data.g;

    v_visibility_factor = mix(0.25, 1.0, raw_vis);
    v_explored = raw_explored;
}

void fragment() {
    vec4 c = texture(albedo_texture, UV) * feature_color;
    float explored = v_explored;

    ALBEDO = c.rgb * (v_visibility_factor * explored);
    METALLIC = 0.0;
    ROUGHNESS = 0.5;
    SPECULAR = 0.2 * explored;
    AO = explored;
    EMISSION = background_color.rgb * (1.0 - explored);
    ALPHA = c.a;
}
