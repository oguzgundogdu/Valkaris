shader_type canvas_item;

// A small fixed set of samples, since the Compatibility renderer may not offer screen mipmaps.
uniform sampler2D screen_texture : hint_screen_texture, filter_linear, repeat_disable;
uniform vec4 tint = vec4(0.09, 0.075, 0.06, 0.78);
uniform float radius = 4.0;

void fragment() {
	vec2 step_size = SCREEN_PIXEL_SIZE * radius;
	vec3 blurred = vec3(0.0);
	float weight = 0.0;
	for (int x = -2; x <= 2; x++) {
		for (int y = -2; y <= 2; y++) {
			float w = 1.0 / (1.0 + float(x * x + y * y));
			blurred += texture(screen_texture, SCREEN_UV + vec2(float(x), float(y)) * step_size).rgb * w;
			weight += w;
		}
	}
	COLOR = vec4(mix(blurred / weight, tint.rgb, tint.a), 1.0);
}
