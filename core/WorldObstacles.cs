using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Everything solid that enemies can't walk through (walls, barrels), kept
/// as simple shapes on the ground: circles and boxes turned about the
/// vertical axis. Height is ignored. Shapes marked BlocksAttacks (walls)
/// also stop attacks, projectiles and enemy contact through them
/// (<see cref="IsLineBlocked"/>).
///
/// Enemies are not physics bodies (see EnemyManager), so Godot's physics
/// can't stop them. Instead, after moving, each enemy asks
/// <see cref="PushOut"/> to move it back out of any shape it walked into.
/// That's a handful of sums per enemy, far cheaper than a physics body each.
///
/// Shapes are added when a level or a prop appears and removed when it goes
/// (<see cref="AddCircle"/>, <see cref="AddBox"/>, <see cref="Remove"/>).
/// Scenes usually don't call these themselves: an <see cref="ObstacleBlocker"/>
/// under a StaticBody3D registers that body's shapes.
///
/// Shapes are sorted into a coarse grid, so an enemy only looks at the few
/// shapes near it. Later, a flow field for pathfinding can read the same
/// shapes to know which ground is blocked.
/// </summary>
public partial class WorldObstacles : Node
{
    /// <summary>The group this node is in, so blockers can find it.</summary>
    public const string Group = "world_obstacles";

    // Width of a grid cell in metres. Bigger cells hold more shapes each;
    // smaller ones put a long wall into more cells. Read once in _Ready.
    [Export] public float CellSize = 2.0f;

    // One shape. A struct is a plain bundle of values (copied, not shared),
    // which suits small data kept in a list.
    private struct Obstacle
    {
        public bool Alive;
        public bool IsBox;
        public bool BlocksAttacks; // stops attacks and sight (see IsLineBlocked)
        public Vector2 Centre;    // on the ground: X and Z
        public float Radius;      // circles
        public Vector2 AxisX;     // boxes: their local X and Z directions
        public Vector2 AxisZ;
        public Vector2 HalfSize;  // boxes: half width along AxisX and AxisZ
        public Vector2I CellMin;  // the cells it was filed in, to unfile it
        public Vector2I CellMax;
    }

    // Index in this list = the id handed out. Ids are never reused: a level
    // has few obstacles and they rarely change.
    private readonly List<Obstacle> _obstacles = new();

    // Cell -> ids of the shapes that reach into it. Only cells with shapes
    // have an entry. Vector2I as a key allocates nothing on lookup.
    private readonly Dictionary<Vector2I, List<int>> _cells = new();

    public override void _EnterTree()
    {
        // Before any _Ready, so blockers in the level always find us.
        AddToGroup(Group);
    }

    /// <summary>
    /// Adds a circle (a barrel, a pillar). Returns its id.
    /// <paramref name="blocksAttacks"/>: whether it also stops attacks and
    /// contact through it (see <see cref="IsLineBlocked"/>). Low props that
    /// are hit themselves, like barrels, don't.
    /// </summary>
    public int AddCircle(Vector3 centre, float radius, bool blocksAttacks)
    {
        var obstacle = new Obstacle
        {
            Alive = true,
            BlocksAttacks = blocksAttacks,
            Centre = new Vector2(centre.X, centre.Z),
            Radius = radius,
        };
        return File(obstacle, radius);
    }

    /// <summary>
    /// Adds a box. <paramref name="transform"/> is the box centre's global
    /// transform (only its turn about the vertical axis and its scale are
    /// used), <paramref name="size"/> the box's full size before scaling,
    /// like BoxShape3D.Size. Returns its id. <paramref name="blocksAttacks"/>
    /// as in <see cref="AddCircle"/>.
    /// </summary>
    public int AddBox(Transform3D transform, Vector3 size, bool blocksAttacks)
    {
        Basis basis = transform.Basis;
        // A basis column's length is the scale along that axis.
        Vector2 axisX = new Vector2(basis.X.X, basis.X.Z);
        Vector2 axisZ = new Vector2(basis.Z.X, basis.Z.Z);
        var obstacle = new Obstacle
        {
            Alive = true,
            IsBox = true,
            BlocksAttacks = blocksAttacks,
            Centre = new Vector2(transform.Origin.X, transform.Origin.Z),
            AxisX = axisX.Normalized(),
            AxisZ = axisZ.Normalized(),
            HalfSize = new Vector2(size.X * axisX.Length(), size.Z * axisZ.Length()) * 0.5f,
        };
        // Half the diagonal: the box fits inside a circle this big.
        return File(obstacle, obstacle.HalfSize.Length());
    }

