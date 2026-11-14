using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// An enclosed space the player can walk into: a house, and later caves,
/// ruins, covered halls. Put this script on an Area3D whose first
/// CollisionShape3D covers the inside; ideally a BoxShape3D (see EdgePieces).
///
/// From the high, angled camera the roof and the near walls would hide the
/// player once they step in. So while the player is inside, this cuts away:
///   - <see cref="AlwaysHide"/>: roofs, ceilings, upper floors.
///   - each child of <see cref="FacingPieces"/> whose outer face (local +Z)
///     points toward the camera: walls.
///   - each child of <see cref="EdgePieces"/> that stands on the camera's
///     side of the centre: posts, pillars, rocks without a clear facing.
///     With a box shape, positions are measured in fractions of the box's
///     half size, so a long room treats all four corners alike.
/// Walls and edge pieces are not hidden outright but cut down to a low stub
/// (<see cref="StubHeight"/>, cutaway_stub.gdshader), so the outline of the
/// room and its doorways stay visible. Roofs are hidden.
/// Only the looks change; collision lives on separate nodes and stays.
///
/// The choice is made again every frame while the player is inside, so it
/// still works if the camera turns. Outside, nothing runs.
/// </summary>
public partial class WalkInSpace : Area3D
{
    [Export] public Node3D[] AlwaysHide = new Node3D[0];

    // Containers, not single pieces: every child is judged on its own. This
    // way a generated building only needs its "Walls" and "Posts" nodes listed.
    [Export] public Node3D[] FacingPieces = new Node3D[0];
    [Export] public Node3D[] EdgePieces = new Node3D[0];

    // How directly a wall must face the camera to be hidden: the dot product
    // of its outer face and the flat camera direction (1 = straight at it,
    // 0 = side on). Slightly above 0 keeps walls seen exactly edge-on.
    [Export(PropertyHint.Range, "-1,1,0.05")] public float FacingThreshold = 0.1f;

    // How far toward the camera from the centre an edge piece must stand to
    // be hidden, in fractions of the box's half size (in metres if the shape
    // isn't a box). Pieces on the dividing line, like the corner posts where
    // a hidden wall meets a shown one, stay and cover the cut edge.
    [Export] public float EdgeThreshold = 0.1f;

    // How much of a cut wall or post stays, in metres above its base.
    // 0 hides the cut pieces completely.
    [Export] public float StubHeight = 0.5f;

    private const string StubShaderPath = "res://props/walk_in/cutaway_stub.gdshader";

    // One wall or post, with the meshes that make up its looks.
    private class Piece
    {
        public Node3D Node;
        public MeshInstance3D[] Meshes;
        public bool Cut;
    }

    private Piece[] _facing;
    private Piece[] _edge;

    // Stub version of each original material, made the first time it is
    // needed. A kit reuses a few materials, so this stays small.
    private readonly Dictionary<Material, ShaderMaterial> _stubMaterials = new();
    private Shader _stubShader;
    private int _playersInside;

    // Middle of the inside and its half size on the ground, in this node's
    // own (local) coordinates. X and Y of the Vector2 are the local X and Z.
    private Vector2 _centre = Vector2.Zero;
    private Vector2 _halfSize = Vector2.One;

    public override void _Ready()
    {
        // Collect the children once, so _Process only walks plain arrays.
        _facing = CollectChildren(FacingPieces);
        _edge = CollectChildren(EdgePieces);
        ReadInsideShape();

        // C# events: += subscribes our method to the Area3D's signal.
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;

        SetProcess(false);
    }

    private void ReadInsideShape()
    {
        foreach (Node child in GetChildren())
        {
            if (child is not CollisionShape3D shape)
                continue;
            _centre = new Vector2(shape.Position.X, shape.Position.Z);
            if (shape.Shape is BoxShape3D box)
                _halfSize = new Vector2(box.Size.X, box.Size.Z) * 0.5f;
            return;
        }
    }

