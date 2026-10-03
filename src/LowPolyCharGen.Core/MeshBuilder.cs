using System.Numerics;

namespace LowPolyCharGen;

public readonly record struct Influence(int Bone, float Weight);

/// <summary>Returns the skin weights for a vertex at the given bind-pose position.</summary>
public delegate Influence[] WeightFn(Vector3 position);

/// <summary>
/// A rectangle of the texture atlas. Charts are measured in centimetres on the model so the packer
/// can give every surface the same texel density (times <see cref="Density"/>).
/// </summary>
public sealed class UvChart
{
    public float Width { get; init; }
    public float Height { get; init; }
    public float Density { get; init; } = 1f;

    // Set by the packer: origin and texels per centimetre in the virtual atlas.
    public float X { get; internal set; }
    public float Y { get; internal set; }
    public float Scale { get; internal set; }
}

/// <summary>A triangle or polygon (counter-clockwise seen from outside).</summary>
public sealed class Face(int[] vertices, Slot slot, PaintDetail detail, int chart, Vector2[] chartUv, byte smoothGroup = 0)
{
    public int[] Vertices { get; } = vertices;
    public Slot Slot { get; } = slot;
    public PaintDetail Detail { get; } = detail;

    /// <summary>Faces in different groups never share normals: the edge between them is always hard.</summary>
    public byte SmoothGroup { get; } = smoothGroup;

    /// <summary>Index of the face's <see cref="UvChart"/>.</summary>
    public int Chart { get; internal set; } = chart;

    /// <summary>Corner positions inside the chart, in centimetres.</summary>
    public Vector2[] ChartUv { get; } = chartUv;
}

/// <summary>Finished, skinned, UV-mapped polygon mesh.</summary>
public sealed class MeshData
{
    /// <summary>Side length of the virtual atlas the charts are packed into.</summary>
    public const int AtlasSize = 2048;

    public required Vector3[] Positions { get; init; }
    public required Influence[][] Weights { get; init; }
    public required Face[] Faces { get; init; }
    public required UvChart[] Charts { get; init; }

    /// <summary>One normal per face corner, parallel to <see cref="Faces"/>.</summary>
    public required Vector3[][] Normals { get; init; }

    /// <summary>One texture coordinate per face corner (0..1, V up), parallel to <see cref="Faces"/>.</summary>
    public required Vector2[][] Uvs { get; init; }

    public int TriangleCount => Faces.Sum(f => f.Vertices.Length - 2);

    /// <summary>
    /// Corner normals: face normals averaged over the faces around a vertex that are within the crease
    /// angle of each other. A crease angle of 0 gives fully faceted shading.
    /// </summary>
    public static Vector3[][] ComputeNormals(IReadOnlyList<Vector3> positions, IReadOnlyList<Face> faces, float creaseDegrees)
    {
        var faceNormals = new Vector3[faces.Count];   // area weighted
        var unitNormals = new Vector3[faces.Count];
        var perVertex = new List<int>?[positions.Count];
        for (var f = 0; f < faces.Count; f++)
        {
            var v = faces[f].Vertices;
            var n = Vector3.Zero;
            for (var i = 0; i < v.Length; i++)   // Newell's method
                n += Vector3.Cross(positions[v[i]], positions[v[(i + 1) % v.Length]]);
            faceNormals[f] = n;
            unitNormals[f] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ;
            foreach (var vi in v) (perVertex[vi] ??= []).Add(f);
        }

        var cos = MathF.Cos(Geo.Deg(creaseDegrees));
        var result = new Vector3[faces.Count][];
        for (var f = 0; f < faces.Count; f++)
        {
            var v = faces[f].Vertices;
            var corner = new Vector3[v.Length];
            for (var i = 0; i < v.Length; i++)
            {
                var sum = Vector3.Zero;
                if (creaseDegrees > 0)
                    foreach (var g in perVertex[v[i]]!)
                        if (faces[g].SmoothGroup == faces[f].SmoothGroup && Vector3.Dot(unitNormals[f], unitNormals[g]) >= cos)
                            sum += faceNormals[g];
                corner[i] = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : unitNormals[f];
            }
            result[f] = corner;
        }
        return result;
    }
}