    /// <summary>Takes a shape out again (a broken barrel). Ignores -1 and
    /// ids already removed.</summary>
    public void Remove(int id)
    {
        if (id < 0 || id >= _obstacles.Count || !_obstacles[id].Alive)
            return;

        Obstacle obstacle = _obstacles[id];
        for (int z = obstacle.CellMin.Y; z <= obstacle.CellMax.Y; z++)
        {
            for (int x = obstacle.CellMin.X; x <= obstacle.CellMax.X; x++)
            {
                if (_cells.TryGetValue(new Vector2I(x, z), out List<int> ids))
                    ids.Remove(id);
            }
        }
        // A struct in a list is a copy: change it, then store it back.
        obstacle.Alive = false;
        _obstacles[id] = obstacle;
    }

    // Stores the shape and files it in every cell its bounding circle
    // reaches.
    private int File(Obstacle obstacle, float boundingRadius)
    {
        int id = _obstacles.Count;
        float reach = boundingRadius;
        obstacle.CellMin = CellOf(obstacle.Centre - new Vector2(reach, reach));
        obstacle.CellMax = CellOf(obstacle.Centre + new Vector2(reach, reach));
        _obstacles.Add(obstacle);

        for (int z = obstacle.CellMin.Y; z <= obstacle.CellMax.Y; z++)
        {
            for (int x = obstacle.CellMin.X; x <= obstacle.CellMax.X; x++)
            {
                var cell = new Vector2I(x, z);
                if (!_cells.TryGetValue(cell, out List<int> ids))
                {
                    ids = new List<int>();
                    _cells[cell] = ids;
                }
                ids.Add(id);
            }
        }
        return id;
    }

    private Vector2I CellOf(Vector2 point)
    {
        return new Vector2I(Mathf.FloorToInt(point.X / CellSize), Mathf.FloorToInt(point.Y / CellSize));
    }

    /// <summary>
    /// Moves a circle of <paramref name="radius"/> at
    /// <paramref name="position"/> (on the ground) out of every shape it
    /// overlaps and returns where it ends up. Y is passed through untouched.
    /// Any radius works: the caller (each enemy's definition) decides.
    /// Allocates nothing, so it's fine to call for every enemy every step.
    /// </summary>
    public Vector3 PushOut(Vector3 position, float radius)
    {
        Vector2 point = new Vector2(position.X, position.Z);

        // Every cell the circle overlaps: usually one, up to four when it
        // sits on a corner (more only for circles wider than a cell).
        Vector2I min = CellOf(point - new Vector2(radius, radius));
        Vector2I max = CellOf(point + new Vector2(radius, radius));
        for (int z = min.Y; z <= max.Y; z++)
        {
            for (int x = min.X; x <= max.X; x++)
            {
                if (!_cells.TryGetValue(new Vector2I(x, z), out List<int> ids))
                    continue; // nothing solid here: the usual case

                // A shape in two of these cells is checked twice. That's
                // harmless: after the first push the circle is already
                // outside it, so the second does nothing.
                // A for loop over the list (not foreach) is guaranteed not
                // to allocate an enumerator.
                for (int n = 0; n < ids.Count; n++)
                {
                    Obstacle obstacle = _obstacles[ids[n]];
                    point = obstacle.IsBox
                        ? PushOutOfBox(point, radius, obstacle)
                        : PushOutOfCircle(point, radius, obstacle);
                }
            }
        }

        return new Vector3(point.X, position.Y, point.Y);
    }

