shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_test_disabled;

uniform vec4 color : source_color = vec4(0.15, 0.4, 0.8, 0.7);
uniform float speed : hint_range(0.0, 2.0) = 0.25;
uniform sampler2D noise_texture : repeat_enable;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 uv = UV;
	uv.x *= 0.0625;
	uv.y -= TIME * speed;
	float n = texture(noise_texture, uv).r;
	vec3 c = clamp(color.rgb + n * 0.3, 0.0, 1.0);
	ALBEDO = c;
	ALPHA = clamp(color.a + n * 0.2, 0.0, 1.0);
}
