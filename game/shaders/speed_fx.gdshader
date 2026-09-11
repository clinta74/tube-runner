// Full-screen speed effects, drawn on a ColorRect over the 3D view (see SpeedFx.cs):
// radial blur toward the edges, chromatic aberration, flickering streaks, and a vignette.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
// 0 = no effect, 1 = full effect. Driven by speed.
uniform float intensity : hint_range(0.0, 1.0) = 0.0;
// Fraction of the distance to the center that edge pixels are smeared.
uniform float blur_strength = 0.08;
uniform float aberration = 0.012;
uniform float streaks = 0.5;
uniform vec3 streak_color = vec3(1.0);
uniform float vignette = 0.5;
// Screen point the motion radiates from (the vanishing point is a little below center).
uniform vec2 focus = vec2(0.5, 0.55);

const int BLUR_SAMPLES = 8;

float hash(float n) {
	return fract(sin(n * 91.345) * 47453.5453);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 dir = uv - focus;
	// Distance from the focus, corrected for a 16:9 screen.
	float r = length(dir * vec2(16.0 / 9.0, 1.0));
	float edge = smoothstep(0.15, 0.85, r) * intensity;

	// Radial blur: average samples pulled toward the focus, more strongly near the edges.
	vec3 col = vec3(0.0);
	for (int i = 0; i < BLUR_SAMPLES; i++) {
		float t = float(i) / float(BLUR_SAMPLES - 1);
		col += texture(screen_tex, uv - dir * (t * blur_strength * edge)).rgb;
	}
	col /= float(BLUR_SAMPLES);

	// Chromatic aberration: red and blue split apart along the motion.
	float split = aberration * edge;
	col.r = mix(col.r, texture(screen_tex, uv + dir * split).r, 0.8);
	col.b = mix(col.b, texture(screen_tex, uv - dir * split).b, 0.8);

	// Streaks: thin radial lines that flicker, only toward the edges.
	float bin = floor(atan(dir.y, dir.x) * 90.0 / 6.2831853);
	float flicker = step(0.94, hash(bin + floor(TIME * 20.0) * 17.0));
	col += streak_color * flicker * streaks * edge * smoothstep(0.35, 0.95, r);

	col *= 1.0 - vignette * smoothstep(0.45, 1.15, r) * (0.4 + 0.6 * intensity);
	COLOR = vec4(col, 1.0);
}
