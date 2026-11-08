// Plays a sprite sheet as a flipbook: a row of animation frames in one
// texture, showing one frame at a time. Used for effects such as the
// hammer smash (weapons/hammer/). The script driving it sets "frame";
// stepping whole frames at a low rate gives the choppy, hand-drawn look of
// old sprite effects.
//
// Blending is additive: the sprite's colour is added to what's behind it,
// so effects glow, overlapping effects get brighter, and black / low alpha
// is invisible. A sheet controls how faint each pixel is with its alpha.
//
// The quad can lie in the world as it is (e.g. a PlaneMesh flat on the
// ground) or, with "billboard" on, always turn to face the camera like a
// 2D sprite standing in the 3D world.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;

// filter_nearest keeps the pixels crisp when scaled up; repeat_disable
// stops the edge of one frame bleeding in from the opposite side.
uniform sampler2D sheet : source_color, filter_nearest, repeat_disable;
uniform int frames = 1;
uniform int frame = 0;

// Multiplies the sheet's colours. intensity > 1 makes it glow brighter.
uniform vec4 tint : source_color = vec4(1.0);
uniform float intensity = 1.0;

uniform bool billboard = false;
// Mirror the sprite left to right.
uniform bool flip_h = false;

void vertex() {
	if (billboard) {
		// Replace the model's rotation with the camera's, keeping its
		// position and scale, so the quad always faces the camera. (The
		// same maths StandardMaterial3D's billboard mode uses.)
		MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
				INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
		MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(
				vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
				vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
				vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
				vec4(0.0, 0.0, 0.0, 1.0));
		MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	}
}

void fragment() {
	vec2 uv = UV;
	if (flip_h) {
		uv.x = 1.0 - uv.x;
	}
	// Squeeze UV.x into this frame's slice of the row.
	uv.x = (uv.x + float(frame)) / float(frames);

	vec4 color = texture(sheet, uv) * tint;
	ALBEDO = color.rgb * intensity;
	ALPHA = color.a;
}
