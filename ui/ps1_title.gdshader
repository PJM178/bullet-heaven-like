// The game-over title sign: text on a few big triangles standing in the
// world, drawn with PS1 affine texture warping and vertex jitter
// (core/ps1.gdshaderinc), fading in through an ordered dither. Unshaded,
// so the scene's lighting doesn't dim it. Both sides show the text the
// right way round.
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

varying vec3 affine_uv;

void vertex() {
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
