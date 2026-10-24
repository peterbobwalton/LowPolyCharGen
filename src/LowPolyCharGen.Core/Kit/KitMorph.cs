using System.Numerics;

namespace LowPolyCharGen.Kit;

/// <summary>
/// Reshapes pack parts for build and gender without touching the skeleton (the pack's animations
/// drive every bone, so proportions can only change in the mesh).
/// </summary>
/// <remarks>
/// The reshaping is one smooth warp of space, the same for every part: the pack's parts overlap
/// (shirt tucked into the trousers, neck inside the collar) rather than share seams, and a continuous
/// warp keeps whatever was inside still inside. Each limb segment and the trunk scales the distance
/// from its axis (across, front and back separately, varying along the segment); a point takes the
/// segments' changes blended by how close it is to each, so joints bend smoothly.
/// </remarks>
public sealed class KitMorph
{
    /// <summary>Scale across a segment at a point along it: width (sideways or up), front, back.</summary>
    public readonly record struct Key(float T, float Width, float Front, float Back)
    {
        public Key(float t, float all) : this(t, all, all, all) { }
    }

    private sealed class Segment(string name, Vector3 a, Vector3 b, Vector3 across)
    {
        public string Name { get; } = name;
        public Vector3 A { get; } = a;
        public Vector3 Axis { get; } = Vector3.Normalize(b - a);
        public float Length { get; } = Vector3.Distance(a, b);
        public Vector3 U { get; } = Vector3.Normalize(across - Vector3.Dot(across, Vector3.Normalize(b - a)) * Vector3.Normalize(b - a));
        public Vector3 V => Vector3.Cross(Axis, U);   // the trunk's V is +Y: negative = front
        public List<Key[]> Profiles { get; } = [];

        public (Vector3 Delta, float Distance) Apply(Vector3 p)
        {
            var t = Math.Clamp(Vector3.Dot(p - A, Axis) / Length, 0f, 1f);
            var off = p - (A + Axis * (t * Length));
            float cu = Vector3.Dot(off, U), cv = Vector3.Dot(off, V);
            float su = 1, sf = 1, sb = 1;
            foreach (var profile in Profiles)
            {
                var k = Eval(profile, t);
                su *= k.Width; sf *= k.Front; sb *= k.Back;
            }
            var sv = cv < 0 ? sf : sb;
            return ((su - 1) * cu * U + (sv - 1) * cv * V, off.Length());
        }

        private static Key Eval(Key[] keys, float t)
        {
            if (t <= keys[0].T) return keys[0];
            for (var i = 1; i < keys.Length; i++)
                if (t <= keys[i].T)
                {
                    var f = (t - keys[i - 1].T) / (keys[i].T - keys[i - 1].T);
                    Key a = keys[i - 1], b = keys[i];
                    return new Key(t, float.Lerp(a.Width, b.Width, f), float.Lerp(a.Front, b.Front, f), float.Lerp(a.Back, b.Back, f));
                }
            return keys[^1];
        }
    }

    /// <summary>A rounded push forward (bust), mirrored on both sides.</summary>
    private readonly record struct Bump(float X, float Z, float RadiusX, float RadiusZ, float Amount);

    /// <summary>Foot (boot) scale about the ankle: width, toe length, heel length, height.</summary>
    private readonly record struct Boot(float Width, float Toe, float Heel, float Height);

    private readonly List<Segment> _segments = [];
    private readonly List<Bump> _bumps = [];
    private Boot _boot = new(1, 1, 1, 1);
    private readonly Vector3 _ankleL;
    private readonly float _frontY;

    /// <summary>How far (cm) the blend between neighbouring segments reaches.</summary>
    private const float Blend = 2.5f;

    public bool IsIdentity { get; private set; } = true;
    public bool Female { get; }

    /// <summary>Points where pack bodies need an extra vertex to take the shape (the bust apexes).</summary>
    public IReadOnlyList<Vector2> BodyInserts { get; }

