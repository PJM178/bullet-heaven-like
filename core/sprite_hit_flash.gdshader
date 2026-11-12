// Hit flash for sprite enemies: draws the sprite's shape in plain white.
//
// Swapped in through MaterialOverride, like the mesh hit flash
// (hit_flash_material.tres), so one shared material serves every enemy.
// An override replaces the sprite's own material, which also did the
// billboarding and knew the texture, so this shader does both itself:
// "sheet" is set to the sprite's atlas (all frames live in one texture,
// and the sprite's UVs already point at the current frame).
shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D sheet : source_color, filter_nearest, repeat_disable;

void vertex() {
	// Y-fixed billboard: stand upright and turn to face the camera, the
	// same as SpriteBase3D's "Fixed Y" mode (Godot's own generated code).
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
		vec4(normalize(cross(vec3(0.0, 1.0, 0.0), INV_VIEW_MATRIX[2].xyz)), 0.0),
		vec4(0.0, 1.0, 0.0, 0.0),
		vec4(normalize(cross(INV_VIEW_MATRIX[0].xyz, vec3(0.0, 1.0, 0.0))), 0.0),
		MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	// Same hard cut-out as the sprite itself (AlphaCut Discard).
	if (texture(sheet, UV).a < 0.5) {
		discard;
	}
	ALBEDO = vec3(1.0);
}
