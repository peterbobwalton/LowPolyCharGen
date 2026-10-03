using System.Numerics;
using LowPolyCharGen.Texturing;
using static LowPolyCharGen.Geo;

namespace LowPolyCharGen.Parts;

/// <summary>
/// The head: a sculpted skull welded to the neck, shell-shaped ears, and the hair, hats and eyewear
/// that sit on it. Eyes, brows, lips, stubble and short hair are painted, not modelled.
/// </summary>
internal static class HeadBuilder
{
    private const float HeadDensity = 2.4f;

    private static bool HasHat(BuildContext c) => c.Spec.Hat != HatStyle.None;

    // ---- skull ---------------------------------------------------------------------------

    /// <summary>
    /// Left half of the head, joined to the top ring of the neck. Called while the body's left half is
    /// being built, so the head is mirrored and welded together with it.
    /// </summary>
    public static void BuildSkullHalf(BuildContext c, MeshBuilder m, int[] neckTop)
    {
        var angles = BuildContext.HeadAngles;
        var rows = BuildContext.HeadRows.Length;
        var s = c.HeadScale;
        var head = c.HeadRigid;
        m.Density = HeadDensity;

        // How far each vertex of the face is pushed forward of the smooth skull, by [row][column]:
        // chin, lips, nose and brow ridge stand out, the eye sockets sit back.
        var lips = c.Female ? 0.65f : 0.5f;
        float[][] forward =
        [
            [c.ChinForward, c.ChinForward * 0.8f, 0.1f],                 // jaw line
            [lips, lips * 0.75f, 0f],                                    // mouth
            [0.5f, 0.3f, 0f],                                            // under the nose
            [c.NoseLength, 0.85f, 0.15f],                                // nose tip and nostrils
            [0.45f, -0.2f, -0.75f, -0.25f],                              // eyes
            [c.BrowRidge + 0.1f, c.BrowRidge, c.BrowRidge * 0.8f, c.BrowRidge * 0.3f],   // brow
        ];

        var grid = new int[rows][];
        for (var r = 0; r < rows; r++)
        {
            grid[r] = new int[angles.Length];
            for (var j = 0; j < angles.Length; j++)
            {
                var t = c.HeadRowT(r, j);
                var p = c.Head.Surface(c.HeadZ(t), BuildContext.HeadTheta(angles[j]));
                if (r < forward.Length && j < forward[r].Length) p += Front * (forward[r][j] * s);
                // The second column carries the width of the nose.
                if (j == 1 && r == 3) p.X = c.NoseHalfWidth * s;
                if (j == 1 && r == 2) p.X = c.NoseHalfWidth * 0.9f * s;
                if (j == 1 && r == 4) p.X = c.NoseHalfWidth * 0.55f * s;
                // Cheekbone.
                if (j == 3 && r == 3) p += Vector3.Normalize(new Vector3(p.X, p.Y - c.HeadCenter.Y, 0)) * (0.3f * s);
                grid[r][j] = m.AddVertex(p, head);
            }
        }
        m.AddGridFacesOutward(grid, false, Slot.Head, c.HeadCenter);

        var crown = m.AddVertex(new Vector3(0, c.HeadCenter.Y, c.HeadZ(1f)), head);
        for (var j = 0; j + 1 < angles.Length; j++)
            m.AddPolygonOutward(Slot.Head, c.HeadCenter, crown, grid[^1][j], grid[^1][j + 1]);

        // Underside of the jaw, from the jaw line in to the neck. The sharp angle it makes with the
        // cheek is what draws the jaw line. The head has one more column than the neck.
        var jaw = grid[0];
        var inside = c.HeadCenter + Up * (0.1f * c.HeadHeight);
        m.SmoothGroup = 1;   // never smoothed into the cheeks: this edge is the jaw line
        m.AddPolygonOutward(Slot.Head, inside, jaw[0], jaw[1], neckTop[0]);
        for (var j = 1; j + 1 < jaw.Length; j++)
            m.AddPolygonOutward(Slot.Head, inside, jaw[j], jaw[j + 1], neckTop[j], neckTop[j - 1]);
        m.SmoothGroup = 0;

        BuildEar(c, m);
        m.Density = 1f;
    }