public enum Cap { None, Flat }

/// <summary>
/// Accumulates skinned low-poly primitives into one mesh. Every primitive also lays out its own UV
/// charts; the charts are packed into a single atlas when the mesh is built.
/// </summary>
public sealed class MeshBuilder(Skeleton skeleton)
{
    public Skeleton Skeleton { get; } = skeleton;
    public List<Vector3> Positions { get; } = [];
    public List<Influence[]> Weights { get; } = [];
    public List<Face> Faces { get; } = [];
    public List<UvChart> Charts { get; } = [];

    /// <summary>Texel density multiplier for the charts created from now on (faces and hands want more).</summary>
    public float Density { get; set; } = 1f;

    /// <summary>Detail the painter adds to the faces created from now on.</summary>
    public PaintDetail Detail { get; set; }

    /// <summary>Smoothing group of the faces created from now on; edges between groups are hard.</summary>
    public byte SmoothGroup { get; set; }

    public int AddVertex(Vector3 position, WeightFn weights)
    {
        Positions.Add(position);
        Weights.Add(weights(position));
        return Positions.Count - 1;
    }

    // ---- charts and faces ----------------------------------------------------------------

    private int NewChart(float width, float height)
    {
        Charts.Add(new UvChart { Width = MathF.Max(width, 0.3f), Height = MathF.Max(height, 0.3f), Density = Density });
        return Charts.Count - 1;
    }

    /// <summary>Adds a face, collapsing repeated vertices; faces with fewer than three corners are dropped.</summary>
    private void Emit(Slot slot, int chart, bool flip, ReadOnlySpan<int> v, ReadOnlySpan<Vector2> uv)
    {
        var vs = new List<int>(v.Length);
        var us = new List<Vector2>(v.Length);
        for (var i = 0; i < v.Length; i++)
        {
            if (vs.Contains(v[i])) continue;
            vs.Add(v[i]);
            us.Add(uv[i]);
        }
        if (vs.Count < 3) return;
        if (flip)
        {
            vs.Reverse();
            us.Reverse();
        }
        Faces.Add(new Face(vs.ToArray(), slot, Detail, chart, us.ToArray(), SmoothGroup));
    }

    private Vector3 PolygonNormal(ReadOnlySpan<int> v)
    {
        var n = Vector3.Zero;
        for (var i = 0; i < v.Length; i++)
            n += Vector3.Cross(Positions[v[i]], Positions[v[(i + 1) % v.Length]]);
        return n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ;
    }

