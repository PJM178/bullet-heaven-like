// A textured, lit PS1-style material for models: the texture warps across
// tilted triangles (affine mapping) and corners can jitter to a coarse
// screen grid (vertex snapping). See core/ps1.gdshaderinc for how both work.
//
// Keep textures tiny and set "Filter" off on import (or rely on
// filter_nearest below) so they stay pixel-sharp.
shader_type spatial;

#include "res://core/ps1.gdshaderinc"

uniform sampler2D albedo_texture : source_color, filter_nearest, repeat_enable;

// Multiplied with the texture and the vertex colours.
uniform vec4 tint : source_color = vec4(1.0);

// How much of the PS1 texture bend to use: 0 = modern, 1 = full PS1.
uniform float affine_strength : hint_range(0.0, 1.0) = 1.0;

// Size of the vertex snapping grid across the screen; 0 = off.
uniform vec2 snap_resolution = vec2(0.0, 0.0);

varying vec3 affine_uv;

void vertex() {
	vec4 clip = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	clip = ps1_snap(clip, snap_resolution);
	POSITION = clip;
	affine_uv = vec3(UV * clip.w, clip.w);
}

void fragment() {
	vec2 uv = mix(UV, affine_uv.xy / affine_uv.z, affine_strength);
	ALBEDO = texture(albedo_texture, uv).rgb * COLOR.rgb * tint.rgb;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