    /// <summary>
    /// A shell-shaped ear: a rim that stands off the head towards the back, a bowl sunk inside it
    /// and a curved back.
    /// </summary>
    private static void BuildEar(BuildContext c, MeshBuilder m)
    {
        var s = c.HeadScale;
        var head = c.HeadRigid;
        var root = c.Head.Surface(c.HeadZ(0.5f), BuildContext.HeadTheta(92f));

        // The ear's plane: "across" runs from where it joins the head to its back rim and swings out
        // from the skull; "up" leans back a little; "face" is the way the hollow side faces.
        // Outline from the front top, over the top, down the back rim to the lobe.
        (float Across, float Up)[] outline =
        [
            (0.0f, 1.9f), (1.3f, 3.0f), (2.6f, 2.7f), (3.3f, 1.2f),
            (3.1f, -0.6f), (2.2f, -2.2f), (1.0f, -3.1f), (0.1f, -2.0f),
        ];
        // How far the ear swings out from the skull, and its size.
        float swing = 34f, size = 1f;
        switch (c.Spec.Ears)
        {
            case EarShape.Pixie:
                // Drawn up into a point at the back of the top.
                outline[1] = (1.2f, 3.3f);
                outline[2] = (3.4f, 5.2f);
                outline[3] = (3.0f, 1.6f);
                swing = 40f;
                break;
            case EarShape.Elephant:
                size = 1.55f;
                swing = 52f;
                break;
            case EarShape.StickOut:
                swing = 66f;
                size = 1.08f;
                break;
        }

        var up = Vector3.Normalize(Up * MathF.Cos(Deg(12f)) + Back * MathF.Sin(Deg(12f)));
        var across = Back * MathF.Cos(Deg(swing)) + Left * MathF.Sin(Deg(swing));
        across = Vector3.Normalize(across - up * Vector3.Dot(across, up));
        var face = Vector3.Cross(across, up);
        s *= size;
        Vector3 At(float a, float u, float f) => root + (across * a + up * u + face * f) * s;

        var rimFront = Array.ConvertAll(outline, o => m.AddVertex(At(o.Across, o.Up, 0.3f), head));
        var rimBack = Array.ConvertAll(outline, o => m.AddVertex(At(o.Across * 0.9f, o.Up * 0.93f, -0.4f), head));
        var bowl = m.AddVertex(At(1.3f, 0.1f, -0.5f), head);
        var back = m.AddVertex(At(1.0f, 0f, -1.7f), head);
        var middle = At(1.6f, 0f, 0f);

        m.AddFan(bowl, rimFront, true, Slot.Ear, FanNeedsReversing(m, rimFront, bowl, middle - face * 4f));
        m.AddFan(back, rimBack, true, Slot.Ear, FanNeedsReversing(m, rimBack, back, middle + face * 4f));
        for (var i = 0; i < outline.Length; i++)
        {
            var j = (i + 1) % outline.Length;
            m.AddPolygonOutward(Slot.Ear, middle, rimFront[i], rimFront[j], rimBack[j], rimBack[i]);
        }

        c.Paint.EarCenter = middle;
        c.Paint.EarAcross = across;
        c.Paint.EarUp = up;
        c.Paint.EarScale = s;
    }

    /// <summary>Whether a fan (centre, ring[i], ring[i+1]) must be reversed to face away from a point.</summary>
    private static bool FanNeedsReversing(MeshBuilder m, int[] ring, int center, Vector3 inside)
    {
        var a = m.Positions[center];
        var normal = Vector3.Zero;
        for (var i = 0; i < ring.Length; i++)
            normal += Vector3.Cross(m.Positions[ring[i]] - a, m.Positions[ring[(i + 1) % ring.Length]] - a);
        return Vector3.Dot(normal, a - inside) < 0;
    }

    /// <summary>Hair, hats and eyewear (built after the body; these are not welded to the skull).</summary>
    public static void BuildExtras(BuildContext c)
    {
        if (c.Spec.FacialHair == FacialHair.BushyBeard) BuildBeard(c);
        BuildHair(c);
        BuildHat(c);
        BuildEyewear(c);
    }

