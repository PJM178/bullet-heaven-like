// The sword's slash: the classic cartoon "swoosh". A crisp crescent whips
// across behind the blade, from the character's right to their left. It's
// thickest just behind the blade and sharp at both ends, and its tail
// catches up with the blade until the crescent closes and is gone.
//
// The mesh (built in SwordWeapon) is a flat curved strip covering the
// swing's wedge. Its UVs give us coordinates along the swing:
//   UV.x: 0 where the swing starts (the right) .. 1 where it ends (left)
//   UV.y: 0 at the strip's inner edge .. 1 at its outer edge, which is
//         the path of the blade's tip
// The swoosh is drawn hugging the outer edge, growing inward.
//
// The weapon moves "progress" from 0 to 1 over the swing.
shader_type spatial;
// unshaded: flat colour, no lighting. blend_mix: normal transparency.
// depth_draw_never: don't hide things behind it. cull_disabled: visible
// from both sides of the strip.
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

// The bright edge where the blade cut, and the body of the swoosh.
uniform vec4 edge_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 body_color : source_color = vec4(0.78, 0.86, 0.93, 0.85);

// Set by SwordWeapon every frame of the swing: 0 = start, 1 = done.
uniform float progress : hint_range(0.0, 1.0) = 0.0;

// When the blade (the swoosh's head) reaches the end of the arc, as a
// fraction of the swing. The head moves fast and slows at the end.
uniform float blade_end : hint_range(0.1, 1.0) = 0.45;

// When the tail starts chasing the blade. It speeds up and reaches the end
// at progress 1, closing the crescent.
uniform float tail_start : hint_range(0.0, 0.9) = 0.12;

// The thickest part of the swoosh, as a fraction of the strip's width.
uniform float max_thickness : hint_range(0.05, 1.0) = 0.42;

// Share of the swoosh's thickness drawn as the bright edge.
uniform float edge_share : hint_range(0.0, 1.0) = 0.3;

void fragment() {
	float along = UV.x;

	// Where the blade (head) and the tail are along the arc right now.
	// 1 - (1 - x)^2 is an "ease out": the head bursts off fast and slows
	// as it reaches the end. The tail's power curve does the opposite: it
	// starts slow and accelerates, so the crescent closes with a snap.
	float head = 1.0 - pow(1.0 - clamp(progress / blade_end, 0.0, 1.0), 2.0);
	float tail = pow(smoothstep(tail_start, 1.0, progress), 1.5);

	// Only the stretch between tail and head has a swoosh.
	if (along > head || along < tail || head - tail < 0.001) {
		discard;
	}

	// Position inside the swoosh: 0 at the tail .. 1 at the blade.
	float u = (along - tail) / (head - tail);

	// Crescent thickness along the swoosh. u^1.5 grows slowly from 0, so
	// the tail is a sharp needle; (1 - u)^0.5 drops steeply only at the
	// very end, so the blade end is heavy and rounded. Their product
	// peaks three-quarters of the way along, at 0.325, so dividing by
	// that makes the fattest point exactly max_thickness.
	float thickness = max_thickness * pow(u, 1.5) * pow(1.0 - u, 0.5) / 0.325;

	// The swoosh grows inward from the blade's path (the outer edge, UV.y
	// = 1). How deep into the swoosh this pixel is: 0 at the outer edge,
	// 1 at its inner edge.
	float depth = (1.0 - UV.y) / max(thickness, 0.0001);
	if (depth > 1.0) {
		discard;
	}

	// Hard-edged tones, cel-shaded like a cartoon: a bright edge along the
	// blade's path, the softer body behind it.
	if (depth < edge_share) {
		ALBEDO = edge_color.rgb;
		ALPHA = edge_color.a;
	} else {
		ALBEDO = body_color.rgb;
		ALPHA = body_color.a;
	}
}
