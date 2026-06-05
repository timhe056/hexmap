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
	ALBEDO = c;
	ALPHA = clamp(color.a + water, 0.0, 1.0);
}
