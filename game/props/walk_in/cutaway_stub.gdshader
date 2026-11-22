// A wall or post cut down to a low stub while the player is inside a walk-in
// space (see WalkInSpace.cs). Everything above stub_height is thrown away,
// so the room's outline and its doorways stay readable without blocking
// the view. The texture is the piece's own, unsquashed.
shader_type spatial;

// Both sides are drawn: with the top cut off, you look into the wall from
// above and would otherwise see through its missing faces.
render_mode cull_disabled;

uniform sampler2D albedo_texture : source_color, filter_nearest_mipmap, repeat_enable;
uniform vec4 albedo_color : source_color = vec4(1.0);

// In the piece's own coordinates: its floor is at 0.
uniform float stub_height = 0.5;

// VERTEX in vertex() is still in the model's own space, so this is the
// height above the piece's base, passed on to every pixel.
varying float height;

void vertex() {
	height = VERTEX.y;
}

void fragment() {
	if (height > stub_height) {
		discard;
	}
	ALBEDO = albedo_color.rgb * texture(albedo_texture, UV).rgb;
}
