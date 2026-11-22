// The game-over fade, PS1 style. It reads what has already been drawn on
// screen (hint_screen_texture) and, as `amount` rises from 0 to 1:
//   - drains the colour toward a dusty grey and darkens it,
//   - cuts the colours down to a few levels with ordered (Bayer) dithering,
//     the checkered-dot shading 16-bit consoles used in place of smooth
//     gradients,
//   - and makes the pixels chunkier.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;

// 0 = untouched picture, 1 = fully faded. GameOverScreen animates this.
uniform float amount : hint_range(0.0, 1.0) = 0.0;

// The dusty tint the world fades toward, and how dark it ends up.
uniform vec3 tint = vec3(0.78, 0.74, 0.70);
uniform float darkness : hint_range(0.0, 1.0) = 0.5;

// How much the screen corners darken extra (a vignette).
uniform float vignette : hint_range(0.0, 2.0) = 0.9;

// Size of one dither dot / chunky pixel in screen pixels at full fade.
uniform float pixel_size : hint_range(1.0, 12.0) = 4.0;

// Colour levels per channel at full fade. Fewer = harsher banding.
uniform float levels : hint_range(2.0, 32.0) = 8.0;

// The classic 4x4 Bayer matrix: each cell holds a threshold from 0 to 1,
// spread so neighbouring cells differ as much as possible. Comparing a
// colour against it turns in-between shades into a fine dot pattern.
const float BAYER[16] = float[16](
	 0.0,  8.0,  2.0, 10.0,
	12.0,  4.0, 14.0,  6.0,
	 3.0, 11.0,  1.0,  9.0,
	15.0,  7.0, 13.0,  5.0);

void fragment() {
	// Chunky pixels: snap to a grid that grows coarser as the fade goes on.
	float size = floor(mix(1.0, pixel_size, amount) + 0.5);
	vec2 cell = floor(FRAGCOORD.xy / size);
	vec2 uv = (cell * size + size * 0.5) * SCREEN_PIXEL_SIZE;
	vec3 picture = texture(screen_texture, uv).rgb;

	// Brightness as the eye sees it (green counts most), tinted.
	float grey = dot(picture, vec3(0.299, 0.587, 0.114));
	vec3 colour = mix(picture, vec3(grey) * tint, amount);
	colour *= 1.0 - darkness * amount;

	// Darker toward the edges: 0 in the centre, about 1 in the corners.
	float edge = length(UV - 0.5) * 1.4;
	colour *= 1.0 - clamp(edge * edge * vignette, 0.0, 1.0) * amount;

	// Ordered dithering: nudge each colour by this cell's threshold, then
	// round down to a few levels. Areas between two levels become a dot
	// pattern instead of a smooth blend.
	int index = int(mod(cell.x, 4.0)) + int(mod(cell.y, 4.0)) * 4;
	float threshold = (BAYER[index] + 0.5) / 16.0 - 0.5;
	float steps = mix(32.0, levels, amount);
	colour = floor(colour * steps + threshold + 0.5) / steps;

	COLOR = vec4(clamp(colour, 0.0, 1.0), 1.0);
}
