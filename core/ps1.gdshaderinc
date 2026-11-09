// Shared PS1-style building blocks. A shader pulls them in with
//   #include "res://core/ps1.gdshaderinc"
// which pastes this file in, so every PS1 shader warps and jitters the
// same way.
//
// The two classic PS1 artefacts:
//
// 1. Vertex snapping. The PS1 had no sub-pixel precision, so every corner
//    of every triangle landed on a whole screen pixel. Shapes jitter and
//    wobble as they move. ps1_snap() rounds a vertex to a coarse grid.
//
// 2. Affine texture mapping. The PS1 spread textures across a triangle in
//    a straight line on screen, ignoring depth. On a surface tilted away
//    from the camera the texture bends, with a visible kink along each
//    triangle's diagonal. Modern GPUs always correct for depth, so to
//    undo it a shader passes the UV multiplied by the vertex's depth (w)
//    plus w itself, and divides one by the other per pixel. The GPU's
//    correction then cancels out. In the shader:
//
//      varying vec3 affine_uv;
//      vertex:   affine_uv = vec3(UV * clip.w, clip.w);
//      fragment: vec2 uv = affine_uv.xy / affine_uv.z;
//
//    (Godot only lets vertex() and fragment() write varyings, which is
//    why those two lines live in each shader rather than here.)

// Rounds a clip-space position to a grid of `resolution` cells across the
// screen. A low resolution (e.g. 320 x 240) gives the PS1 wobble; 0 turns
// snapping off.
vec4 ps1_snap(vec4 clip, vec2 resolution) {
	if (resolution.x <= 0.0 || resolution.y <= 0.0) {
		return clip;
	}
	// Clip space divided by w gives -1..1 across the screen ("NDC"), so
	// half the resolution maps one grid cell to one step of 1.
	vec2 half_res = resolution * 0.5;
	vec2 ndc = clip.xy / clip.w;
	ndc = round(ndc * half_res) / half_res;
	return vec4(ndc * clip.w, clip.zw);
}

// The 4x4 Bayer matrix: ordered-dither thresholds from 0 to 1, spread so
// neighbouring cells differ as much as possible. Comparing a value against
// it turns in-between amounts into the PS1-era dot pattern.
const float PS1_BAYER[16] = float[16](
	 0.0,  8.0,  2.0, 10.0,
	12.0,  4.0, 14.0,  6.0,
	 3.0, 11.0,  1.0,  9.0,
	15.0,  7.0, 13.0,  5.0);

// The dither threshold (0..1) for the screen cell containing `cell`
// (pass FRAGCOORD.xy, or FRAGCOORD.xy / dot_size for bigger dots).
float ps1_bayer(vec2 cell) {
	ivec2 c = ivec2(mod(floor(cell), 4.0));
	return (PS1_BAYER[c.x + c.y * 4] + 0.5) / 16.0;
}