    private static Piece[] CollectChildren(Node3D[] containers)
    {
        var pieces = new List<Piece>();
        foreach (Node3D container in containers)
        {
            if (container == null)
                continue;
            foreach (Node child in container.GetChildren())
            {
                // "is Node3D piece" tests the type and casts in one step.
                if (child is not Node3D node)
                    continue;
                // Every MeshInstance3D below the piece (an imported model
                // has its meshes a level or two down).
                var meshes = new List<MeshInstance3D>();
                foreach (Node found in node.FindChildren("*", nameof(MeshInstance3D), true, false))
                    meshes.Add((MeshInstance3D)found);
                if (node is MeshInstance3D self)
                    meshes.Add(self);
                pieces.Add(new Piece { Node = node, Meshes = meshes.ToArray() });
            }
        }
        return pieces.ToArray();
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is not Player)
            return;
        _playersInside++;
        SetProcess(true);
    }

    private void OnBodyExited(Node3D body)
    {
        if (body is not Player)
            return;
        _playersInside--;
        if (_playersInside > 0)
            return;
        _playersInside = 0;
        SetProcess(false);
        ShowAll();
    }

    public override void _Process(double delta)
    {
        // GetViewport() is the SubViewport the world renders in; its current
        // camera is the game camera.
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;

        // The camera's +Z axis points back out of the screen, toward the
        // camera. Flattened onto the ground, it says which side of the
        // space is the "near" side.
        Vector3 toCamera = camera.GlobalBasis.Z;
        toCamera.Y = 0.0f;
        if (toCamera.LengthSquared() < 0.0001f)
            return; // looking straight down: no near side
        toCamera = toCamera.Normalized();

        foreach (Node3D piece in AlwaysHide)
        {
            if (piece != null)
                piece.Visible = false;
        }

        foreach (Piece piece in _facing)
        {
            Vector3 outward = piece.Node.GlobalBasis.Z;
            outward.Y = 0.0f;
            SetCut(piece, outward.Normalized().Dot(toCamera) > FacingThreshold);
        }

        // Edge pieces are compared in this node's local frame, where the
        // box is axis-aligned. Basis.Inverse() turns a world direction into
        // a local one (the box may be rotated in the level).
        Vector3 localToCamera = GlobalBasis.Inverse() * toCamera;
        var cameraSide = new Vector2(localToCamera.X, localToCamera.Z);
        foreach (Piece piece in _edge)
        {
            Vector3 local = ToLocal(piece.Node.GlobalPosition);
            // Divide by the half size: the box's walls end up at -1 and 1.
            Vector2 offset = (new Vector2(local.X, local.Z) - _centre) / _halfSize;
            SetCut(piece, offset.Dot(cameraSide) > EdgeThreshold);
        }
    }

    // Cuts a piece down to its stub, or puts it back. Only does work when
    // the state changes, not every frame.
    private void SetCut(Piece piece, bool cut)
    {
        if (piece.Cut == cut)
            return;
        piece.Cut = cut;

        if (StubHeight <= 0.0f)
        {
            piece.Node.Visible = !cut;
            return;
        }

        foreach (MeshInstance3D mesh in piece.Meshes)
        {
            if (mesh.Mesh == null)
                continue;
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            {
                // A surface override replaces the mesh's own material on
                // this one instance only; null goes back to the original.
                Material stub = cut ? StubFor(mesh.Mesh.SurfaceGetMaterial(i)) : null;
                mesh.SetSurfaceOverrideMaterial(i, stub);
            }
        }
    }

    private ShaderMaterial StubFor(Material original)
    {
        // TryGetValue looks the key up and hands back the value via "out".
        if (original != null && _stubMaterials.TryGetValue(original, out ShaderMaterial cached))
            return cached;

        _stubShader ??= GD.Load<Shader>(StubShaderPath);
        var stub = new ShaderMaterial { Shader = _stubShader };
        stub.SetShaderParameter("stub_height", StubHeight);
        // Copy the look of a standard material; anything else gets a plain
        // grey stub, which still shows the outline.
        if (original is BaseMaterial3D standard)
        {
            stub.SetShaderParameter("albedo_texture", standard.AlbedoTexture);
            stub.SetShaderParameter("albedo_color", standard.AlbedoColor);
        }
        else
        {
            stub.SetShaderParameter("albedo_color", new Color(0.5f, 0.5f, 0.5f));
        }

        if (original != null)
            _stubMaterials[original] = stub;
        return stub;
    }

    private void ShowAll()
    {
        foreach (Node3D piece in AlwaysHide)
        {
            if (piece != null)
                piece.Visible = true;
        }
        foreach (Piece piece in _facing)
            SetCut(piece, false);
        foreach (Piece piece in _edge)
            SetCut(piece, false);
    }
}
