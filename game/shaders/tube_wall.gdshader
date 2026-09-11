// Scrolling neon wall pattern. UV.x = around the tube (0..1), UV.y = distance along it (world units).
shader_type spatial;
render_mode unshaded, cull_disabled;

// Distance travelled, wrapped by the caller to keep float precision (see Main.cs).
uniform float scroll = 0.0;
uniform float ring_spacing = 8.0;
uniform float stripe_count = 12.0;
// Hue cycles per world unit.
uniform float hue_speed = 0.02;
uniform float glow_strength = 3.0;

vec3 hue(float h) {
	return clamp(abs(mod(h * 6.0 + vec3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
}

void fragment() {
	float along = UV.y + scroll;
	float ring = smoothstep(0.85, 1.0, fract(along / ring_spacing));
	float stripe = smoothstep(0.92, 1.0, fract(UV.x * stripe_count));
	vec3 base = hue(fract(along * hue_speed + UV.x * 0.25)) * 0.12;
	// Values above 1.0 feed the environment glow (bloom).
	vec3 glow = hue(fract(along * hue_speed + 0.5)) * (ring * glow_strength + stripe * glow_strength * 0.5);
	ALBEDO = base + glow;
}
