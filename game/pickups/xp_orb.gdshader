// An XP orb: a small polished sphere, like a gem or a glass marble with
// colour deep inside. It doesn't glow; it's lit by the scene like any
// other object, with a soft sheen.
//
// Three things make it read as polished:
//   - depth: brighter where the surface faces us, darker toward the edges,
//     as if looking into something translucent;
//   - the real light's specular highlight (low roughness);
//   - a small, crisp painted highlight that always sits at the upper left,
//     so the sheen shows even when the scene light doesn't catch it. Hard
//     highlights like this were a common PS1-era trick.
//
// Orbs are drawn with a MultiMesh (see XpManager), so this one material
// draws every orb at once. INSTANCE_ID tells the shader which orb it is
// drawing, which lets each orb bob at its own phase for free, on the GPU,
// without the CPU touching orbs that are just hovering.
shader_type spatial;

// Colour where the surface faces the camera, and at the rim.
uniform vec4 center_color : source_color = vec4(0.42, 0.66, 0.6, 1.0);
uniform vec4 edge_color : source_color = vec4(0.1, 0.24, 0.24, 1.0);

// Lower is shinier: a tighter, brighter highlight from the scene light.
uniform float roughness : hint_range(0.0, 1.0) = 0.3;

// The painted highlight: how bright, and how big (closer to 1 = smaller).
uniform float highlight_strength : hint_range(0.0, 1.0) = 0.35;
uniform float highlight_size : hint_range(0.8, 1.0) = 0.95;

uniform float bob_height = 0.03;
uniform float bob_speed = 2.0;

void vertex() {
	// Multiplying the id by an awkward number spreads the phases out, so
	// neighbouring orbs don't bob in lockstep.
	float phase = float(INSTANCE_ID) * 1.7;
	VERTEX.y += sin(TIME * bob_speed + phase) * bob_height;
}

void fragment() {
	// NORMAL and VIEW are in view space: the camera looks along -Z, so
	// VIEW points from the surface toward the camera. Their dot product is
	// 1 where the surface faces us and falls to 0 at the silhouette.
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	ALBEDO = mix(edge_color.rgb, center_color.rgb, facing * facing);

	ROUGHNESS = roughness;
	SPECULAR = 0.5;

	// The painted highlight: where the normal points up-left toward the
	// viewer. smoothstep over a narrow range gives a small spot with a
	// crisp edge. EMISSION just adds brightness here; with no bloom in
	// the game, it doesn't glow.
	vec3 highlight_dir = normalize(vec3(-0.45, 0.55, 0.7));
	float spot = smoothstep(highlight_size, highlight_size + 0.02, dot(NORMAL, highlight_dir));
	EMISSION = vec3(spot * highlight_strength);
}
