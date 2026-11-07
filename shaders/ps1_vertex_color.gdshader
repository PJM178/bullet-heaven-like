// The shared base shader for the game's opaque models (player, enemies,
// props). It reproduces the matte, vertex-coloured look we had with
// StandardMaterial3D, plus a hit flash. The PS1-style effects (vertex
// snapping, etc.) are meant to be added here later, so every model gets
// them at once.
//
// Each model has its own ShaderMaterial (.tres) that points at this file
// and fills in the "uniform" settings below. Models of the same kind share
// one material, e.g. every slime uses models/slime_material.tres.
shader_type spatial;

// Multiplied with the vertex colours. Models painted with vertex colours
// in Blender leave it white; a model without vertex colours (where COLOR
// is white) gets its whole colour from here, like the bush.
// "source_color" makes the editor show a colour picker and treat the
// value as a normal (sRGB) colour.
uniform vec4 tint : source_color = vec4(1.0);

// How white the model is drawn: 0 = normal, 1 = fully white. Normal
// materials leave it at 0. shaders/hit_flash_material.tres sets it to 1,
// and code swaps that material onto a model while it flashes (see
// EnemyManager.SetHitFlash). Using this same shader for the flash means
// the PS1 effects added here later apply to flashing models too.
//
// (This is a plain uniform, one value per material. A per-mesh
// "instance uniform" would avoid the material swap, but phones such as
// the Adreno 642L only fit about 256 of those in the whole game.)
uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	// COLOR is the vertex colour, blended across the triangle.
	vec3 base = COLOR.rgb * tint.rgb;

	// mix(a, b, t) blends from a (t = 0) to b (t = 1).
	ALBEDO = mix(base, vec3(1.0), hit_flash);

	// Same as the old materials: fully rough, no shiny highlight.
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
