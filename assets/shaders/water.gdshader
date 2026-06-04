shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_test_disabled;

uniform vec4 color : source_color = vec4(0.15, 0.4, 0.8, 0.6);

void fragment() {
	ALBEDO = color.rgb;
	ALPHA = color.a;
}
