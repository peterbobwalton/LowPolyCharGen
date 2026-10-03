using System.Numerics;

namespace LowPolyCharGen;

/// <summary>Factory for the weight functions the part builders use.</summary>
public sealed class Skinning(Skeleton skeleton)
{
    public const int MaxInfluences = 4;

    public Skeleton Skeleton { get; } = skeleton;

    /// <summary>All vertices follow one bone.</summary>
    public WeightFn Rigid(string bone)
    {
        var w = new[] { new Influence(Skeleton.IndexOf(bone), 1f) };
        return _ => w;
    }

    /// <summary>All vertices get the weights <paramref name="source"/> has at one point (gear that rides on a soft surface).</summary>
    public static WeightFn RigidAt(WeightFn source, Vector3 point)
    {
        var w = source(point);
        return _ => w;
    }

    /// <summary>
    /// Bones laid out along one coordinate. Segment i owns the range from its start to the next
    /// segment's start; neighbours cross-fade over +-blend around each joint.
    /// </summary>
    public WeightFn Chain(Func<Vector3, float> coordinate, float blend, params (string Bone, float Start)[] segments)
    {
        var bones = segments.Select(s => Skeleton.IndexOf(s.Bone)).ToArray();
        var starts = segments.Select(s => s.Start).ToArray();
        return p =>
        {
            var t = coordinate(p);
            var list = new List<Influence>(MaxInfluences);
            for (var i = 0; i < bones.Length; i++)
            {
                var w = 1f;
                if (i > 0) w *= Geo.SmoothStep(((t - starts[i]) / blend + 1) / 2);
                if (i + 1 < bones.Length) w *= 1 - Geo.SmoothStep(((t - starts[i + 1]) / blend + 1) / 2);
                if (w > 0.02f) list.Add(new Influence(bones[i], w));
            }
            return Normalize(list);
        };
    }

    /// <summary>Cross-fades between two weight functions; <paramref name="amount"/> is the share of <paramref name="b"/>.</summary>
    public static WeightFn Blend(WeightFn a, WeightFn b, Func<Vector3, float> amount) => p =>
    {
        var t = Geo.Saturate(amount(p));
        if (t <= 0.001f) return a(p);
        if (t >= 0.999f) return b(p);
        var list = new List<Influence>();
        foreach (var w in a(p)) Accumulate(list, w.Bone, w.Weight * (1 - t));
        foreach (var w in b(p)) Accumulate(list, w.Bone, w.Weight * t);
        return Normalize(list);
    };

    private static void Accumulate(List<Influence> list, int bone, float weight)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i].Bone == bone)
            {
                list[i] = new Influence(bone, list[i].Weight + weight);
                return;
            }
        list.Add(new Influence(bone, weight));
    }

    /// <summary>Keeps the strongest <see cref="MaxInfluences"/> influences and makes them sum to one.</summary>
    public static Influence[] Normalize(List<Influence> list)
    {
        list.Sort((x, y) => y.Weight.CompareTo(x.Weight));
        if (list.Count > MaxInfluences) list.RemoveRange(MaxInfluences, list.Count - MaxInfluences);
        var sum = list.Sum(x => x.Weight);
        if (sum <= 1e-6f) throw new InvalidOperationException("Vertex without skin weights.");
        return list.Select(x => new Influence(x.Bone, x.Weight / sum)).ToArray();
    }
}
