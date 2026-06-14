/*
 * Part 8 / 27: Water Functions — 水面效果共享函数库
 * 被 water / water_shore / river / estuary 等 shader 共用
 */

#include "hex_metrics.gdshaderinc"

uniform float waves_scale : hint_range(0.1, 10.0, 0.1) = 3.0;

// Foam: 岸边泡沫效果
//   shore: UV.y，0=水面侧，1=陆地侧
//   worldXZ: 世界坐标 XZ
//   noiseTex: 噪声纹理
float Foam(float shore, vec2 worldXZ, sampler2D noiseTex) {
	shore = sqrt(shore) * 0.9;
	vec2 noiseUV = worldXZ + TIME * 0.25;
	vec4 noise = texture(noiseTex, noiseUV * (2.0 * TILING_SCALE));
	float distortion1 = noise.x * (1.0 - shore);
	float foam1 = sin((shore + distortion1) * 10.0 - TIME);
	foam1 *= foam1;
	float distortion2 = noise.y * (1.0 - shore);
	float foam2 = sin((shore + distortion2) * 10.0 + TIME + 2.0);
	foam2 *= foam2 * 0.7;
	return max(foam1, foam2) * shore;
}

// Waves: 开放水面波浪效果（逐行匹配 Unity Water.cginc）
//   worldXZ: 世界坐标 XZ 平面（vec2）
//   noiseTex: 噪声纹理
float Waves(vec2 worldXZ, sampler2D noiseTex) {
	vec2 uv1 = worldXZ;
	uv1.y += TIME;
	vec4 noise1 = texture(noiseTex, uv1 * (waves_scale * TILING_SCALE));

	vec2 uv2 = worldXZ;
	uv2.x += TIME;
	vec4 noise2 = texture(noiseTex, uv2 * (waves_scale * TILING_SCALE));

	float blendWave = sin(
		(worldXZ.x + worldXZ.y) * 0.1 +
		(noise1.y + noise2.z) + TIME
	);
	blendWave *= blendWave;

	float waves =
		mix(noise1.z, noise1.w, blendWave) +
		mix(noise2.x, noise2.y, blendWave);
	return smoothstep(0.75, 2.0, waves);
}

// River: 河流流动效果（逐行匹配 Unity Water.cginc）
//   riverUV: 河流专用 UV 坐标
//   noiseTex: 噪声纹理
float River(vec2 riverUV, sampler2D noiseTex) {
	vec2 uv = riverUV;
	uv.x = uv.x * 0.0625 + TIME * 0.005;
	uv.y -= TIME * 0.25;
	vec4 noise = texture(noiseTex, uv);

	vec2 uv2 = riverUV;
	uv2.x = uv2.x * 0.0625 - TIME * 0.0052;
	uv2.y -= TIME * 0.23;
	vec4 noise2 = texture(noiseTex, uv2);

	return noise.r * noise2.w;
}
