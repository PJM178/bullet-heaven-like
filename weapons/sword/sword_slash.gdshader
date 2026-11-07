// The sword's slash: a flat arc-shaped strip that a bright band sweeps
// across, leaving a short fading trail behind it.
//
// The mesh (built in SwordWeapon) is a curved strip: UV.x runs along the
// arc from where the swing starts (0) to where it ends (1), and UV.y runs
// from the inner edge (0) to the blade tip's edge (1). The weapon moves
// "progress" from 0 to 1 over the swing; this shader decides which part
// of the strip is visible at that moment.
shader_type spatial;
// unshaded: a flat colour, no lighting. blend_mix: normal transparency.
// depth_draw_never: don't hide things behind it. cull_disabled: visible
// from both sides of the strip.
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

uniform vec4 color : source_color = vec4(0.86, 0.88, 0.9, 0.75);

// Set by SwordWeapon every frame of the swing: 0 = start, 1 = done.
uniform float progress : hint_range(0.0, 1.0) = 0.0;

// How much of the arc the trail behind the blade covers (fraction).
uniform float trail : hint_range(0.05, 1.0) = 0.6;

// Hard-edged fade steps instead of a smooth gradient, for the PS1 look.
uniform float bands = 3.0;

void fragment() {
	// The blade's leading edge travels from 0 to past 1, far enough that
	// the trail behind it has fully left the arc when progress reaches 1.
	float head = progress * (1.0 + trail);

	// How far this pixel is behind the leading edge, along the arc.
	float behind = head - UV.x;
	if (behind < 0.0 || behind > trail) {
		discard; // ahead of the blade, or the trail has already passed
	}

	// Solid at the blade, fading out along the trail, in hard steps.
	float alpha = 1.0 - behind / trail;
	alpha = ceil(alpha * bands) / bands;

	// Fade toward the inner edge, so the slash reads as the path of the
	// blade's tip rather than a solid slice of pie.
	alpha *= smoothstep(0.0, 0.5, UV.y);

	ALBEDO = color.rgb;
	ALPHA = alpha * color.a;
}
