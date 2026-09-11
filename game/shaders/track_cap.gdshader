// The wall where a split's branch tubes begin (or end): the chamber's cross-section with openings
// cut where the branches are. UV holds the section-space position (see TrackRenderer.cs).
shader_type spatial;
render_mode unshaded, cull_disabled;

// Openings: xy = center, zw = half width and half height of the branch section.
uniform vec4 holes[4];
uniform int hole_count = 0;
// Superellipse exponent of the branch section (2 = ellipse).
uniform float hole_exponent = 2.0;

// Theme (sRGB colors), set by TrackRenderer.cs.
uniform vec3 cap_color = vec3(0.12, 0.08, 0.04);
uniform vec3 rim_color = vec3(0.85, 0.68, 0.30);
uniform vec3 far_color = vec3(0.05, 0.04, 0.02);
uniform float fade_start = 30.0;
uniform float fade_end = 320.0;
uniform float glow = 0.0;

vec3 to_linear(vec3 c) {
	return pow(c, vec3(2.2));
}

void fragment() {
	float nearest = 1e9;
	for (int i = 0; i < hole_count; i++) {
		vec2 d = abs(UV - holes[i].xy) / holes[i].zw;
		float f = pow(pow(d.x, hole_exponent) + pow(d.y, hole_exponent), 1.0 / hole_exponent);
		if (f < 1.0) {
			discard;
		}
		nearest = min(nearest, f);
	}

	// A bright rim around each opening, fading into the dark wall.
	float rim = 1.0 - smoothstep(1.0, 1.12, nearest);
	vec3 col = mix(to_linear(cap_color), to_linear(rim_color) * (1.0 + 2.0 * glow), rim);
	col = mix(col, to_linear(far_color), smoothstep(fade_start, fade_end, length(VERTEX)));
	ALBEDO = col;
}
