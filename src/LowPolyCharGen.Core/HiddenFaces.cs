using System.Numerics;

namespace LowPolyCharGen;

/// <summary>
/// Finds the triangles nobody can see: skin under a vest, helmet or scarf, the buried ends of straps
/// and pouches, and gear faces sunk into the body. A triangle is hidden when every ray leaving it
/// into the open hemisphere above it, from points spread over it, runs into another triangle.
/// </summary>
/// <remarks>
/// Materials are single sided, so a hidden triangle only costs: it is never drawn in front of anything.
/// Rays nearly along the surface are ignored: they could only reveal a sliver at a grazing angle.
/// </remarks>
public static class HiddenFaces
{
    /// <summary>Start points are lifted this far off the surface (cm), so rays don't hit the neighbours they touch.</summary>
    private const float Lift = 0.05f;

    /// <summary>Rays closer to the surface than this (cosine to the normal) are ignored.</summary>
    private const float MinCosine = 0.08f;

    private static readonly Vector3[] Directions = FibonacciSphere(160);

    /// <summary>Flags the hidden faces of a triangle mesh.</summary>
    /// <param name="occluders">The faces that can block rays (all if null).</param>
    /// <param name="candidates">The faces to test (all if null); the others are reported as visible.</param>
    public static bool[] Find(IReadOnlyList<Vector3> positions, IReadOnlyList<Face> faces,
        bool[]? occluders = null, bool[]? candidates = null)
    {
        var bvh = new Bvh(positions, faces, occluders);
        var hidden = new bool[faces.Count];
        Parallel.For(0, faces.Count, f =>
        {
            if (candidates is { } c0 && !c0[f]) return;
            var v = faces[f].Vertices;
            Vector3 a = positions[v[0]], b = positions[v[1]], c = positions[v[2]];
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() < 1e-12f) return;
            n = Vector3.Normalize(n);
            var centre = (a + b + c) / 3;

            // Corners pulled in a little, edge midpoints and the centre.
            Span<Vector3> samples =
            [
                centre,
                Vector3.Lerp(a, centre, 0.15f), Vector3.Lerp(b, centre, 0.15f), Vector3.Lerp(c, centre, 0.15f),
                Vector3.Lerp((a + b) / 2, centre, 0.15f), Vector3.Lerp((b + c) / 2, centre, 0.15f), Vector3.Lerp((c + a) / 2, centre, 0.15f),
            ];
            foreach (var s in samples)
            {
                var origin = s + n * Lift;
                if (bvh.Escapes(origin, n, f)) return;
                foreach (var d in Directions)
                    if (Vector3.Dot(d, n) > MinCosine && bvh.Escapes(origin, d, f)) return;
            }
            hidden[f] = true;
        });
        return hidden;
    }

    private static Vector3[] FibonacciSphere(int count)
    {
        var result = new Vector3[count];
        var golden = MathF.PI * (3 - MathF.Sqrt(5));
        for (var i = 0; i < count; i++)
        {
            var z = 1 - 2 * (i + 0.5f) / count;
            var r = MathF.Sqrt(1 - z * z);
            var (s, c) = MathF.SinCos(golden * i);
            result[i] = new Vector3(r * c, r * s, z);
        }
        return result;
    }

    /// <summary>Bounding volume hierarchy over the triangles, for "does this ray hit anything" queries.</summary>
    private sealed class Bvh
    {
        private readonly Vector3[] _a, _e1, _e2;          // triangle corner and edges
        private readonly int[] _face;                       // triangle -> face index
        private readonly List<Node> _nodes = [];
        private readonly int[] _order;

        private struct Node
        {
            public Vector3 Min, Max;
            public int Left, Right;   // children, or -1 for a leaf
            public int Start, Count;  // leaf range in _order
        }

        public Bvh(IReadOnlyList<Vector3> positions, IReadOnlyList<Face> faces, bool[]? use)
        {
            var count = use?.Count(x => x) ?? faces.Count;
            _a = new Vector3[count];
            _e1 = new Vector3[count];
            _e2 = new Vector3[count];
            _face = new int[count];
            var centres = new Vector3[count];
            var t = 0;
            for (var f = 0; f < faces.Count; f++)
            {
                if (use is { } u && !u[f]) continue;
                var v = faces[f].Vertices;
                _a[t] = positions[v[0]];
                _e1[t] = positions[v[1]] - _a[t];
                _e2[t] = positions[v[2]] - _a[t];
                _face[t] = f;
                centres[t] = _a[t] + (_e1[t] + _e2[t]) / 3;
                t++;
            }
            _order = Enumerable.Range(0, count).ToArray();
            if (count > 0) Split(0, count, centres);
        }

        private int Split(int start, int count, Vector3[] centres)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var i = start; i < start + count; i++)
            {
                var t = _order[i];
                foreach (var p in (ReadOnlySpan<Vector3>)[_a[t], _a[t] + _e1[t], _a[t] + _e2[t]])
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
            var index = _nodes.Count;
            _nodes.Add(new Node { Min = min, Max = max, Left = -1, Right = -1, Start = start, Count = count });
            if (count <= 4) return index;

            var size = max - min;
            var axis = size.X > size.Y ? (size.X > size.Z ? 0 : 2) : (size.Y > size.Z ? 1 : 2);
            Array.Sort(_order, start, count, Comparer<int>.Create((x, y) => centres[x][axis].CompareTo(centres[y][axis])));
            var half = count / 2;
            var left = Split(start, half, centres);
            var right = Split(start + half, count - half, centres);
            var node = _nodes[index];
            node.Left = left;
            node.Right = right;
            _nodes[index] = node;
            return index;
        }

        /// <summary>True if the ray from <paramref name="origin"/> along <paramref name="dir"/> hits no triangle but <paramref name="self"/>.</summary>
        public bool Escapes(Vector3 origin, Vector3 dir, int self)
        {
            var inv = new Vector3(1 / dir.X, 1 / dir.Y, 1 / dir.Z);
            Span<int> stack = stackalloc int[64];
            var top = 0;
            if (_nodes.Count > 0) stack[top++] = 0;
            while (top > 0)
            {
                var node = _nodes[stack[--top]];
                if (!HitsBox(origin, inv, node.Min, node.Max)) continue;
                if (node.Left < 0)
                {
                    for (var i = node.Start; i < node.Start + node.Count; i++)
                    {
                        var t = _order[i];
                        if (_face[t] != self && HitsTriangle(origin, dir, t)) return false;
                    }
                }
                else
                {
                    stack[top++] = node.Left;
                    stack[top++] = node.Right;
                }
            }
            return true;
        }

        private static bool HitsBox(Vector3 o, Vector3 inv, Vector3 min, Vector3 max)
        {
            var t1 = (min - o) * inv;
            var t2 = (max - o) * inv;
            var lo = Vector3.Min(t1, t2);
            var hi = Vector3.Max(t1, t2);
            var near = MathF.Max(MathF.Max(lo.X, lo.Y), MathF.Max(lo.Z, 0f));
            var far = MathF.Min(MathF.Min(hi.X, hi.Y), hi.Z);
            return near <= far;
        }

        /// <summary>Möller-Trumbore, either side of the triangle counts.</summary>
        private bool HitsTriangle(Vector3 o, Vector3 d, int t)
        {
            var p = Vector3.Cross(d, _e2[t]);
            var det = Vector3.Dot(_e1[t], p);
            if (MathF.Abs(det) < 1e-9f) return false;
            var inv = 1 / det;
            var s = o - _a[t];
            var u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, _e1[t]);
            var v = Vector3.Dot(d, q) * inv;
            if (v < 0 || u + v > 1) return false;
            return Vector3.Dot(_e2[t], q) * inv > 1e-4f;
        }
    }
}