    /// <summary>
    /// Whether a straight line on the ground from <paramref name="from"/> to
    /// <paramref name="to"/> crosses a shape that blocks attacks (a wall).
    /// Used for "can this attack or touch reach that": projectiles between
    /// steps, melee and area hits from their centre, enemy contact, aiming.
    /// Height is ignored. Allocates nothing.
    /// </summary>
    public bool IsLineBlocked(Vector3 from, Vector3 to)
    {
        // Any wall on the line will do, so stop at the first one found.
        return FirstBlock(from, to, stopAtAny: true) <= 1.0f;
    }

    /// <summary>
    /// The line from <paramref name="from"/> to <paramref name="to"/>, cut
    /// short where it first meets a wall: the furthest point along it that
    /// is still on the near side, <paramref name="margin"/> metres back from
    /// the wall. <paramref name="to"/> itself when nothing is in the way.
    /// For placing something "ahead of the player" that mustn't end up on
    /// the far side of a wall (see WeaponTargeting.SpotAhead).
    /// </summary>
    public Vector3 ClipLine(Vector3 from, Vector3 to, float margin)
    {
        float t = FirstBlock(from, to, stopAtAny: false);
        if (t > 1.0f)
            return to;

        Vector3 line = to - from;
        float length = line.Length();
        // Step back from the wall by the margin, but never behind the start.
        float distance = Mathf.Max(t * length - margin, 0.0f);
        return length > 0.0001f ? from + line / length * distance : from;
    }

