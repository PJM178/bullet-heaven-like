using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Cuts a model into pieces for a break effect, without any hand-made
/// pieces: every triangle is given to one piece, and the triangles of a
/// piece become a mesh of their own.
///
/// Triangles facing up or down (the lid and the floor of a barrel, the top
/// of a crate) go to a top or a bottom piece. All the others are sorted
/// like slices of a cake by the angle of their centre around the upright
/// axis. An eight-sided barrel with 8 slices breaks into its 8 staves.
///
/// Triangles aren't cut in two, so a piece's edge follows the model's own
/// edges. That suits low-poly models: the pieces look like real parts.
///
/// Slow (it walks every triangle and builds new meshes), so it's meant to
/// run once per kind of prop, and the pieces are then shared.
/// </summary>
public static class MeshShatter
{
    /// <summary>One piece: its mesh, with the vertices moved so (0, 0, 0)
    /// is the piece's own centre (so it spins around itself), and where that
    /// centre was in the whole model.</summary>
    public sealed class Piece
    {
        public ArrayMesh Mesh;
        public Vector3 Centre;
    }

    // A triangle counts as facing up or down when its normal is within
    // about 45 degrees of straight up or down (cos 45° = 0.7).
    private const float CapNormalY = 0.7f;

    // The triangles of one piece that use one material, gathered before
    // they are turned into a mesh surface.
    private sealed class SurfaceBuild
    {
        public Material Material;
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<Color> Colors = new();
        public bool HasUvs;
        public bool HasColors;
    }

    /// <summary>
    /// Splits every mesh under <paramref name="model"/> into
    /// <paramref name="slices"/> side pieces plus a top and a bottom piece.
    /// Positions are in the model's own space (its root at the origin).
    /// Pieces that got no triangles are left out.
    /// </summary>
    public static Piece[] Split(Node3D model, int slices)
    {
        slices = Mathf.Max(1, slices);
        int pieceCount = slices + 2;
        int top = slices;
        int bottom = slices + 1;

        // pieces[p] maps a material to the triangles of piece p using it.
        var pieces = new List<SurfaceBuild>[pieceCount];
        for (int p = 0; p < pieceCount; p++)
            pieces[p] = new List<SurfaceBuild>();

        // Every MeshInstance3D below the model, with the transform from its
        // own space to the model's: the model can be a tree of nodes.
        var meshes = new List<MeshInstance3D>();
        CollectMeshes(model, meshes);

        // The model's middle height decides whether a flat triangle is the
        // top or the bottom.
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (MeshInstance3D instance in meshes)
        {
            Aabb box = ToModel(model, instance) * instance.GetAabb();
            minY = Mathf.Min(minY, box.Position.Y);
            maxY = Mathf.Max(maxY, box.End.Y);
        }
        float midY = (minY + maxY) * 0.5f;

        foreach (MeshInstance3D instance in meshes)
        {
            Mesh mesh = instance.Mesh;
            if (mesh == null)
                continue;
            Transform3D toModel = ToModel(model, instance);
            // Normals turn with the model but don't move or stretch like
            // positions do; for the plain rotations and even scales used
            // here, the rotation part (Basis) normalised afterwards is enough.
            Basis normalBasis = toModel.Basis;

            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                // A surface's data is a Godot array with one slot per kind
                // of data (positions, normals, UVs, ...), empty if unused.
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                // An unused slot holds nothing (Nil), and reading it as an
                // array would fail, hence the checks; "? :" picks the empty
                // array then.
                Variant normalSlot = arrays[(int)Mesh.ArrayType.Normal];
                Variant uvSlot = arrays[(int)Mesh.ArrayType.TexUV];
                Variant colorSlot = arrays[(int)Mesh.ArrayType.Color];
                Variant indexSlot = arrays[(int)Mesh.ArrayType.Index];
                Vector3[] normals = IsEmpty(normalSlot) ? new Vector3[0] : normalSlot.AsVector3Array();
                Vector2[] uvs = IsEmpty(uvSlot) ? new Vector2[0] : uvSlot.AsVector2Array();
                Color[] colors = IsEmpty(colorSlot) ? new Color[0] : colorSlot.AsColorArray();
                int[] indices = IsEmpty(indexSlot) ? new int[0] : indexSlot.AsInt32Array();
                // Without an index list every three vertices in a row are a
                // triangle.
                int cornerCount = indices.Length > 0 ? indices.Length : vertices.Length;
                Material material = instance.GetActiveMaterial(s);

                for (int t = 0; t + 2 < cornerCount; t += 3)
                {
                    int a = indices.Length > 0 ? indices[t] : t;
                    int b = indices.Length > 0 ? indices[t + 1] : t + 1;
                    int c = indices.Length > 0 ? indices[t + 2] : t + 2;
                    Vector3 pa = toModel * vertices[a];
                    Vector3 pb = toModel * vertices[b];
                    Vector3 pc = toModel * vertices[c];
                    Vector3 centre = (pa + pb + pc) / 3.0f;
                    // The cross product of two edges points straight out of
                    // the triangle; only its up-down part matters here.
                    Vector3 faceNormal = (pb - pa).Cross(pc - pa).Normalized();

                    int piece;
                    if (Mathf.Abs(faceNormal.Y) > CapNormalY)
                    {
                        piece = centre.Y > midY ? top : bottom;
                    }
                    else
                    {
                        // Atan2 gives the angle around the upright axis,
                        // -Pi..Pi; turn it into 0..1 and then a slice.
                        float turn = (Mathf.Atan2(centre.X, centre.Z) + Mathf.Pi) / Mathf.Tau;
                        piece = Mathf.Clamp((int)(turn * slices), 0, slices - 1);
                    }

                    SurfaceBuild build = FindOrAdd(pieces[piece], material);
                    build.HasUvs |= uvs.Length > 0;
                    build.HasColors |= colors.Length > 0;
                    // The same three corners, in the same order, so the
                    // triangle still faces the same way.
                    AddCorner(build, a, toModel, normalBasis, faceNormal, vertices, normals, uvs, colors);
                    AddCorner(build, b, toModel, normalBasis, faceNormal, vertices, normals, uvs, colors);
                    AddCorner(build, c, toModel, normalBasis, faceNormal, vertices, normals, uvs, colors);
                }
            }
        }

