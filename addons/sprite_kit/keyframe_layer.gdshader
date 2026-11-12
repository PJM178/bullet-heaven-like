// One layer of an STR
// effect, drawn as a quad facing the
// camera ("spherical billboard"), whose four corners are given each frame
// by the CPU in view space (x right, y up, in metres).
shader_type spatial;

// unshaded: no lighting. blend_add: src * src_alpha + dst, the same as the
// D3D blend (SRC_ALPHA, DEST_ALPHA) STR files use, since the back buffer's
// alpha is 1. skip_vertex_transform: VERTEX is already in view space.
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled,
	cull_disabled, skip_vertex_transform, fog_disabled, shadows_disabled;

uniform sampler2D layer_texture : source_color, filter_linear, repeat_disable;
// Corner positions, one component per corner (0 top-left, 1 top-right,
// 2 bottom-left, 3 bottom-right of the texture).
uniform vec4 corner_x;
uniform vec4 corner_y;
// The keyframe colour (RGBA, 0..1).
uniform vec4 tint = vec4(1.0);

void vertex() {
	// QuadMesh UVs are (0,0) (1,0) (0,1) (1,1) at its corners, so they tell
	// which corner this vertex is.
	int corner = int(UV.x + 0.5) + int(UV.y + 0.5) * 2;
	// A one-hot vector (1 in the slot of this corner) picks its values.
	vec4 pick = vec4(equal(ivec4(corner), ivec4(0, 1, 2, 3)));
	vec3 centre = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	VERTEX = centre + vec3(dot(corner_x, pick), dot(corner_y, pick), 0.0);
}

void fragment() {
	vec4 texel = texture(layer_texture, UV);
	ALBEDO = texel.rgb * tint.rgb;
	ALPHA = texel.a * tint.a;
}
