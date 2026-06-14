shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_draw_always;

#include "hex_cell_data.gdshaderinc"
#include "hex_metrics.gdshaderinc"

uniform vec4 color : source_color = vec4(0.5, 0.35, 0.2, 1.0);
uniform sampler2D noise_texture : repeat_enable;
global uniform bool debug_always_visible = false;

varying vec3 world_pos;
varying vec2 v_visibility;

void vertex() {
	/* 轻微抬离地形表面，避免与地形共面产生 z-fighting */
	VERTEX.y += 0.01;

	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

	/* Part 20: 从 UV2 读取两个 cell index，用 COLOR 权重混合 visibility */
	float raw_vis = debug_always_visible ? 1.0 : (GetCellData(UV2.x).r * COLOR.r + GetCellData(UV2.y).r * COLOR.g);
	float raw_explored = debug_always_visible ? 1.0 : (GetCellData(UV2.x).g * COLOR.r + GetCellData(UV2.y).g * COLOR.g);
	v_visibility.x = mix(0.25, 1.0, raw_vis);
	v_visibility.y = raw_explored;
}

void fragment() {
	vec2 noise_uv = world_pos.xz * (3.0 * TILING_SCALE);
	vec4 n = texture(noise_texture, noise_uv);
	float blend = UV.x;
	blend *= n.r + 0.5;
	blend = smoothstep(0.4, 0.7, blend);
	float explored = v_visibility.y;
	vec3 c = color.rgb * (n.g * 0.75 + 0.25) * v_visibility.x;
	ALBEDO = c;
	ALPHA = blend * explored;
}