        var result = new List<Piece>();
        foreach (List<SurfaceBuild> surfaces in pieces)
        {
            Piece piece = BuildPiece(surfaces);
            if (piece != null)
                result.Add(piece);
        }
        return result.ToArray();
    }

    /// <summary>Turns the gathered triangles of one piece into a mesh
    /// centred on the piece.</summary>
    private static Piece BuildPiece(List<SurfaceBuild> surfaces)
    {
        // The piece's centre: the average of all its corners.
        Vector3 sum = Vector3.Zero;
        int count = 0;
        foreach (SurfaceBuild build in surfaces)
        {
            foreach (Vector3 v in build.Vertices)
                sum += v;
            count += build.Vertices.Count;
        }
        if (count == 0)
            return null;
        Vector3 centre = sum / count;

        var mesh = new ArrayMesh();
        foreach (SurfaceBuild build in surfaces)
        {
            var vertices = new Vector3[build.Vertices.Count];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = build.Vertices[i] - centre;

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.Normal] = build.Normals.ToArray();
            if (build.HasUvs)
                arrays[(int)Mesh.ArrayType.TexUV] = build.Uvs.ToArray();
            if (build.HasColors)
                arrays[(int)Mesh.ArrayType.Color] = build.Colors.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, build.Material);
        }
        return new Piece { Mesh = mesh, Centre = centre };
    }

    private static SurfaceBuild FindOrAdd(List<SurfaceBuild> surfaces, Material material)
    {
        foreach (SurfaceBuild build in surfaces)
        {
            if (build.Material == material)
                return build;
        }
        var added = new SurfaceBuild { Material = material };
        surfaces.Add(added);
        return added;
    }

    private static void CollectMeshes(Node node, List<MeshInstance3D> found)
    {
        if (node is MeshInstance3D instance)
            found.Add(instance);
        foreach (Node child in node.GetChildren())
            CollectMeshes(child, found);
    }

    /// <summary>The transform from <paramref name="node"/>'s space to
    /// <paramref name="model"/>'s, by multiplying the local transforms on
    /// the way up. (GlobalTransform would also work, but only once the
    /// model is in the scene tree.)</summary>
    private static Transform3D ToModel(Node3D model, Node3D node)
    {
        Transform3D transform = Transform3D.Identity;
        Node current = node;
        while (current != null && current != model)
        {
            if (current is Node3D current3D)
                transform = current3D.Transform * transform;
            current = current.GetParent();
        }
        return transform;
    }

    private static void AddCorner(SurfaceBuild build, int v, Transform3D toModel, Basis normalBasis,
        Vector3 faceNormal, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, Color[] colors)
    {
        build.Vertices.Add(toModel * vertices[v]);
        // A model without normals gets the flat face's own.
        build.Normals.Add(normals.Length > 0 ? (normalBasis * normals[v]).Normalized() : faceNormal);
        build.Uvs.Add(uvs.Length > 0 ? uvs[v] : Vector2.Zero);
        build.Colors.Add(colors.Length > 0 ? colors[v] : Colors.White);
    }

    private static bool IsEmpty(Variant slot)
    {
        return slot.VariantType == Variant.Type.Nil;
    }
}
