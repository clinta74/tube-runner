// The wall where a split's branch tubes begin (or end), and the wall closing the end of a level or
// a core. A split's is a funnel sunk towards each opening, built with the holes already cut; a flat
// one has its holes cut here. UV holds the section-space position (see TrackView.cs).
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
// 1 on a funnel, whose slopes are shaded so the shape can be read; 0 on a flat wall.
uniform float funnel = 0.0;

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

	// A bright rim around each opening, fading into the dark wall. On a funnel only where the
	// surface has sunk almost to the throat: where an opening touches the chamber wall the surface
	// between the two is a strip running the whole depth of the throat, all of it within the rim's
	// band in section space, and lit along its length it was a streak across the wall.
	float rim = 1.0 - smoothstep(1.0, 1.12, nearest);
	rim *= mix(1.0, smoothstep(0.85, 0.97, UV2.x), funnel);
	vec3 col = mix(to_linear(cap_color), to_linear(rim_color) * (1.0 + 2.0 * glow), rim);

	// A funnel is lit from a geometric normal off the screen derivatives, the way a well's rim is:
	// the wall carries no normals and the shader is unshaded, and a sunk surface in one flat colour
	// is a flat wall to the eye. Face-on it is unchanged; the slopes into the throats go darker.
	// Held outside any branch, because derivatives in non-uniform control flow are undefined.
	vec3 gn = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	float facing = abs(dot(gn, normalize(-VERTEX)));
	col *= mix(1.0, 0.6 + 0.4 * facing, funnel);
	// And darker the deeper it is sunk, which is what says the throat goes in rather than out.
	col *= mix(1.0, 0.5, UV2.x * funnel);

	col = mix(col, to_linear(far_color), smoothstep(fade_start, fade_end, length(VERTEX)));
	ALBEDO = col;
}
