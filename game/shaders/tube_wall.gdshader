// Smooth checkerboard wall in earthy brown / green / gold. The tube is divided into segments
// separated by a seam line; each segment picks its own palette, checker size, and twist.
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
// Distance at which the wall fades toward the far color.
uniform float fade_start = 30.0;
uniform float fade_end = 320.0;

// Palette authored in sRGB; converted to linear in fragment().
const vec3 DARKS[3] = vec3[3](
	vec3(0.23, 0.16, 0.10),  // dark brown
	vec3(0.18, 0.29, 0.17),  // moss green
	vec3(0.29, 0.31, 0.14)   // olive
);
const vec3 LIGHTS[3] = vec3[3](
	vec3(0.69, 0.55, 0.24),  // gold
	vec3(0.48, 0.35, 0.18),  // bronze
	vec3(0.55, 0.54, 0.26)   // pale olive-gold
);
// Checker cells around the tube; must be even so the pattern meets itself at the seam.
const float AROUND[4] = float[4](8.0, 10.0, 12.0, 16.0);
// Checker cell length along the tube; divisors of the default segment length.
const float ALONG[4] = float[4](3.0, 4.0, 5.0, 6.0);
// Twist in cells per world unit.
const float TWIST[4] = float[4](0.0, 0.0, 0.06, -0.06);

const vec3 SEAM_DARK = vec3(0.12, 0.08, 0.04);
const vec3 SEAM_GOLD = vec3(0.85, 0.68, 0.30);
const vec3 FAR_COLOR = vec3(0.05, 0.04, 0.02);

float hash(float n, float salt) {
	return fract(sin(n * 127.1 + salt * 311.7) * 43758.5453);
}

int pick(float seg, float salt, int count) {
	return int(floor(hash(seg, salt) * float(count)));
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
	vec3 col = mix(DARKS[pick(seg, 4.0, 3)], LIGHTS[pick(seg, 5.0, 3)], t);

	// Seam at each segment boundary: a dark groove with a thin gold core.
	float d = min(local, segment_length - local);
	float aa = fwidth(local);
	col = mix(col, SEAM_DARK, 1.0 - smoothstep(0.35, 0.7 + aa, d));
	col = mix(col, SEAM_GOLD, 1.0 - smoothstep(0.08, 0.18 + aa, d));

	// Fade with distance for depth.
	col = mix(col, FAR_COLOR, smoothstep(fade_start, fade_end, length(VERTEX)));

	ALBEDO = pow(col, vec3(2.2));
}