    public KitMorph(Skeleton toon, Gender gender, float build)
    {
        Vector3 J(string n) => toon[n].Position;
        var pelvis = J("Bip001-Pelvis");
        _frontY = J("Bip001-Spine").Y;
        Female = gender == Gender.Female;

        var trunk = Add("trunk", pelvis - new Vector3(0, 0, 8), J("Bip001-Neck"), Vector3.UnitX);
        var head = Add("head", J("Bip001-Neck"), J("Bip001-Head") + new Vector3(0, 0, 24), Vector3.UnitX);
        var parts = new Dictionary<string, List<Segment>>();
        foreach (var s in new[] { "L", "R" })
        {
            var hand = J($"Bip001-{s}-Hand");
            var handEnd = hand + Vector3.Normalize(hand - J($"Bip001-{s}-Forearm")) * 11f;
            void Limb(string key, Segment seg) { if (!parts.TryGetValue(key, out var l)) parts[key] = l = []; l.Add(seg); }
            Limb("clavicle", Add("clavicle", J($"Bip001-{s}-Clavicle"), J($"Bip001-{s}-UpperArm"), Vector3.UnitZ));
            Limb("upperarm", Add("upperarm", J($"Bip001-{s}-UpperArm"), J($"Bip001-{s}-Forearm"), Vector3.UnitZ));
            Limb("forearm", Add("forearm", J($"Bip001-{s}-Forearm"), hand, Vector3.UnitZ));
            Limb("hand", Add("hand", hand, handEnd, Vector3.UnitZ));
            Limb("thigh", Add("thigh", J($"Bip001-{s}-Thigh"), J($"Bip001-{s}-Calf"), Vector3.UnitX));
            Limb("calf", Add("calf", J($"Bip001-{s}-Calf"), J($"Bip001-{s}-Foot"), Vector3.UnitX));
        }
        _ankleL = J("Bip001-L-Foot");
        void Profile(string key, params Key[] keys)
        {
            IsIdentity = false;
            if (key == "trunk") trunk.Profiles.Add(keys);
            else if (key == "head") head.Profiles.Add(keys);
            else foreach (var seg in parts[key]) seg.Profiles.Add(keys);
        }

        // ---- build: 0 slim, 0.5 the pack's own, 1 heavy -----------------------------------------
        // Trunk keys: t = 0 is 8 cm below the pelvis joint (seat), 0.18 hips, 0.35 belly/waist,
        // 0.6 chest, 0.85 shoulders, 1 the neck joint.
        if (build < 0.5f)
        {
            var k = (0.5f - build) / 0.5f;   // 1 at the slimmest
            float S(float slim) => float.Lerp(1f, slim, k);
            Profile("trunk", new Key(0, S(0.86f)), new Key(0.35f, S(0.8f), S(0.76f), S(0.84f)), new Key(0.6f, S(0.86f)), new Key(0.85f, S(0.9f)), new Key(1, 1));
            foreach (var limb in new[] { "upperarm", "forearm", "thigh", "calf" })
                Profile(limb, new Key(0, S(0.8f)), new Key(1, S(0.84f)));
            Profile("clavicle", new Key(0, 1), new Key(1, S(0.85f)));
        }
        else if (build > 0.5f)
        {
            var k = (build - 0.5f) / 0.5f;   // 1 at the heaviest
            float H(float heavy) => float.Lerp(1f, heavy, k);
            Profile("trunk", new Key(0, H(1.16f), H(1.2f), H(1.16f)), new Key(0.35f, H(1.22f), H(1.5f), H(1.14f)),
                new Key(0.6f, H(1.14f), H(1.24f), H(1.08f)), new Key(0.85f, H(1.08f)), new Key(1, H(1.04f)));
            foreach (var limb in new[] { "upperarm", "forearm", "thigh", "calf" })
                Profile(limb, new Key(0, H(1.2f)), new Key(1, H(1.12f)));
            Profile("clavicle", new Key(0, 1), new Key(1, H(1.15f)));
        }

        // ---- female: narrower shoulders and waist, fuller hips, bust, finer arms, hands and feet --
        if (Female)
        {
            Profile("trunk", new Key(0, 1.2f, 0.95f, 1.18f), new Key(0.18f, 1.18f, 0.95f, 1.14f), new Key(0.38f, 0.76f, 0.8f, 0.86f),
                new Key(0.55f, 0.84f, 0.9f, 0.88f), new Key(0.7f, 0.86f, 0.9f, 0.88f), new Key(0.85f, 0.86f, 0.88f, 0.88f), new Key(1, 0.84f, 0.88f, 0.88f));
            Profile("clavicle", new Key(0, 0.9f), new Key(1, 0.84f));
            Profile("head", new Key(0, 0.84f), new Key(0.25f, 0.86f), new Key(0.4f, 1f), new Key(1, 1f));   // slimmer neck
            Profile("upperarm", new Key(0, 0.82f), new Key(1, 0.8f));
            Profile("forearm", new Key(0, 0.8f), new Key(1, 0.78f));
            Profile("hand", new Key(0, 0.85f), new Key(1, 0.85f));
            Profile("thigh", new Key(0, 1.2f, 1.06f, 1.18f), new Key(0.2f, 1.04f, 1f, 1.04f), new Key(0.5f, 0.94f), new Key(1, 0.86f));
            Profile("calf", new Key(0, 0.86f), new Key(1, 0.85f));
            _bumps.Add(new Bump(7.5f, 121f, 6.5f, 6.5f, 6.5f));
            _boot = new Boot(0.86f, 0.85f, 0.9f, 0.95f);
        }
        BodyInserts = Female ? [new Vector2(8f, 121f), new Vector2(-8f, 121f)] : [];
    }