    /// <summary>Flattens points onto the plane with the given normal and returns them relative to their bounding box.</summary>
    private (Vector2[] Uv, float Width, float Height) Flatten(ReadOnlySpan<int> v, Vector3 normal)
    {
        var e1 = Vector3.Cross(normal, MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
        e1 = Vector3.Normalize(e1);
        var e2 = Vector3.Cross(normal, e1);
        var uv = new Vector2[v.Length];
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        for (var i = 0; i < v.Length; i++)
        {
            var p = Positions[v[i]];
            uv[i] = new Vector2(Vector3.Dot(p, e1), Vector3.Dot(p, e2));
            min = Vector2.Min(min, uv[i]);
            max = Vector2.Max(max, uv[i]);
        }
        for (var i = 0; i < uv.Length; i++) uv[i] -= min;
        return (uv, max.X - min.X, max.Y - min.Y);
    }

    /// <summary>Adds a counter-clockwise polygon with its own flat chart.</summary>
    public void AddPolygon(Slot slot, params int[] v)
    {
        var (uv, w, h) = Flatten(v, PolygonNormal(v));
        Emit(slot, NewChart(w, h), false, v, uv);
    }

    /// <summary>Adds a polygon wound so that it faces away from <paramref name="inside"/>.</summary>
    public void AddPolygonOutward(Slot slot, Vector3 inside, params int[] v)
    {
        var centre = Vector3.Zero;
        foreach (var i in v) centre += Positions[i];
        centre /= v.Length;
        if (Vector3.Dot(PolygonNormal(v), centre - inside) < 0) Array.Reverse(v);
        AddPolygon(slot, v);
    }

    /// <summary>
    /// Adds the quads of a vertex grid (rows of equal length) and unwraps them into one rectangular
    /// chart. Repeated vertices are allowed: the affected quads become triangles or vanish.
    /// </summary>
    /// <param name="closed">True if the last column joins the first.</param>
    /// <param name="skip">Quads (row, column) to leave out, for holes.</param>
    public void AddGridFaces(int[][] grid, bool closed, SlotSpec slots, bool flip, Func<int, int, bool>? skip = null)
    {
        var n = grid[0].Length;
        var columns = closed ? n : n - 1;
        var u = new float[columns + 1];
        var v = new float[grid.Length];
        for (var c = 0; c < columns; c++)
        {
            var sum = 0f;
            foreach (var row in grid) sum += Vector3.Distance(Positions[row[c]], Positions[row[(c + 1) % n]]);
            u[c + 1] = u[c] + MathF.Max(0.2f, sum / grid.Length);
        }
        for (var r = 0; r + 1 < grid.Length; r++)
        {
            var sum = 0f;
            for (var c = 0; c < n; c++) sum += Vector3.Distance(Positions[grid[r][c]], Positions[grid[r + 1][c]]);
            v[r + 1] = v[r] + MathF.Max(0.2f, sum / n);
        }

        var chart = NewChart(u[columns], v[^1]);
        Span<int> quad = stackalloc int[4];
        Span<Vector2> uv = stackalloc Vector2[4];
        for (var r = 0; r + 1 < grid.Length; r++)
            for (var c = 0; c < columns; c++)
            {
                if (skip?.Invoke(r, c) == true) continue;
                var c2 = (c + 1) % n;
                quad[0] = grid[r][c]; quad[1] = grid[r][c2]; quad[2] = grid[r + 1][c2]; quad[3] = grid[r + 1][c];
                uv[0] = new Vector2(u[c], v[r]); uv[1] = new Vector2(u[c + 1], v[r]);
                uv[2] = new Vector2(u[c + 1], v[r + 1]); uv[3] = new Vector2(u[c], v[r + 1]);
                Emit(slots[r], chart, flip, quad, uv);
            }
    }

    /// <summary>As <see cref="AddGridFaces"/>, wound so that the surface faces away from <paramref name="inside"/>.</summary>
    public void AddGridFacesOutward(int[][] grid, bool closed, SlotSpec slots, Vector3 inside, Func<int, int, bool>? skip = null)
    {
        var n = grid[0].Length;
        var columns = closed ? n : n - 1;
        var score = 0f;
        for (var r = 0; r + 1 < grid.Length; r++)
            for (var c = 0; c < columns; c++)
            {
                var a = Positions[grid[r][c]];
                var b = Positions[grid[r][(c + 1) % n]];
                var cc = Positions[grid[r + 1][(c + 1) % n]];
                var d = Positions[grid[r + 1][c]];
                var normal = Vector3.Cross(cc - a, d - b);   // twice the quad's area vector
                score += Vector3.Dot(normal, (a + b + cc + d) / 4 - inside);
            }
        AddGridFaces(grid, closed, slots, score < 0, skip);
    }

    /// <summary>Triangle fan from a centre vertex to a loop (or open strip) of vertices, in one flat chart.</summary>
    public void AddFan(int center, int[] ring, bool closed, Slot slot, bool reverse)
    {
        var all = new int[ring.Length + 1];
        ring.CopyTo(all, 0);
        all[^1] = center;
        var (uv, w, h) = Flatten(all, PolygonNormal(ring));
        var chart = NewChart(w, h);
        var count = closed ? ring.Length : ring.Length - 1;
        Span<int> tri = stackalloc int[3];
        Span<Vector2> tuv = stackalloc Vector2[3];
        for (var i = 0; i < count; i++)
        {
            var j = (i + 1) % ring.Length;
            tri[0] = center; tri[1] = ring[i]; tri[2] = ring[j];
            tuv[0] = uv[^1]; tuv[1] = uv[i]; tuv[2] = uv[j];
            Emit(slot, chart, reverse, tri, tuv);
        }
    }

    // ---- primitives ----------------------------------------------------------------------

    /// <summary>
    /// Skins a stack of point rings. Rings closed around their loop are oriented automatically
    /// (outward) from the signed volume; open strips use <paramref name="flip"/>.
    /// </summary>
    /// <param name="rings">Equal-length point loops, in order along the stack.</param>
    /// <param name="slots">Slot per segment (ring i to ring i+1); caps use the adjacent segment's slot.</param>
    /// <param name="startApex">Fan centre closing the first ring, or null to leave it open.</param>
    /// <param name="endApex">Fan centre closing the last ring, or null to leave it open.</param>
    /// <param name="closed">False for an open strip (first and last point of a ring are not joined).</param>
    /// <returns>The vertex indices of the rings.</returns>
    public int[][] AddRingStack(IReadOnlyList<Vector3[]> rings, SlotSpec slots, WeightFn weights,
        Vector3? startApex = null, Vector3? endApex = null, bool closed = true, bool flip = false)
    {
        if (closed) flip = SignedVolume(rings) < 0;

        var idx = new int[rings.Count][];
        for (var r = 0; r < rings.Count; r++)
            idx[r] = Array.ConvertAll(rings[r], p => AddVertex(p, weights));

        if (rings.Count > 1) AddGridFaces(idx, closed, slots, flip);
        if (startApex is { } s) AddFan(AddVertex(s, weights), idx[0], closed, slots[0], !flip);
        if (endApex is { } e) AddFan(AddVertex(e, weights), idx[^1], closed, slots[rings.Count - 2], flip);
        return idx;
    }

    public static Vector3 Centroid(Vector3[] ring)
    {
        var c = Vector3.Zero;
        foreach (var p in ring) c += p;
        return c / ring.Length;
    }

    /// <summary>Volume of the stack with both ends capped, for the default (unflipped) winding.</summary>
    private static float SignedVolume(IReadOnlyList<Vector3[]> rings)
    {
        var n = rings[0].Length;
        var o = Centroid(rings[0]);
        float Tri(Vector3 a, Vector3 b, Vector3 c) => Vector3.Dot(a - o, Vector3.Cross(b - o, c - o));

        var vol = 0f;
        for (var r = 0; r + 1 < rings.Count; r++)
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                vol += Tri(rings[r][i], rings[r][j], rings[r + 1][j]) + Tri(rings[r][i], rings[r + 1][j], rings[r + 1][i]);
            }
        var end = rings[^1];
        var ce = Centroid(end);
        for (var i = 0; i < n; i++)   // the start cap contributes nothing: it contains the origin
            vol += Tri(ce, end[i], end[(i + 1) % n]);
        return vol;
    }

