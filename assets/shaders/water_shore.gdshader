shader_type spatial;
render_mode blend_mix, cull_disabled, depth_test_disabled;

#include "water_functions.gdshaderinc"

uniform vec4 color : source_color = vec4(0.022, 0.133, 0.604, 0.6);
uniform sampler2D noise_texture : repeat_enable;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// shore = UV.y: 0 = water side, 1 = land side
	float shore = UV.y;
	float foam = Foam(shore, world_pos.xz, noise_texture);
	float waves = Waves(world_pos.xz, noise_texture);
	waves *= 1.0 - shore;
	float water = max(foam, waves);
	vec3 c = clamp(color.rgb + water, 0.0, 1.0);
	ALBEDO = c;
	ALPHA = clamp(color.a + water, 0.0, 1.0);
}
