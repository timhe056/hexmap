shader_type spatial;
render_mode blend_mix, cull_disabled;

#include "water_functions.gdshaderinc"
#include "hex_cell_data.gdshaderinc"

uniform vec4 color : source_color = vec4(0.022, 0.133, 0.604, 0.7);
uniform float flow_scale : hint_range(0.0, 2.0) = 0.4;
uniform float speed : hint_range(0.0, 2.0) = 0.25;
uniform sampler2D noise_texture : repeat_enable;
global uniform bool debug_always_visible = false;

varying vec3 world_pos;
varying vec2 v_visibility;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

	/* Part 20: 从 UV2 读取两个 cell index，用 COLOR 权重混合 visibility */
	float raw_vis = debug_always_visible ? 1.0 : (GetCellData(UV2.x).r * COLOR.r + GetCellData(UV2.y).r * COLOR.g);
	float raw_explored = debug_always_visible ? 1.0 : (GetCellData(UV2.x).g * COLOR.r + GetCellData(UV2.y).g * COLOR.g);
	v_visibility.x = mix(0.25, 1.0, raw_vis);
	v_visibility.y = raw_explored;
}

void fragment() {
	float river = River(UV, noise_texture) * flow_scale;
	vec3 c = clamp(color.rgb + river, 0.0, 1.0);
	float explored = v_visibility.y;
	ALBEDO = c * v_visibility.x;
	ALPHA = clamp(color.a + river, 0.0, 1.0) * explored;
}
