// The game-over title sign: text on a strip of big triangles standing in
// the world, rippling like a banner in the wind. Drawn with PS1 affine
// texture warping and vertex jitter (core/ps1.gdshaderinc), fading in
// through an ordered dither. Unshaded, so the scene's lighting doesn't
// dim it. Both sides show the text the right way round.
//
// The ripple is what shows off the warp: each triangle keeps tilting
// toward and away from the camera, and the PS1 mapping bends the letters
// across it differently every moment.
shader_type spatial;
render_mode unshaded, cull_disabled;

#include "res://core/ps1.gdshaderinc"

// The rendered text (GameOverScreen sets this from a SubViewport).
uniform sampler2D albedo_texture : source_color, filter_nearest;

// 0 = modern, 1 = full PS1 texture bend.
uniform float affine_strength : hint_range(0.0, 1.0) = 1.0;

// Vertex snapping grid across the screen; lower = more wobble. 0 = off.
uniform vec2 snap_resolution = vec2(480.0, 216.0);

// 0 = invisible, 1 = fully shown, fading in dot by dot.
uniform float reveal : hint_range(0.0, 1.0) = 1.0;

// Size of one dither dot, in screen pixels.
uniform float dither_size : hint_range(1.0, 12.0) = 4.0;

// The ripple: how far the sign bulges toward/away from the camera and
// how far it sways up and down (metres), the distance between wave crests
// (metres) and how fast the waves travel along the sign (crests per
// second).
uniform float wave_amplitude = 0.25;
uniform float wave_lift = 0.015;
uniform float wave_length = 1.5;
uniform float wave_speed = 0.3;

varying vec3 affine_uv;

void vertex() {
	// A wave travelling along the sign (x), pushing it in and out (z).
	// TAU / wave_length turns metres into radians, so one full sine cycle
	// spans one wave_length. A second, smaller and slower wave running at
	// a slant keeps the motion from looking perfectly regular. TIME is
	// the shader clock in seconds; it keeps running while the game is
	// paused, so the sign keeps rippling on the death screen.
	float phase = TAU * (VERTEX.x / wave_length - TIME * wave_speed);
	VERTEX.z += sin(phase) * wave_amplitude;
	// A quarter cycle behind the bulge (cos instead of sin), so the word
	// rolls like a wave rather than just bobbing.
	VERTEX.y += cos(phase) * wave_lift;
	VERTEX.z += sin(TAU * ((VERTEX.x + VERTEX.y * 2.0) / (wave_length * 1.7) + TIME * wave_speed * 0.45))
		* wave_amplitude * 0.35;

	vec4 clip = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	clip = ps1_snap(clip, snap_resolution);
	POSITION = clip;
	affine_uv = vec3(UV * clip.w, clip.w);
}

void fragment() {
	vec2 uv = mix(UV, affine_uv.xy / affine_uv.z, affine_strength);
	// Seen from behind, the text would be mirrored; flip it back.
	if (!FRONT_FACING) {
		uv.x = 1.0 - uv.x;
	}
	vec4 text = texture(albedo_texture, uv);
	ALBEDO = text.rgb;
	// Hard-edged: a pixel is either there or not, like the PS1's
	// cut-out textures. The dither decides which pixels have appeared.
	ALPHA = text.a * step(ps1_bayer(FRAGCOORD.xy / dither_size), reveal);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
}
