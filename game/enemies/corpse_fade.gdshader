// Draws a dying sprite enemy and dithers it away at the end, PS1 style:
// instead of turning see-through, more and more pixels drop out in the
// ordered (Bayer) dot pattern until nothing is left.
//
// One material is shared by every corpse, so the fade amount can't be a
// uniform (that would fade them all together). Each corpse passes it in
// its Modulate alpha instead, which reaches the shader as the vertex
// COLOR. (A per-object "instance uniform" would also work, but phones
// like the Adreno 642L only have room for about 256 of those.)
//
// Like the hit flash, this replaces the sprite's own material through
// MaterialOverride, so it billboards and samples the atlas itself.
shader_type spatial;
render_mode unshaded, cull_disabled;

#include "res://core/ps1.gdshaderinc"

// The sprite's atlas. All frames live in it, and the sprite's UVs already
// point at the current frame.
uniform sampler2D sheet : source_color, filter_nearest, repeat_disable;

// Size of one dither dot, in screen pixels.
uniform float dot_size : hint_range(1.0, 8.0) = 2.0;

varying float visible;

void vertex() {
	// Y-fixed billboard, the same as SpriteBase3D's "Fixed Y" mode.
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
		vec4(normalize(cross(vec3(0.0, 1.0, 0.0), INV_VIEW_MATRIX[2].xyz)), 0.0),
		vec4(0.0, 1.0, 0.0, 0.0),
		vec4(normalize(cross(INV_VIEW_MATRIX[0].xyz, vec3(0.0, 1.0, 0.0))), 0.0),
		MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	// 1 = fully there, 0 = gone (the corpse's Modulate alpha).
	visible = COLOR.a;
}

void fragment() {
	vec4 texel = texture(sheet, UV);
	// The sprite's own hard cut-out, plus the dither: a pixel stays only
	// while the visible amount is above its cell's threshold.
	if (texel.a < 0.5 || visible <= ps1_bayer(FRAGCOORD.xy / dot_size)) {
		discard;
	}
	ALBEDO = texel.rgb * COLOR.rgb;
}
