// Smooth checkerboard wall in the level's theme colors. The track is divided into segments
// separated by a seam line; each segment picks its own palette pair, checker size, and twist.
// Each mesh chunk is one segment (see TrackRenderer.cs).
// UV.x = distance around the section / perimeter (continues past 0..1 onto open planes),
// UV.y = distance into the segment (world units).
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

	float x = UV.x * AROUND[pick(seg, 1.0, 4)] + local * TWIST[pick(seg, 2.0, 4)];
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

	// The wall carries no normals and the shader is unshaded, so the inward curve has nothing to
	// light it: take a geometric normal from the screen derivatives instead. Held outside any branch,
	// because derivatives in non-uniform control flow are undefined. Wall turning away from the eye
	// loses light, which is what gives the throat its form rather than a flat dark patch.
	vec3 gn = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	float facing = abs(dot(gn, normalize(-VERTEX)));
	col *= mix(1.0, 0.30 + 0.70 * facing, smoothstep(0.0, 0.20, well));

	// Then to black just inside the rim, so the lit wall ends against a hole.
	col = mix(col, vec3(0.0), smoothstep(0.05, 0.40, well));

	// A thin bright band riding the rim itself draws the lip as a hard edge against that dark. Gated
	// so it vanishes on flat wall, where the band's tail would otherwise wash the whole tube.
	float lip = (well - 0.08) / 0.05;
	col += to_linear(seam_light) * (1.0 + 2.0 * glow) * 0.85 * exp(-lip * lip) * smoothstep(0.0, 0.03, well);

	ALBEDO = col;
}
