using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Describes one kind of breakable prop (barrel, crate, pot, ...): its model,
/// how much it takes to break, how it breaks apart and what falls out.
/// Saved as its own .tres (e.g. props/breakables/barrel.tres), like
/// PickupDefinition. A new kind of breakable is a new .tres, not new code.
///
/// The <see cref="BreakableManager"/> does all the work; this is only data.
/// </summary>
[GlobalClass]
public partial class BreakableDefinition : Resource
{
    [Export] public string DisplayName = "";

    // Hits needed depend on the weapons: the sword does 1 per swing.
    [Export] public float Health = 3.0f;

    // How wide it is, in metres from its centre: weapons hit it within this
    // distance, and the player bumps into a cylinder of this radius.
    [Export] public float Radius = 0.35f;

    // Height of that cylinder, and of the stand-in model.
    [Export] public float Height = 0.8f;

    // Shown on the compass (see WorldMarkers) until it breaks. Optional:
    // most props don't need one.
    [Export] public WorldMarkerDefinition Marker;

    [ExportGroup("Looks")]
    // The model (.glb or .tscn), stored as a text path because the models
    // live in the git-ignored local_assets/ (see DirectionalSprite3D.FramesPath
    // for why). Without the file a plain cylinder of Radius and Height
    // stands in, so the prop still works in a fresh clone.
    [Export(PropertyHint.File, "*.glb,*.tscn")] public string ModelPath = "";

    // How many slices the sides break into when it breaks, cut like a cake
    // around its upright axis. The top and the bottom become one more piece
    // each. 8 suits an eight-sided barrel: one slice per stave.
    [Export(PropertyHint.Range, "1,16,1")] public int Slices = 8;

    // A one-shot sprite animation played where it breaks (a dust puff), also
    // a text path into local_assets/. Optional.
    [Export(PropertyHint.File, "*.spritesheet")] public string DustFramesPath = "";
    [Export] public float DustPixelSize = 0.012f;

    [ExportGroup("Loot")]
    // Each line is rolled on its own when it breaks.
    [Export] public LootDrop[] Loot = System.Array.Empty<LootDrop>();

    // ---- Loaded or built on first use, then shared by every prop of this
    // kind. Not exported, so none of it is saved into the .tres. ----------
    private PackedScene _model;
    private bool _modelLoaded;
    private SpriteFrames _dustFrames;
    private bool _dustLoaded;

    /// <summary>The pieces it breaks into. BreakableManager builds them
    /// from the first prop of this kind that breaks.</summary>
    public MeshShatter.Piece[] Pieces;

    /// <summary>The model, or null if the file is missing.</summary>
    public PackedScene Model
    {
        get
        {
            if (!_modelLoaded)
            {
                _modelLoaded = true;
                if (ModelPath != "" && ResourceLoader.Exists(ModelPath))
                    _model = GD.Load<PackedScene>(ModelPath);
                else if (ModelPath != "")
                    GD.PushWarning($"Breakable '{DisplayName}': {ModelPath} not found; using a stand-in cylinder.");
            }
            return _model;
        }
    }

    /// <summary>The dust puff, or null if there is none.</summary>
    public SpriteFrames DustFrames
    {
        get
        {
            if (!_dustLoaded)
            {
                _dustLoaded = true;
                if (DustFramesPath != "" && ResourceLoader.Exists(DustFramesPath))
                    _dustFrames = GD.Load<SpriteFrames>(DustFramesPath);
            }
            return _dustFrames;
        }
    }
}