    /// <summary>Lofts superellipse cross-sections into a tube.</summary>
    /// <param name="startTip">Distance the start cap's centre is pushed out along the stack (0 = flat).</param>
    public int[][] AddLoft(IReadOnlyList<Ring> rings, int sides, float angleOffset, SlotSpec slots, WeightFn weights,
        Cap start = Cap.Flat, Cap end = Cap.Flat, float startTip = 0, float endTip = 0)
    {
        var pts = rings.Select(r => r.Points(sides, angleOffset)).ToList();
        Vector3? s = null, e = null;
        if (start == Cap.Flat)
        {
            var dir = rings.Count > 1 ? rings[0].Center - rings[1].Center : Vector3.Zero;
            s = Centroid(pts[0]) + (dir.LengthSquared() > 1e-10f ? Vector3.Normalize(dir) * startTip : Vector3.Zero);
        }
        if (end == Cap.Flat)
        {
            var dir = rings.Count > 1 ? rings[^1].Center - rings[^2].Center : Vector3.Zero;
            e = Centroid(pts[^1]) + (dir.LengthSquared() > 1e-10f ? Vector3.Normalize(dir) * endTip : Vector3.Zero);
        }
        return AddRingStack(pts, slots, weights, s, e);
    }

    /// <summary>
    /// Tube along a path that lies in a plane perpendicular to <paramref name="uAxis"/>.
    /// With 4 sides and a 45 degree offset this is a flat strap (radii = half extents * sqrt 2).
    /// </summary>
    public void AddPathLoft(IReadOnlyList<Vector3> path, Func<int, (float Ru, float Rv)> radii, Vector3 uAxis,
        int sides, float angleOffset, SlotSpec slots, WeightFn weights, float exp = 2f,
        Cap start = Cap.Flat, Cap end = Cap.Flat, float endTip = 0)
    {
        var rings = new List<Ring>(path.Count);
        for (var i = 0; i < path.Count; i++)
        {
            var tangent = Vector3.Normalize(path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)]);
            var v = Vector3.Normalize(Vector3.Cross(tangent, uAxis));
            var (ru, rv) = radii(i);
            rings.Add(new Ring(path[i], uAxis, v, ru, rv, exp));
        }
        AddLoft(rings, sides, angleOffset, slots, weights, start, end, 0, endTip);
    }

    /// <summary>
    /// Box with half-extents <paramref name="half"/>. <paramref name="taper"/> scales the +Z face in
    /// local X and Y (1 = straight box). On the four sides the chart's V axis runs along local Z.
    /// </summary>
    public void AddBox(Vector3 center, Vector3 half, Slot slot, WeightFn weights,
        Quaternion? rotation = null, Vector2? taper = null)
    {
        var rot = rotation ?? Quaternion.Identity;
        var t = taper ?? Vector2.One;
        Span<int> v = stackalloc int[8];
        for (var i = 0; i < 8; i++)
        {
            var top = i >= 4;
            var sx = (i & 3) is 1 or 2 ? 1f : -1f;
            var sy = (i & 3) is 2 or 3 ? 1f : -1f;
            var local = new Vector3(sx * half.X * (top ? t.X : 1), sy * half.Y * (top ? t.Y : 1), top ? half.Z : -half.Z);
            v[i] = AddVertex(center + Vector3.Transform(local, rot), weights);
        }
        Span<int> quad = stackalloc int[4];
        Span<Vector2> uv = stackalloc Vector2[4];
        void Side(int a, int b, int c, int d, float width, float height, Span<int> quad, Span<Vector2> uv)
        {
            quad[0] = a; quad[1] = b; quad[2] = c; quad[3] = d;
            uv[0] = Vector2.Zero; uv[1] = new Vector2(width, 0); uv[2] = new Vector2(width, height); uv[3] = new Vector2(0, height);
            Emit(slot, NewChart(width, height), false, quad, uv);
        }
        Side(v[0], v[3], v[2], v[1], half.Y * 2, half.X * 2, quad, uv);   // bottom
        Side(v[4], v[5], v[6], v[7], half.X * 2, half.Y * 2, quad, uv);   // top
        Side(v[0], v[1], v[5], v[4], half.X * 2, half.Z * 2, quad, uv);   // -Y
        Side(v[1], v[2], v[6], v[5], half.Y * 2, half.Z * 2, quad, uv);   // +X
        Side(v[2], v[3], v[7], v[6], half.X * 2, half.Z * 2, quad, uv);   // +Y
        Side(v[3], v[0], v[4], v[7], half.Y * 2, half.Z * 2, quad, uv);   // -X
    }

    /// <summary>Box stretched between two points; <paramref name="sideHint"/> orients its width.</summary>
    public void AddBeam(Vector3 from, Vector3 to, float halfWidth, float halfThickness, Vector3 sideHint,
        Slot slot, WeightFn weights, Vector2? taper = null)
    {
        var axis = to - from;
        var length = axis.Length();
        if (length < 1e-4f) return;
        AddBox((from + to) / 2, new Vector3(halfWidth, halfThickness, length / 2), slot, weights,
            Geo.LookAlong(axis, sideHint), taper);
    }

    /// <summary>
    /// Dome or full ellipsoid whose pole points along local +Z. Cross-sections are superellipses.
    /// </summary>
    /// <param name="phiMaxDegrees">Angle from the pole where the surface stops (180 = closed ellipsoid).</param>
    /// <param name="rimScale">Horizontal flare of the final ring (helmet rims).</param>
    public void AddDome(Vector3 center, Vector3 radii, float phiMaxDegrees, int sides, int stacks, Slot slot,
        WeightFn weights, Quaternion? rotation = null, float exp = 2f, float angleOffset = 0f, float rimScale = 1f)
    {
        var rot = rotation ?? Quaternion.Identity;
        var full = phiMaxDegrees >= 179.9f;
        var count = full ? stacks - 1 : stacks;
        var rings = new List<Vector3[]>(count);
        for (var k = 1; k <= count; k++)
        {
            var phi = Geo.Deg(phiMaxDegrees) * k / stacks;
            var (sp, cp) = MathF.SinCos(phi);
            var flare = k == count && !full ? rimScale : 1f;
            var ring = new Vector3[sides];
            for (var i = 0; i < sides; i++)
            {
                var (s, c) = MathF.SinCos(angleOffset + MathF.Tau * i / sides);
                var local = new Vector3(radii.X * sp * Geo.SuperPow(c, exp) * flare, radii.Y * sp * Geo.SuperPow(s, exp) * flare, radii.Z * cp);
                ring[i] = center + Vector3.Transform(local, rot);
            }
            rings.Add(ring);
        }
        var pole = center + Vector3.Transform(new Vector3(0, 0, radii.Z), rot);
        var endApex = full ? center + Vector3.Transform(new Vector3(0, 0, -radii.Z), rot) : Centroid(rings[^1]);
        AddRingStack(rings, slot, weights, pole, endApex);
    }

    // ---- symmetry ------------------------------------------------------------------------

    private MeshBuilder Child() => new(Skeleton) { Density = Density, Detail = Detail, SmoothGroup = SmoothGroup };

    /// <summary>Runs <paramref name="build"/> for the left side and adds a separate mirrored copy for the right.</summary>
    public void Mirrored(Action<MeshBuilder> build)
    {
        var temp = Child();
        build(temp);
        Append(temp, mirror: false, weld: false);
        Append(temp, mirror: true, weld: false);
    }

    /// <summary>
    /// Runs <paramref name="build"/> for the left half of a symmetric shape and adds its mirror image,
    /// sharing the vertices that lie on the centre plane so the two halves form one surface.
    /// </summary>
    public void MirroredWelded(Action<MeshBuilder> build)
    {
        var temp = Child();
        build(temp);
        for (var i = 0; i < temp.Positions.Count; i++)
            if (MathF.Abs(temp.Positions[i].X) < CentreTolerance)
                temp.Positions[i] = temp.Positions[i] with { X = 0 };
        var offset = Positions.Count;
        Append(temp, mirror: false, weld: false);
        Append(temp, mirror: true, weld: true, leftOffset: offset);
    }

    private const float CentreTolerance = 1e-3f;

    private void Append(MeshBuilder other, bool mirror, bool weld, int leftOffset = 0)
    {
        var map = new int[other.Positions.Count];
        for (var i = 0; i < other.Positions.Count; i++)
        {
            var p = other.Positions[i];
            if (mirror && weld && p.X == 0)
            {
                map[i] = leftOffset + i;   // centre-line vertex shared with the left half
                continue;
            }
            var w = other.Weights[i];
            if (mirror)
            {
                p = new Vector3(-p.X, p.Y, p.Z);
                w = Array.ConvertAll(w, x => new Influence(Skeleton.Mirror(x.Bone), x.Weight));
            }
            map[i] = Positions.Count;
            Positions.Add(p);
            Weights.Add(w);
        }

        var chartOffset = Charts.Count;
        Charts.AddRange(other.Charts.Select(c => new UvChart { Width = c.Width, Height = c.Height, Density = c.Density }));
        foreach (var f in other.Faces)
        {
            var v = Array.ConvertAll(f.Vertices, x => map[x]);
            var uv = (Vector2[])f.ChartUv.Clone();
            if (mirror)
            {
                Array.Reverse(v);
                Array.Reverse(uv);
            }
            Faces.Add(new Face(v, f.Slot, f.Detail, f.Chart + chartOffset, uv, f.SmoothGroup));
        }
    }

    // ---- output --------------------------------------------------------------------------

    public MeshData Build(float creaseDegrees)
    {
        var faces = Faces.ToArray();
        var charts = Charts.ToArray();
        PackCharts(charts);
        var uvs = new Vector2[faces.Length][];
        for (var f = 0; f < faces.Length; f++)
        {
            var chart = charts[faces[f].Chart];
            uvs[f] = Array.ConvertAll(faces[f].ChartUv, uv => new Vector2(
                (chart.X + uv.X * chart.Scale) / MeshData.AtlasSize,
                1f - (chart.Y + uv.Y * chart.Scale) / MeshData.AtlasSize));
        }
        return new MeshData
        {
            Positions = Positions.ToArray(),
            Weights = Weights.ToArray(),
            Faces = faces,
            Charts = charts,
            Normals = MeshData.ComputeNormals(Positions, faces, creaseDegrees),
            Uvs = uvs,
        };
    }

    /// <summary>Gap between charts in the virtual atlas, so texels never bleed between them.</summary>
    private const int ChartPadding = 8;

    /// <summary>
    /// Packs the charts into the square atlas at the largest texel density that fits, with a skyline
    /// (bottom-left) packer: each chart goes where it ends up lowest.
    /// </summary>
    private static void PackCharts(UvChart[] charts)
    {
        const int cell = 8;                                   // the skyline is tracked in cells of this many texels
        const int cells = MeshData.AtlasSize / cell;
        var order = Enumerable.Range(0, charts.Length)
            .OrderByDescending(i => MathF.Max(charts[i].Width, charts[i].Height) * charts[i].Density).ToArray();
        var skyline = new int[cells];

        bool Fits(float texelsPerCm, bool apply)
        {
            Array.Clear(skyline);
            foreach (var i in order)
            {
                var c = charts[i];
                var w = (int)MathF.Ceiling((c.Width * c.Density * texelsPerCm + ChartPadding) / cell);
                var h = (int)MathF.Ceiling(c.Height * c.Density * texelsPerCm) + ChartPadding;
                if (w > cells) return false;

                // Lowest position: slide a window of the chart's width along the skyline.
                int bestX = -1, bestY = int.MaxValue;
                for (var x = 0; x + w <= cells; x++)
                {
                    var y = 0;
                    for (var k = x; k < x + w; k++) y = Math.Max(y, skyline[k]);
                    if (y < bestY)
                    {
                        bestY = y;
                        bestX = x;
                    }
                }
                if (bestY + h > MeshData.AtlasSize) return false;
                for (var k = bestX; k < bestX + w; k++) skyline[k] = bestY + h;
                if (apply)
                {
                    c.X = bestX * cell + ChartPadding / 2f;
                    c.Y = bestY + ChartPadding / 2f;
                    c.Scale = c.Density * texelsPerCm;
                }
            }
            return true;
        }

        float lo = 0.05f, hi = 80f;
        for (var i = 0; i < 16; i++)
        {
            var mid = (lo + hi) / 2;
            if (Fits(mid, false)) lo = mid; else hi = mid;
        }
        Fits(lo, true);
    }
}
