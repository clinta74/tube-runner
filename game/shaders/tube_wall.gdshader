// Smooth checkerboard wall in the level's theme colors. The track is divided into segments
// separated by a seam line; each segment picks its own palette pair, checker size, and twist.
// Each mesh chunk is one segment (see TrackRenderer.cs).
// UV.x = distance around the section / perimeter (continues past 0..1 onto open planes),
// UV.y = distance into the segment (world units).
// UV2.x = how far into a warp well a point lies; UV2.y = this wall's size against the section's own,
// which is 1 everywhere except the core of a ring.
shader_type spatial;
render_mode unshaded, cull_disabled;

// Segment index of this chunk; per-instance so every chunk shares one material.
instance uniform int segment_index = 0;
uniform float segment_length = 60.0;
// Extra checker edge blur, in cell widths.
uniform float softness = 0.06;

// Theme (sRGB colors), set from the level by ThemeView.cs.
uniform vec3 darks[4];
uniform int dark_count = 1;
uniform vec3 lights[4];
uniform int light_count = 1;
uniform vec3 seam_dark = vec3(0.12, 0.08, 0.04);
uniform vec3 seam_light = vec3(0.85, 0.68, 0.30);
uniform vec3 far_color = vec3(0.05, 0.04, 0.02);
uniform float fade_start = 30.0;
uniform float fade_end = 320.0;
// Extra brightness on light cells and seams; above 0 they bloom.
uniform float glow = 0.0;
// Draw the tube as a see-through wireframe instead of a solid wall.
uniform bool wire = false;

// Spacing of the wire grid: rings this many units apart along the track, and this many rails around.
const float WIRE_RINGS = 10.0;
const float WIRE_RAILS = 16.0;

// A line at every whole number of v, held to about a pixel wide however far away it is. Without the
// derivative the far end of a long tube turns into a solid sheet of aliasing.
float wire_line(float v) {
	float w = max(fwidth(v), 1e-5);
	float d = abs(fract(v + 0.5) - 0.5) / w;
	return 1.0 - smoothstep(0.5, 1.5, d);
}

// Checker cells around the tube; must be even so the pattern meets itself at the seam.
const float AROUND[4] = float[4](8.0, 10.0, 12.0, 16.0);
// Checker cell length along the tube; divisors of the default segment length.
const float ALONG[4] = float[4](3.0, 4.0, 5.0, 6.0);
// Twist in cells per world unit.
const float TWIST[4] = float[4](0.0, 0.0, 0.06, -0.06);

float hash(float n, float salt) {
	return fract(sin(n * 127.1 + salt * 311.7) * 43758.5453);
}

int pick(float seg, float salt, int count) {
	return int(floor(hash(seg, salt) * float(count)));
}

vec3 to_linear(vec3 c) {
	return pow(c, vec3(2.2));
}

// Box-filtered checker: 0 or 1 per cell, anti-aliased, with `soft` cells of extra edge blur.
float checker(vec2 p, float soft) {
	vec2 w = fwidth(p) + soft;
	vec2 i = 2.0 * (abs(fract((p - 0.5 * w) * 0.5) - 0.5) - abs(fract((p + 0.5 * w) * 0.5) - 0.5)) / w;
	return 0.5 - 0.5 * i.x * i.y;
}

void fragment() {
	float seg = float(segment_index);
	float local = UV.y;

	// Wireframe: keep the lines and throw the wall away, so the track ahead can be seen through it -
	// splits diverging, a well cut into the far side, the shape of a bend long before it arrives.
	// Discard rather than blending, so there is no transparency to sort and a near line still hides
	// what is behind it. `wire` is a uniform, so this branch is uniform and the derivatives inside it
	// are well defined.
	if (wire) {
		float rails = max(4.0, floor(WIRE_RAILS * max(UV2.y, 0.04)));
		float mask = max(wire_line(local / WIRE_RINGS), wire_line(UV.x * rails));
		if (mask < 0.04) discard;

		vec3 lit = to_linear(seam_light) * (1.0 + glow);
		// A well's rim burns brighter, which is all the marking it needs once the wall is gone.
		float rim = (UV2.x - 0.06) / 0.05;
		lit *= 1.0 + 2.5 * exp(-rim * rim) * step(0.001, UV2.x);

		// Distance dims the lines rather than hiding them: seeing a long way is the whole point of
		// this style, so the fade only takes them down to a quarter and reaches much further out.
		float dim = mix(1.0, 0.25, smoothstep(fade_start, fade_end * 2.0, length(VERTEX)));
		ALBEDO = lit * mask * dim;
	} else {

	// Cells are counted around the section, so a smaller wall has to take fewer of them or they
	// come out finer than a pixel and the box filter averages them into a flat grey slab. Kept even,
	// so the pattern still meets itself where the wall closes.
	float around = max(4.0, 2.0 * floor(float(AROUND[pick(seg, 1.0, 4)]) * max(UV2.y, 0.04) * 0.5 + 0.5));
	float x = UV.x * around + local * TWIST[pick(seg, 2.0, 4)];
	float y = local / ALONG[pick(seg, 3.0, 4)];
	float t = checker(vec2(x, y), softness);
	vec3 dark = to_linear(darks[pick(seg, 4.0, dark_count)]);
	vec3 light = to_linear(lights[pick(seg, 5.0, light_count)]) * (1.0 + glow);
	vec3 col = mix(dark, light, t);

	// Seam at each segment boundary: a groove with a thin bright core.
	float d = min(local, segment_length - local);
	float aa = fwidth(local);
	col = mix(col, to_linear(seam_dark), 1.0 - smoothstep(0.35, 0.7 + aa, d));
	col = mix(col, to_linear(seam_light) * (1.0 + 2.0 * glow), 1.0 - smoothstep(0.08, 0.18 + aa, d));

	// Fade with distance for depth.
	col = mix(col, to_linear(far_color), smoothstep(fade_start, fade_end, length(VERTEX)));

	// Warp wells. The wall is drawn sinking into them, and UV2.x says how far in a point lies: 0 at
	// the rim, 1 at the throat. After the distance fade, so the dark never washes back out.
	float well = UV2.x;

	// The round-over where the wall turns into the hole. The wall carries no normals and the shader
	// is unshaded, so the curve is lit from a geometric normal taken off the screen derivatives.
	// Held outside any branch, because derivatives in non-uniform control flow are undefined.
	//
	// Only the rim is shaded. Shading further in was a mistake: past the round-over a well should be
	// a void, and lighting the inside of a void turns it into a lump - which is exactly how it read,
	// helped by abs() here throwing away the sign so a bowl and a bulge shaded identically.
	vec3 gn = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	float facing = abs(dot(gn, normalize(-VERTEX)));
	col *= mix(1.0, 0.35 + 0.65 * facing, smoothstep(0.0, 0.09, well));

	// Into black quickly, so what lies past the round-over is a hole and not a lit cave.
	col = mix(col, vec3(0.0), smoothstep(0.04, 0.17, well));

	// A tight bright ring on the rim itself, which is what makes an edge read as an edge. Gated so
	// it vanishes on flat wall, where the band's tail would otherwise wash the whole tube.
	float lip = (well - 0.05) / 0.03;
	col += to_linear(seam_light) * (1.0 + 2.0 * glow) * 1.15 * exp(-lip * lip) * smoothstep(0.0, 0.02, well);

	ALBEDO = col;
	}
}
