using System.Numerics;

namespace LowPolyCharGen;

/// <summary>
/// Reduces a dense triangle mesh to a triangle budget by repeatedly collapsing the edge whose removal
/// changes the shape least (quadric error metrics, Garland and Heckbert 1997). The triangles that are
/// left are spent where the surface curves - knuckles, fingertips, the rim of an ear - and the flat
/// parts get large triangles, which is what makes a decimated sculpt look organic rather than gridded.
/// </summary>
/// <remarks>
/// Collapses keep the mesh manifold (the link condition), never flip or crush a triangle, and leave
/// locked vertices (for example a seam that has to meet another part) exactly where they are. Open
/// borders are held by extra planes standing on them, so a border keeps its outline.
/// </remarks>
public static class Decimator
{
    public sealed class Options
    {
        /// <summary>Stop once the mesh has this many triangles or fewer.</summary>
        public int TargetTriangles { get; init; }

        /// <summary>Vertices that may not move or be removed.</summary>
        public IReadOnlySet<int>? Locked { get; init; }

        /// <summary>
        /// Per-vertex weight on the error (default 1): above 1 keeps more detail there, e.g. fingertips
        /// and nails, below 1 lets it go first.
        /// </summary>
        public Func<int, float>? Importance { get; init; }

        /// <summary>
        /// True to move a merged vertex to the point that fits the surface best; false to keep one of
        /// the two original vertices (so per-vertex data such as UVs and weights can be carried over).
        /// </summary>
        public bool OptimalPlacement { get; init; } = true;

        /// <summary>A collapse may not turn any triangle by more than this angle (degrees).</summary>
        public float MaxTurnDegrees { get; init; } = 75f;

        /// <summary>Weight of the planes that hold open borders, relative to the surface.</summary>
        public float BorderWeight { get; init; } = 10f;
    }

    /// <summary>The simplified mesh. <c>Source[i]</c> is the original vertex that vertex i was kept from.</summary>
    public sealed record Result(Vector3[] Positions, int[][] Triangles, int[] Source);