    // How far along the line (0 = from, 1 = to) it first enters a shape
    // that blocks attacks; more than 1 when it never does. With stopAtAny
    // it returns as soon as any shape is found, not the nearest one.
    private float FirstBlock(Vector3 from, Vector3 to, bool stopAtAny)
    {
        var start = new Vector2(from.X, from.Z);
        var end = new Vector2(to.X, to.Z);
        float first = NoBlock;

        // Every cell in the rectangle around the line. Lines are short
        // (attack ranges), so that's a handful of cells. A shape in several
        // cells may be tested more than once; that only costs time.
        Vector2I min = CellOf(new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y)));
        Vector2I max = CellOf(new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y)));
        for (int z = min.Y; z <= max.Y; z++)
        {
            for (int x = min.X; x <= max.X; x++)
            {
                if (!_cells.TryGetValue(new Vector2I(x, z), out List<int> ids))
                    continue;
                for (int n = 0; n < ids.Count; n++)
                {
                    Obstacle obstacle = _obstacles[ids[n]];
                    if (!obstacle.BlocksAttacks)
                        continue;
                    float t = obstacle.IsBox
                        ? LineEntersBox(start, end, obstacle)
                        : LineEntersCircle(start, end, obstacle);
                    if (t < first)
                    {
                        first = t;
                        if (stopAtAny)
                            return first;
                    }
                }
            }
        }
        return first;
    }

    // "Never": any value above 1 means the line misses.
    private const float NoBlock = 2.0f;

    // Where the line first enters the circle (0..1), or NoBlock.
    private static float LineEntersCircle(Vector2 start, Vector2 end, Obstacle obstacle)
    {
        // Points on the line are start + line * t. Setting their distance
        // from the centre equal to the radius gives a quadratic in t
        // (a t^2 + b t + c = 0); its smaller root is where the line enters.
        Vector2 line = end - start;
        Vector2 fromCentre = start - obstacle.Centre;
        float c = fromCentre.LengthSquared() - obstacle.Radius * obstacle.Radius;
        if (c < 0.0f)
            return 0.0f; // starts inside it
        float a = line.LengthSquared();
        if (a < 0.000001f)
            return NoBlock; // a point outside the circle
        float b = 2.0f * fromCentre.Dot(line);
        float discriminant = b * b - 4.0f * a * c;
        if (discriminant < 0.0f)
            return NoBlock; // passes beside it
        float t = (-b - Mathf.Sqrt(discriminant)) / (2.0f * a);
        return t >= 0.0f && t <= 1.0f ? t : NoBlock;
    }

    // Where the line first enters the box (0..1), or NoBlock.
    private static float LineEntersBox(Vector2 start, Vector2 end, Obstacle obstacle)
    {
        // In the box's own frame the box is a plain rectangle. Clip the line
        // against its two pairs of sides ("slab test"): tEnter and tExit
        // close in on the part of the line (0..1) that is inside both
        // pairs. If anything is left, the line passes through the box,
        // entering at tEnter.
        Vector2 a = start - obstacle.Centre;
        Vector2 b = end - obstacle.Centre;
        float startX = a.Dot(obstacle.AxisX), startZ = a.Dot(obstacle.AxisZ);
        float stepX = b.Dot(obstacle.AxisX) - startX, stepZ = b.Dot(obstacle.AxisZ) - startZ;

        float tEnter = 0.0f, tExit = 1.0f;
        bool crosses = ClipSlab(startX, stepX, obstacle.HalfSize.X, ref tEnter, ref tExit)
            && ClipSlab(startZ, stepZ, obstacle.HalfSize.Y, ref tEnter, ref tExit);
        return crosses ? tEnter : NoBlock;
    }

    // Narrows tEnter..tExit to where start + step * t lies between -half and
    // +half on one axis. False when no part of the line does. "ref" lets
    // the method change the caller's variables.
    private static bool ClipSlab(float start, float step, float half, ref float tEnter, ref float tExit)
    {
        if (Mathf.Abs(step) < 0.000001f)
            return Mathf.Abs(start) <= half; // parallel: inside the slab or never

        float t1 = (-half - start) / step;
        float t2 = (half - start) / step;
        if (t1 > t2)
            (t1, t2) = (t2, t1); // swap, so t1 is where it enters
        tEnter = Mathf.Max(tEnter, t1);
        tExit = Mathf.Min(tExit, t2);
        return tEnter <= tExit;
    }

    private static Vector2 PushOutOfCircle(Vector2 point, float radius, Obstacle obstacle)
    {
        Vector2 away = point - obstacle.Centre;
        float minDistance = radius + obstacle.Radius;
        float distanceSquared = away.LengthSquared();
        if (distanceSquared >= minDistance * minDistance)
            return point;
        if (distanceSquared < 0.000001f)
            away = Vector2.Right; // dead centre: any way out will do
        return obstacle.Centre + away.Normalized() * minDistance;
    }

    private static Vector2 PushOutOfBox(Vector2 point, float radius, Obstacle obstacle)
    {
        // Work in the box's own frame, where it is a plain rectangle from
        // -HalfSize to +HalfSize: dot products give the point's coordinates
        // along the box's two axes.
        Vector2 offset = point - obstacle.Centre;
        float x = offset.Dot(obstacle.AxisX);
        float z = offset.Dot(obstacle.AxisZ);
        Vector2 half = obstacle.HalfSize;

        if (Mathf.Abs(x) < half.X && Mathf.Abs(z) < half.Y)
        {
            // Centre inside the box: leave through the nearest side.
            if (half.X - Mathf.Abs(x) < half.Y - Mathf.Abs(z))
                x = (x < 0.0f ? -1.0f : 1.0f) * (half.X + radius);
            else
                z = (z < 0.0f ? -1.0f : 1.0f) * (half.Y + radius);
        }
        else
        {
            // Outside: find the box's closest point and keep at least
            // radius away from it.
            float closestX = Mathf.Clamp(x, -half.X, half.X);
            float closestZ = Mathf.Clamp(z, -half.Y, half.Y);
            var away = new Vector2(x - closestX, z - closestZ);
            float distanceSquared = away.LengthSquared();
            if (distanceSquared >= radius * radius)
                return point;
            away = away / Mathf.Sqrt(distanceSquared) * radius;
            x = closestX + away.X;
            z = closestZ + away.Y;
        }

        // Back from the box's frame to the world.
        return obstacle.Centre + obstacle.AxisX * x + obstacle.AxisZ * z;
    }
}
