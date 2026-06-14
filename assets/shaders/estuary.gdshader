shader_type spatial;
render_mode blend_mix, cull_disabled;

#include "water_functions.gdshaderinc"
#include "hex_cell_data.gdshaderinc"

uniform vec4 color : source_color = vec4(0.2509804, 0.4117647, 1.0, 0.2901961);
uniform sampler2D noise_texture : repeat_enable;
global uniform bool debug_always_visible = false;

varying vec3 world_pos;
varying vec2 v_visibility;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

	/* Part 20 (forward+): CUSTOM0.xyz 存 cell indices，COLOR.xyz 存混合权重 */
	vec3 cellIndices = CUSTOM0.xyz;
	vec3 cellWeights = COLOR.rgb;
	vec4 data0 = GetCellData(cellIndices.x);
	vec4 data1 = GetCellData(cellIndices.y);
	vec4 data2 = GetCellData(cellIndices.z);
	float raw_vis = debug_always_visible ? 1.0 : (
		data0.r * cellWeights.x +
		data1.r * cellWeights.y +
		data2.r * cellWeights.z
	);
	float raw_explored = debug_always_visible ? 1.0 : (
		data0.g * cellWeights.x +
		data1.g * cellWeights.y +
		data2.g * cellWeights.z
	);
	v_visibility.x = mix(0.25, 1.0, raw_vis);
	v_visibility.y = raw_explored;
}

void fragment() {
	// UV.y = shore coordinate (same as water_shore)
	float shore = UV.y;
	float foam = Foam(shore, world_pos.xz, noise_texture);
	float waves = Waves(world_pos.xz, noise_texture);
	waves *= 1.0 - shore;
	float shoreWater = max(foam, waves);

	// UV2 = river flow coordinate
	float river = River(UV2, noise_texture);

	// UV.x = blend factor: 0 = shore only, 1 = full river
	float water = mix(shoreWater, river, UV.x);
	vec3 c = clamp(color.rgb + water, 0.0, 1.0);
	float explored = v_visibility.y;
	ALBEDO = c * v_visibility.x;
	ALPHA = color.a * explored;
}