    public static Result Simplify(IReadOnlyList<Vector3> positions, IReadOnlyList<int[]> triangles, Options options)
    {
        var n = positions.Count;
        var pos = positions.ToArray();
        // Weld vertices that sit on top of each other (marching cubes leaves some) and drop the
        // triangles that become degenerate, or the surface is not one connected sheet.
        var weld = new int[n];
        var cells = new Dictionary<(long, long, long), int>();
        const float cell = 1e-4f;
        for (var v = 0; v < n; v++)
        {
            var key = ((long)MathF.Round(pos[v].X / cell), (long)MathF.Round(pos[v].Y / cell), (long)MathF.Round(pos[v].Z / cell));
            if (options.Locked?.Contains(v) != true && cells.TryGetValue(key, out var first)) weld[v] = first;
            else
            {
                weld[v] = v;
                cells.TryAdd(key, v);
            }
        }
        var tris = new List<int[]>(triangles.Count);
        foreach (var t in triangles)
        {
            int a = weld[t[0]], b = weld[t[1]], c = weld[t[2]];
            if (a == b || b == c || c == a) continue;
            tris.Add([a, b, c]);
        }
        var removed = new bool[tris.Count];
        var vertexFaces = new List<int>[n];
        for (var v = 0; v < n; v++) vertexFaces[v] = [];
        for (var f = 0; f < tris.Count; f++)
            foreach (var v in tris[f]) vertexFaces[v].Add(f);
        var locked = new bool[n];
        if (options.Locked is not null)
            foreach (var v in options.Locked) locked[v] = true;
        var dead = new bool[n];
        var version = new int[n];

        // ---- quadrics: the squared distance to the planes of the faces around each vertex ----
        var q = new Quadric[n];
        for (var f = 0; f < tris.Count; f++)
        {
            var (a, b, c) = (pos[tris[f][0]], pos[tris[f][1]], pos[tris[f][2]]);
            var cross = Vector3.Cross(b - a, c - a);
            var area = cross.Length() / 2;
            if (area < 1e-12f) continue;
            var plane = Quadric.Plane(cross / (2 * area), a) * area;
            foreach (var v in tris[f]) q[v] += plane;
        }
        // Open borders: a plane through each border edge, at right angles to its face.
        var edgeUse = new Dictionary<(int, int), int>();
        foreach (var t in tris)
            for (var k = 0; k < 3; k++)
            {
                var key = Key(t[k], t[(k + 1) % 3]);
                edgeUse[key] = edgeUse.GetValueOrDefault(key) + 1;
            }
        foreach (var t in tris)
        {
            var normal = Vector3.Cross(pos[t[1]] - pos[t[0]], pos[t[2]] - pos[t[0]]);
            if (normal.LengthSquared() < 1e-20f) continue;
            normal = Vector3.Normalize(normal);
            for (var k = 0; k < 3; k++)
            {
                var (a, b) = (t[k], t[(k + 1) % 3]);
                if (edgeUse[Key(a, b)] != 1) continue;
                var along = pos[b] - pos[a];
                var length = along.Length();
                if (length < 1e-9f) continue;
                var side = Vector3.Normalize(Vector3.Cross(along, normal));
                var plane = Quadric.Plane(side, pos[a]) * (options.BorderWeight * length * length);
                q[a] += plane;
                q[b] += plane;
            }
        }
        if (options.Importance is { } importance)
            for (var v = 0; v < n; v++) q[v] *= MathF.Max(1e-3f, importance(v));

        // ---- candidate collapses, cheapest first; stale entries are skipped by version ----
        var heap = new PriorityQueue<(int A, int B, int VersionA, int VersionB), double>();
        void Push(int a, int b)
        {
            if (locked[a] && locked[b]) return;
            var (cost, _) = Evaluate(a, b);
            heap.Enqueue((a, b, version[a], version[b]), cost);
        }
        (double Cost, Vector3 Target) Evaluate(int a, int b)
        {
            var sum = q[a] + q[b];
            if (locked[a]) return (sum.Error(pos[a]), pos[a]);
            if (locked[b]) return (sum.Error(pos[b]), pos[b]);
            var best = pos[a];
            var bestError = sum.Error(pos[a]);
            void Try(Vector3 p)
            {
                var e = sum.Error(p);
                if (e < bestError) { bestError = e; best = p; }
            }
            Try(pos[b]);
            if (options.OptimalPlacement)
            {
                Try((pos[a] + pos[b]) / 2);
                if (sum.Minimum(out var optimal) && Vector3.DistanceSquared(optimal, (pos[a] + pos[b]) / 2) < 4 * Vector3.DistanceSquared(pos[a], pos[b]))
                    Try(optimal);
            }
            return (bestError, best);
        }
        foreach (var (a, b) in edgeUse.Keys) Push(a, b);

        var count = tris.Count;
        var neighboursA = new HashSet<int>();
        // A collapse that is refused now may be fine once its neighbourhood has changed, so when the
        // queue runs dry every remaining edge is queued again, for as long as that still makes progress.
        var countAtRefill = count;
        while (count > options.TargetTriangles)
        {
            if (!heap.TryDequeue(out var item, out _))
            {
                if (count == countAtRefill) break;
                countAtRefill = count;
                for (var f = 0; f < tris.Count; f++)
                {
                    if (removed[f]) continue;
                    var t = tris[f];
                    for (var k = 0; k < 3; k++)
                        if (t[k] < t[(k + 1) % 3]) Push(t[k], t[(k + 1) % 3]);
                        else if (!HasFaceWithEdge(t[(k + 1) % 3], t[k], f)) Push(t[(k + 1) % 3], t[k]);
                }
                continue;
            }
            var (a, b, va, vb) = item;
            if (dead[a] || dead[b] || version[a] != va || version[b] != vb) continue;
            // Keep the locked one, or the one the target sits on when the original points are kept.
            var (_, target) = Evaluate(a, b);
            if (locked[b] || (!locked[a] && !options.OptimalPlacement && target == pos[b])) (a, b) = (b, a);

            // Link condition: the vertices next to both a and b must be exactly the third corners of
            // the faces on the edge, or the collapse would pinch the surface.
            neighboursA.Clear();
            foreach (var f in vertexFaces[a])
                if (!removed[f])
                    foreach (var v in tris[f])
                        if (v != a) neighboursA.Add(v);
            var shared = 0;
            var edgeFaces = 0;
            var seen = new HashSet<int>();
            foreach (var f in vertexFaces[b])
            {
                if (removed[f]) continue;
                var t = tris[f];
                if (t[0] == a || t[1] == a || t[2] == a) edgeFaces++;
                foreach (var v in t)
                    if (v != b && v != a && neighboursA.Contains(v) && seen.Add(v)) shared++;
            }
            if (edgeFaces == 0 || shared != edgeFaces) continue;

            // No triangle may turn too far, flip or collapse to a sliver.
            var minCos = MathF.Cos(options.MaxTurnDegrees * MathF.PI / 180f);
            var ok = true;
            foreach (var v in new[] { a, b })
            {
                foreach (var f in vertexFaces[v])
                {
                    if (removed[f]) continue;
                    var t = tris[f];
                    if ((t[0] == a || t[1] == a || t[2] == a) && (t[0] == b || t[1] == b || t[2] == b)) continue;
                    var before = Vector3.Cross(pos[t[1]] - pos[t[0]], pos[t[2]] - pos[t[0]]);
                    Vector3 P(int i) => i == a || i == b ? target : pos[i];
                    var after = Vector3.Cross(P(t[1]) - P(t[0]), P(t[2]) - P(t[0]));
                    var la = after.Length();
                    var lb = before.Length();
                    if (la < 1e-10f) { ok = false; break; }
                    if (lb < 1e-10f) continue;   // already degenerate (marching cubes makes some): no direction to keep
                    if (Vector3.Dot(before, after) < minCos * la * lb) { ok = false; break; }
                    // Slivers: twice the area against the longest edge squared (0.87 for an equilateral
                    // triangle). Long thin triangles along a finger are fine; needles are not, unless the
                    // triangle was already one.
                    var after2 = la / MathF.Max((P(t[1]) - P(t[0])).LengthSquared(), MathF.Max((P(t[2]) - P(t[1])).LengthSquared(), (P(t[0]) - P(t[2])).LengthSquared()));
                    if (after2 < 0.03f)
                    {
                        var before2 = lb / MathF.Max((pos[t[1]] - pos[t[0]]).LengthSquared(), MathF.Max((pos[t[2]] - pos[t[1]]).LengthSquared(), (pos[t[0]] - pos[t[2]]).LengthSquared()));
                        if (after2 < 0.5f * before2) { ok = false; break; }
                    }
                }
                if (!ok) break;
            }
            if (!ok) continue;

            // Collapse b into a.
            pos[a] = target;
            q[a] += q[b];
            dead[b] = true;
            foreach (var f in vertexFaces[b])
            {
                if (removed[f]) continue;
                var t = tris[f];
                if (t[0] == a || t[1] == a || t[2] == a)
                {
                    removed[f] = true;
                    count--;
                    continue;
                }
                for (var k = 0; k < 3; k++)
                    if (t[k] == b) t[k] = a;
                vertexFaces[a].Add(f);
            }
            vertexFaces[a].RemoveAll(f => removed[f]);
            version[a]++;
            var around = new HashSet<int>();
            foreach (var f in vertexFaces[a])
                foreach (var v in tris[f])
                    if (v != a) around.Add(v);
            foreach (var v in around)
            {
                version[v]++;
                Push(a, v);
                // The other edges of a neighbour were invalidated by its version bump: re-queue them.
                foreach (var f in vertexFaces[v])
                    if (!removed[f])
                        foreach (var w in tris[f])
                            if (w != v && w != a) Push(v, w);
            }
        }

        // An open border edge appears in only one face, so it would be skipped above when its
        // vertices come in descending order: check for its twin.
        bool HasFaceWithEdge(int a, int b, int except)
        {
            foreach (var f in vertexFaces[a])
            {
                if (f == except || removed[f]) continue;
                var t = tris[f];
                if (t[0] == b || t[1] == b || t[2] == b) return true;
            }
            return false;
        }

        // ---- compact ----
        var map = new int[n];
        var kept = new List<int>();
        for (var v = 0; v < n; v++) map[v] = -1;
        var outTris = new List<int[]>();
        for (var f = 0; f < tris.Count; f++)
        {
            if (removed[f]) continue;
            var t = tris[f];
            var o = new int[3];
            for (var k = 0; k < 3; k++)
            {
                if (map[t[k]] < 0)
                {
                    map[t[k]] = kept.Count;
                    kept.Add(t[k]);
                }
                o[k] = map[t[k]];
            }
            outTris.Add(o);
        }
        return new Result(kept.Select(v => pos[v]).ToArray(), outTris.ToArray(), kept.ToArray());
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>A symmetric 4x4 error quadric stored as its ten distinct terms (double precision).</summary>
    private readonly struct Quadric(double a2, double ab, double ac, double ad, double b2, double bc, double bd, double c2, double cd, double d2)
    {
        private readonly double _a2 = a2, _ab = ab, _ac = ac, _ad = ad, _b2 = b2, _bc = bc, _bd = bd, _c2 = c2, _cd = cd, _d2 = d2;

        public static Quadric Plane(Vector3 normal, Vector3 point)
        {
            double a = normal.X, b = normal.Y, c = normal.Z;
            var d = -(a * point.X + b * point.Y + c * point.Z);
            return new Quadric(a * a, a * b, a * c, a * d, b * b, b * c, b * d, c * c, c * d, d * d);
        }

        public static Quadric operator +(Quadric x, Quadric y) => new(
            x._a2 + y._a2, x._ab + y._ab, x._ac + y._ac, x._ad + y._ad, x._b2 + y._b2,
            x._bc + y._bc, x._bd + y._bd, x._c2 + y._c2, x._cd + y._cd, x._d2 + y._d2);

        public static Quadric operator *(Quadric x, float s) => new(
            x._a2 * s, x._ab * s, x._ac * s, x._ad * s, x._b2 * s, x._bc * s, x._bd * s, x._c2 * s, x._cd * s, x._d2 * s);

        public double Error(Vector3 p)
        {
            double x = p.X, y = p.Y, z = p.Z;
            return _a2 * x * x + 2 * _ab * x * y + 2 * _ac * x * z + 2 * _ad * x
                 + _b2 * y * y + 2 * _bc * y * z + 2 * _bd * y
                 + _c2 * z * z + 2 * _cd * z + _d2;
        }

        /// <summary>The point of least error, if the quadric is well conditioned.</summary>
        public bool Minimum(out Vector3 p)
        {
            // Solve [a2 ab ac; ab b2 bc; ac bc c2] x = -[ad bd cd] by Cramer's rule.
            var det = _a2 * (_b2 * _c2 - _bc * _bc) - _ab * (_ab * _c2 - _bc * _ac) + _ac * (_ab * _bc - _b2 * _ac);
            var scale = Math.Abs(_a2) + Math.Abs(_b2) + Math.Abs(_c2);
            if (Math.Abs(det) < 1e-9 * scale * scale * scale || scale == 0)
            {
                p = default;
                return false;
            }
            double r0 = -_ad, r1 = -_bd, r2 = -_cd;
            var x = (r0 * (_b2 * _c2 - _bc * _bc) - _ab * (r1 * _c2 - _bc * r2) + _ac * (r1 * _bc - _b2 * r2)) / det;
            var y = (_a2 * (r1 * _c2 - _bc * r2) - r0 * (_ab * _c2 - _bc * _ac) + _ac * (_ab * r2 - r1 * _ac)) / det;
            var z = (_a2 * (_b2 * r2 - r1 * _bc) - _ab * (_ab * r2 - r1 * _ac) + r0 * (_ab * _bc - _b2 * _ac)) / det;
            p = new Vector3((float)x, (float)y, (float)z);
            return float.IsFinite(p.X + p.Y + p.Z);
        }
    }
}
