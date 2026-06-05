shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_test_disabled;

uniform vec4 color : source_color = vec4(0.15, 0.4, 0.8, 0.6);
uniform sampler2D noise_texture : repeat_enable;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	/* 双方向噪声采样（按教程精确实现） */
	vec2 uv1 = world_pos.xz;
	uv1.y += TIME;
	vec4 noise1 = texture(noise_texture, uv1 * 0.025);

	vec2 uv2 = world_pos.xz;
	uv2.x += TIME;
	vec4 noise2 = texture(noise_texture, uv2 * 0.025);

	/* Blend wave：对角正弦波 + 噪声扰动 */
	float blendWave = sin(
		(world_pos.x + world_pos.z) * 0.1 +
		(noise1.y + noise2.z) + TIME
	);
	blendWave *= blendWave;

	/* 四个通道插值产生动态波纹 */
	float waves =
		mix(noise1.z, noise1.w, blendWave) +
		mix(noise2.x, noise2.y, blendWave);
	waves = smoothstep(0.75, 2.0, waves);

	vec3 c = clamp(color.rgb + waves, 0.0, 1.0);
	ALBEDO = c;
	ALPHA = color.a;
}