    // ---- shells that wrap the skull ------------------------------------------------------

    /// <summary>The skull's columns all the way round, as degrees from the front (counter-clockwise from above).</summary>
    private static readonly float[] ScalpAngles = [0f, 11f, 30f, 56f, 86f, 120f, 152f, 180f, 208f, 240f, 274f, 304f, 330f, 349f];

    private static readonly float[] ScalpRows = [0.20f, 0.33f, 0.41f, 0.52f, 0.62f, 0.76f, 0.90f, 0.975f];

    private const float TopRow = 0.975f;

    /// <summary>-1 at the very front of the head, +1 at the very back.</summary>
    private static float Frontness(float degreesFromFront) => -MathF.Cos(Deg(degreesFromFront));

    /// <summary>Point on the skull at head height t, pushed out by offset (and up, near the crown).</summary>
    private static Vector3 ScalpPoint(BuildContext c, float t, float degreesFromFront, float offset)
    {
        var p = c.Head.Surface(c.HeadZ(t), BuildContext.HeadTheta(degreesFromFront), offset);
        if (offset > 0) p.Z += offset * SmoothStep((t - 0.6f) / 0.4f);
        return p;
    }

    /// <summary>
    /// Shell over the skull on the skull's own columns. It covers everything above
    /// <paramref name="edge"/>; with a <paramref name="bandHeight"/> it is only a band of that height
    /// (hat bands, cuffs, straps).
    /// </summary>
    /// <returns>The shell's lower edge, one point per column.</returns>
    private static Vector3[] AddScalp(BuildContext c, ScalpEdge edge, float offset, Slot slot, float? bandHeight = null, bool tuck = true)
    {
        var mesh = c.Mesh;
        var head = c.HeadRigid;
        var sides = ScalpAngles.Length;
        var band = bandHeight is not null;

        var columns = new List<int>[sides];
        var lowerEdge = new Vector3[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = ScalpAngles[i];
            var lo = MathF.Min(edge.At(Frontness(angle)), TopRow - 0.02f);
            var hi = band ? MathF.Min(lo + bandHeight!.Value, TopRow) : TopRow;
            var column = columns[i] = [];
            lowerEdge[i] = ScalpPoint(c, lo, angle, offset);

            if (tuck) column.Add(mesh.AddVertex(ScalpPoint(c, lo, angle, -0.4f), head));
            var previous = float.NaN;
            foreach (var row in ScalpRows)
            {
                // Rows below the edge collapse onto it; the resulting empty faces are dropped.
                var t = Math.Clamp(row, lo, hi);
                if (t != previous) column.Add(mesh.AddVertex(ScalpPoint(c, t, angle, offset), head));
                else column.Add(column[^1]);
                previous = t;
            }
            if (band && tuck) column.Add(mesh.AddVertex(ScalpPoint(c, hi, angle, -0.4f), head));
        }

        var grid = new int[columns[0].Count][];
        for (var r = 0; r < grid.Length; r++)
            grid[r] = columns.Select(col => col[r]).ToArray();
        mesh.AddGridFaces(grid, true, slot, false);

        if (!band)
        {
            var apex = mesh.AddVertex(new Vector3(0, c.HeadCenter.Y, c.HeadZ(1f) + offset), head);
            mesh.AddFan(apex, grid[^1], true, slot, false);
        }
        return lowerEdge;
    }

    // ---- beard ---------------------------------------------------------------------------