    private Segment Add(string name, Vector3 a, Vector3 b, Vector3 across)
    {
        var s = new Segment(name, a, b, across);
        _segments.Add(s);
        return s;
    }

    public Vector3 Apply(Vector3 p)
    {
        if (IsIdentity) return p;
        Span<float> dist = stackalloc float[_segments.Count];
        Span<Vector3> delta = stackalloc Vector3[_segments.Count];
        var nearest = float.MaxValue;
        for (var i = 0; i < _segments.Count; i++)
        {
            (delta[i], dist[i]) = _segments[i].Apply(p);
            nearest = MathF.Min(nearest, dist[i]);
        }
        Vector3 sum = Vector3.Zero;
        float total = 0;
        for (var i = 0; i < _segments.Count; i++)
        {
            var w = MathF.Exp(-(dist[i] - nearest) / Blend);
            sum += delta[i] * w;
            total += w;
        }
        var q = p + sum / total;

        foreach (var b in _bumps)
            if (p.Y < _frontY)
                foreach (var side in stackalloc[] { 1f, -1f })
                {
                    float dx = (p.X - side * b.X) / b.RadiusX, dz = (p.Z - b.Z) / b.RadiusZ;
                    q.Y -= b.Amount * MathF.Exp(-(dx * dx + dz * dz));
                }

        if (_boot != new Boot(1, 1, 1, 1))
        {
            var side = MathF.Sign(p.X);
            var ankle = _ankleL with { X = _ankleL.X * side };
            var k = Math.Clamp((ankle.Z + 4f - p.Z) / 8f, 0f, 1f);
            if (k > 0 && side != 0)
            {
                var o = q - ankle with { Z = 0 };
                var booted = new Vector3(o.X * _boot.Width, o.Y * (o.Y < 0 ? _boot.Toe : _boot.Heel), o.Z * _boot.Height) + ankle with { Z = 0 };
                q = Vector3.Lerp(q, booted, k);
            }
        }
        // The soles stay on the ground (the reshape can push them a millimetre or so below it).
        if (q.Z < 0 && p.Z >= 0) q.Z = 0;
        return q;
    }
}
