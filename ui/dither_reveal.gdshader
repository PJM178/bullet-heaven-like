// Fades a control in through an ordered dither pattern instead of smooth
// transparency: dots switch on one by one in a fixed pattern until it is
// solid, the way PS1-era games faded things in. Used for the game-over
// text.
shader_type canvas_item;

// 0 = invisible, 1 = fully shown. GameOverScreen animates this.
uniform float reveal : hint_range(0.0, 1.0) = 0.0;

// Size of one dither dot in screen pixels.
uniform float pixel_size : hint_range(1.0, 12.0) = 4.0;

const float BAYER[16] = float[16](
	 0.0,  8.0,  2.0, 10.0,
	12.0,  4.0, 14.0,  6.0,
	 3.0, 11.0,  1.0,  9.0,
	15.0,  7.0, 13.0,  5.0);

void fragment() {
	vec4 colour = texture(TEXTURE, UV) * COLOR;

	vec2 cell = floor(FRAGCOORD.xy / pixel_size);
	int index = int(mod(cell.x, 4.0)) + int(mod(cell.y, 4.0)) * 4;
	float threshold = (BAYER[index] + 0.5) / 16.0;

	// A dot is shown once reveal passes its threshold; the glyph edges
	// stay hard (no half-transparent pixels), which suits the look.
	float shown = step(threshold, reveal);
	COLOR = vec4(colour.rgb, step(0.5, colour.a) * shown);
}