    /// <summary>
    /// A full, bushy beard: a shell over the jaw and cheeks that grows out and down to a rounded point
    /// below the chin. Behind the ears its rings sink into the head, so only the front shows.
    /// </summary>
    private static void BuildBeard(BuildContext c)
    {
        var s = c.HeadScale;
        const int count = 18;
        // How far the beard stands off the face at each height (0 = top edge, 1 = chin), and how far down past the chin.
        float[] levels = [0f, 0.3f, 0.6f, 0.85f, 1f, 1.2f, 1.45f];
        float[] bulk = [0.3f, 1.4f, 2.2f, 2.8f, 3.0f, 2.6f, 1.2f];
        var chinZ = c.HeadZ(0.02f);
        var rings = new List<Vector3[]>();
        for (var k = 0; k < levels.Length; k++)
        {
            var ring = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var deg = -180f + 360f * i / count;             // 0 = front
                var side = MathF.Abs(deg);
                var covered = side < 104f;
                // Top edge: under the lower lip at the front, climbing to the cheekbone at the sides.
                var topT = Lerp(0.17f, 0.42f, SmoothStep(15f, 75f, side));
                var f = levels[k];
                Vector3 p;
                if (f <= 1f)
                {
                    var t = Lerp(topT, 0.02f, f);
                    var off = covered ? bulk[k] * s * (1f - 0.55f * SmoothStep(60f, 104f, side)) : -1.2f;
                    p = c.Head.Surface(c.HeadZ(t), BuildContext.HeadTheta(deg), off);
                }
                else
                {
                    // Below the chin the beard narrows to a point under the front of the jaw.
                    var below = (f - 1f) / 0.45f;
                    var basis = c.Head.Surface(chinZ, BuildContext.HeadTheta(deg), covered ? bulk[k] * s : -1.2f);
                    var tip = c.Head.Surface(chinZ, BuildContext.HeadTheta(0f), 1.2f * s);
                    p = Vector3.Lerp(basis, tip, 0.55f * below) - Up * (5.5f * s * below);
                    if (!covered) p = Vector3.Lerp(p, tip, 0.8f);
                }
                ring[i] = p;
            }
            rings.Add(ring);
        }
        var tipPoint = MeshBuilder.Centroid(rings[^1]) - Up * 1.2f * s;
        c.Mesh.AddRingStack(rings, Slot.Hair, c.HeadRigid, null, tipPoint);
    }

    // ---- hair ----------------------------------------------------------------------------

    private static void BuildHair(BuildContext c)
    {
        var mesh = c.Mesh;
        var paint = c.Paint;
        var style = c.Spec.Hair;
        var hat = HasHat(c);
        var s = c.HeadScale;
        // Under a hat the hair lies flat so it cannot poke through the crown.
        float Volume(float offset) => hat ? MathF.Min(offset, 0.5f) : offset;

        var cropped = new ScalpEdge(0.80f, 0.63f, 0.50f, 0.30f);
        var shortCut = new ScalpEdge(0.78f, 0.60f, 0.46f, 0.26f);

        if (c.Spec.FaceCover == FaceCover.Balaclava) return;   // all of it is under the knit

        switch (style)
        {
            case HairStyle.Bald:
                return;
            case HairStyle.Buzz:
                // Painted only.
                paint.ScalpHair = cropped;
                paint.ScalpHairDensity = 0.78f;
                return;
            case HairStyle.Mohawk:
                paint.ScalpHair = cropped;
                paint.ScalpHairDensity = 0.3f;   // shaved sides
                if (!hat)
                    mesh.AddDome(c.HeadCenter + Up * (0.08f * c.HeadHeight), new Vector3(1.9f * s, c.HeadHalfDepth + 1.4f, 0.5f * c.HeadHeight + 2.8f),
                        78f, 8, 3, Slot.Hair, c.HeadRigid, Quaternion.CreateFromAxisAngle(Vector3.UnitX, Deg(-14f)), 2f, MathF.PI / 8);
                return;
            case HairStyle.Short or HairStyle.Ponytail or HairStyle.Bun:
                paint.ScalpHair = shortCut;
                AddScalp(c, shortCut, Volume(0.7f), Slot.Hair);
                break;
            case HairStyle.Bob:
                paint.ScalpHair = shortCut;
                AddScalp(c, new ScalpEdge(0.78f, 0.30f, 0.10f, 0.04f), Volume(1.2f), Slot.Hair);
                break;
            case HairStyle.Long:
                paint.ScalpHair = shortCut;
                AddScalp(c, new ScalpEdge(0.78f, 0.28f, 0.06f, 0.02f), Volume(1.2f), Slot.Hair);
                break;
        }

        if (style == HairStyle.Long)
        {
            // Curtain of hair down the back, filling the hollow behind the neck; the top follows the
            // head, the bottom the upper back.
            var weights = Skinning.Blend(c.HeadRigid, c.Spine, p => (c.ChinZ + 2f - p.Z) / 10f);
            Ring Curtain(float z, float inner, float outer, float halfWidth) =>
                new(new Vector3(0, (inner + outer) / 2, z), Left, Back, halfWidth, (outer - inner) / 2, 3f);
            var topZ = c.HeadZ(0.36f);
            var napeZ = c.HeadZ(0.08f);
            var neckBack = -(-c.HeadCenter.Y - 0.6f * c.HeadScale) + c.NeckRadius;
            mesh.AddLoft(
                [
                    Curtain(topZ, c.Head.BackY(topZ, 0) - 3f, c.Head.BackY(topZ, 0) + 1.0f, 0.90f * c.HeadHalfWidth),
                    Curtain(napeZ, neckBack - 1.5f, c.Head.BackY(c.HeadZ(0.3f), 0) + 0.4f, 1.02f * c.HeadHalfWidth),
                    Curtain(148f, c.Torso.BackY(148f, 0) - 1.5f, c.Torso.BackY(148f, 0) + 2.4f, 1.05f * c.HeadHalfWidth),
                    Curtain(139f, c.Torso.BackY(139f, 0) - 0.6f, c.Torso.BackY(139f, 0) + 1.3f, 0.86f * c.HeadHalfWidth),
                ],
                BuildContext.Sides, BuildContext.FlatFront, Slot.Hair, weights, Cap.None, Cap.Flat);
        }

        if (style == HairStyle.Ponytail)
        {
            var angle = Deg(hat ? 100f : 62f);
            var root = c.Head.Project(c.HeadCenter, new Vector3(0, MathF.Sin(angle), MathF.Cos(angle)), -1f);
            var path = new List<Vector3>
            {
                root,
                root + (Back * 3.0f + Up * 0.4f) * s,
                root + (Back * 4.8f - Up * 5.2f) * s,
                root + (Back * 5.0f - Up * 11.5f) * s,
            };
            float[] radius = [2.0f, 2.5f, 2.2f, 1.3f];
            mesh.AddPathLoft(path, i => (radius[i] * s, radius[i] * s), Left, 6, 0, Slot.Hair, c.HeadRigid, 2f, Cap.None, Cap.Flat, 1.5f * s);
        }

        if (style == HairStyle.Bun)
        {
            var angle = Deg(hat ? 104f : 52f);
            var centre = c.Head.Project(c.HeadCenter, new Vector3(0, MathF.Sin(angle), MathF.Cos(angle)), 1.6f * s);
            mesh.AddDome(centre, new Vector3(3.0f, 3.0f, 3.0f) * s, 180f, 6, 4, Slot.Hair, c.HeadRigid);
        }
    }

    // ---- hats ----------------------------------------------------------------------------

    private static void BuildHat(BuildContext c)
    {
        var mesh = c.Mesh;
        var w = c.HeadHalfWidth;
        var d = c.HeadHalfDepth;
        var h = c.HeadHeight;
        var s = c.HeadScale;
        var head = c.HeadRigid;

        switch (c.Spec.Hat)
        {
            case HatStyle.Helmet:
            {
                var centre = new Vector3(0, c.HeadCenter.Y + 0.4f, c.HeadZ(0.57f));
                mesh.AddDome(centre, new Vector3(w * 1.10f + 1.8f, d * 1.07f + 2.0f, 0.5f * h + 2.6f), 91f,
                    12, 4, Slot.Hat, head, Quaternion.CreateFromAxisAngle(Vector3.UnitX, Deg(-12f)),
                    c.HeadExp, MathF.PI / 12, 1.06f);
                // Chin straps.
                mesh.Detail = PaintDetail.Strap;
                mesh.Mirrored(m =>
                {
                    var topZ = c.HeadZ(0.56f);
                    var bottomZ = c.HeadZ(0.06f);
                    m.AddBeam(new Vector3(c.Head.At(topZ).HalfWidth + 0.2f, c.HeadCenter.Y - 0.1f * d, topZ),
                        new Vector3(c.Head.At(bottomZ).HalfWidth * 0.8f + 0.2f, c.HeadCenter.Y - 0.55f * d, bottomZ),
                        0.8f, 0.2f, Back, Slot.HatTrim, head);
                });
                mesh.Detail = PaintDetail.None;
                break;
            }
            case HatStyle.Cap:
            {
                const float offset = 1.0f;
                var edge = new ScalpEdge(0.70f, 0.67f, 0.60f, 0.50f);
                AddScalp(c, edge, offset, Slot.Hat);
                var brow = ScalpPoint(c, edge.Front, 0f, offset);
                var forward = Vector3.Normalize(Front * MathF.Cos(Deg(10f)) - Up * MathF.Sin(Deg(10f)));
                mesh.AddBox(brow + forward * 3.6f * s + Up * 0.3f, new Vector3(0.9f * w, 0.35f, 4.6f * s), Slot.HatTrim, head,
                    LookAlong(forward, Left), new Vector2(0.72f, 0.8f));
                break;
            }
            case HatStyle.Beanie:
            {
                var edge = new ScalpEdge(0.66f, 0.61f, 0.52f, 0.40f);
                mesh.Detail = PaintDetail.Ribbed;
                AddScalp(c, edge, 1.0f, Slot.Hat);
                AddScalp(c, edge, 1.7f, Slot.HatTrim, 0.13f);
                mesh.Detail = PaintDetail.None;
                break;
            }
            case HatStyle.Boonie:
            {
                const float offset = 1.0f;
                var edge = new ScalpEdge(0.72f, 0.70f, 0.66f, 0.60f);
                var inner = AddScalp(c, edge, offset, Slot.Hat, tuck: false);
                Vector3[] Shift(float outward, float down) => inner.Select(p =>
                {
                    var radial = Vector3.Normalize(new Vector3(p.X - c.HeadCenter.X, p.Y - c.HeadCenter.Y, 0));
                    return p + radial * outward - Up * down;
                }).ToArray();
                mesh.AddRingStack([inner, Shift(5.5f * s, 1.1f), Shift(5.5f * s, 1.6f), Shift(-0.8f, 1.0f)], Slot.Hat, head);
                AddScalp(c, edge, offset + 0.3f, Slot.HatTrim, 0.07f);
                break;
            }
            case HatStyle.Keffiyeh:
            {
                // Square headcloth over the head and ears, falling behind to the shoulders, held by a black agal.
                var edge = new ScalpEdge(0.70f, 0.52f, 0.22f, 0.12f);
                AddScalp(c, edge, 1.0f, Slot.Scarf, tuck: false);
                var weights = Skinning.Blend(head, c.Spine, p => (c.ChinZ + 2f - p.Z) / 10f);
                Ring Drape(float z, float inner, float outer, float halfWidth) =>
                    new(new Vector3(0, (inner + outer) / 2, z), Left, Back, halfWidth, (outer - inner) / 2, 2.6f);
                var topZ = c.HeadZ(0.45f);
                var napeZ = c.HeadZ(0.12f);
                var neckBack = c.HeadCenter.Y + 0.6f * s + c.NeckRadius;
                c.Mesh.AddLoft(
                    [
                        Drape(topZ, c.Head.BackY(topZ, 0) - 7f, c.Head.BackY(topZ, 0) + 1.2f, w + 1.2f),
                        Drape(napeZ, neckBack - 6f, c.Head.BackY(c.HeadZ(0.3f), 0) + 1.0f, w + 2.2f),
                        Drape(148f, c.Torso.BackY(148f, 0) - 8f, c.Torso.BackY(148f, 0) + 2.0f, w + 4.0f),
                        Drape(141f, c.Torso.BackY(141f, 0) - 4f, c.Torso.BackY(141f, 0) + 1.2f, w + 3.2f),
                    ],
                    BuildContext.Sides, BuildContext.FlatFront, Slot.Scarf, weights, Cap.None, Cap.Flat);
                // Agal: two black cords round the crown.
                AddScalp(c, new ScalpEdge(0.80f, 0.78f, 0.74f, 0.70f), 1.9f, Slot.Dark, 0.035f);
                AddScalp(c, new ScalpEdge(0.86f, 0.84f, 0.80f, 0.76f), 1.8f, Slot.Dark, 0.03f);
                break;
            }
            case HatStyle.Turban:
            {
                // Cloth wound round the head in thick turns, higher at the back.
                mesh.Detail = PaintDetail.Ribbed;
                AddScalp(c, new ScalpEdge(0.70f, 0.62f, 0.55f, 0.52f), 2.2f, Slot.Hat);
                AddScalp(c, new ScalpEdge(0.70f, 0.62f, 0.55f, 0.52f), 3.0f, Slot.HatTrim, 0.10f);
                AddScalp(c, new ScalpEdge(0.80f, 0.74f, 0.68f, 0.66f), 3.2f, Slot.HatTrim, 0.09f);
                mesh.Detail = PaintDetail.None;
                mesh.AddDome(new Vector3(0, c.HeadCenter.Y + 0.6f, c.HeadZ(0.86f)), new Vector3(w + 2.4f, d + 2.4f, 0.22f * h), 180f,
                    10, 4, Slot.Hat, head, Quaternion.CreateFromAxisAngle(Vector3.UnitX, Deg(-8f)), 2.4f, MathF.PI / 10);
                break;
            }
            case HatStyle.Beret:
            {
                var edge = new ScalpEdge(0.74f, 0.72f, 0.68f, 0.62f);
                AddScalp(c, edge, 0.8f, Slot.Hat, tuck: false);
                AddScalp(c, edge, 1.1f, Slot.HatTrim, 0.07f);
                mesh.AddDome(new Vector3(1.4f * s, c.HeadCenter.Y + 0.3f, c.HeadZ(0.91f) + 0.9f), new Vector3(w + 2.6f, d + 1.6f, 0.11f * h), 180f,
                    10, 4, Slot.Hat, head, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Deg(13f)), 2f, MathF.PI / 10);
                break;
            }
        }
    }

    // ---- eyewear -------------------------------------------------------------------------

    private static void BuildEyewear(BuildContext c)
    {
        var mesh = c.Mesh;
        var w = c.HeadHalfWidth;
        var h = c.HeadHeight;
        var s = c.HeadScale;
        var head = c.HeadRigid;

        switch (c.Spec.Eyewear)
        {
            case EyewearStyle.Sunglasses:
            {
                var z = c.Paint.EyeZ;
                var x = c.Paint.EyeX;
                // Just in front of the brow ridge.
                var front = c.Head.FrontY(z, 0) - (c.BrowRidge + 0.5f) * s;
                mesh.Mirrored(m =>
                {
                    m.AddBox(new Vector3(x, front, z - 0.1f * s), new Vector3(1.9f * s, 0.18f, 1.25f * s), Slot.Lens, head);
                    var hinge = x + 1.9f * s;
                    var earZ = z + 0.5f * s;
                    m.AddBeam(new Vector3(hinge, front, earZ),
                        new Vector3(c.Head.At(earZ).HalfWidth + 0.3f, c.HeadCenter.Y + 0.5f, earZ), 0.3f, 0.15f, Up, Slot.Dark, head);
                });
                mesh.AddBox(new Vector3(0, front, z + 0.6f * s), new Vector3(x - 1.9f * s + 0.2f, 0.16f, 0.3f * s), Slot.Dark, head);
                break;
            }
            case EyewearStyle.Goggles:
            {
                // Pushed up on the forehead (or onto the hat).
                var offset = c.Spec.Hat switch
                {
                    HatStyle.Helmet => 0.10f * w + 2.5f,
                    HatStyle.None => c.Spec.Hair is HairStyle.Bald or HairStyle.Buzz or HairStyle.Mohawk ? 0.4f : 1.4f,
                    HatStyle.Beanie => 2.1f,
                    _ => 1.6f,
                };
                const float bottom = 0.69f, height = 0.085f;
                mesh.Detail = PaintDetail.Strap;
                AddScalp(c, new ScalpEdge(bottom), offset, Slot.GearTrim, height);
                mesh.Detail = PaintDetail.None;
                var centre = ScalpPoint(c, bottom + height / 2, 0f, offset);
                mesh.AddBox(centre + Front * 0.5f, new Vector3(0.74f * w, 0.7f, 0.06f * h), Slot.Lens, head);
                break;
            }
        }
    }
}
