shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_test_disabled;

uniform vec4 color : source_color = vec4(0.5, 0.35, 0.2, 1.0);
uniform sampler2D noise_texture : repeat_enable;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 noise_uv = world_pos.xz * 0.025;
	vec4 n = texture(noise_texture, noise_uv);
	float blend = UV.x;
	blend *= n.r + 0.5;
	blend = smoothstep(0.4, 0.7, blend);
	vec3 c = color.rgb * (n.g * 0.75 + 0.25);
	ALBEDO = c;
	ALPHA = blend;
}
