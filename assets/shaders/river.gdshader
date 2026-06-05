shader_type spatial;
render_mode blend_mix, cull_disabled, depth_test_disabled;

#include "water_functions.gdshaderinc"

uniform vec4 color : source_color = vec4(0.022, 0.133, 0.604, 0.7);
uniform float flow_scale : hint_range(0.0, 2.0) = 0.4;
uniform float speed : hint_range(0.0, 2.0) = 0.25;
uniform sampler2D noise_texture : repeat_enable;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float river = River(UV, noise_texture) * flow_scale;
	vec3 c = clamp(color.rgb + river, 0.0, 1.0);
	ALBEDO = c;
	ALPHA = clamp(color.a + river, 0.0, 1.0);
}
