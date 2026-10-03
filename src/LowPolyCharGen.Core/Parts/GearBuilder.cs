using System.Numerics;
using static LowPolyCharGen.Geo;

namespace LowPolyCharGen.Parts;

/// <summary>Add-ons worn on the body: backpacks, vests and belt kit.</summary>
internal static class GearBuilder
{
    private const float BoxExp = 4f;

    /// <summary>Shells around the torso use the torso's own twelve columns so they never cut into it.</summary>
    private const int WrapSides = 12;

    public static void Build(BuildContext c)
    {
        BuildVest(c);
        BuildBackpack(c);
        BuildBelt(c);
        c.Mesh.Detail = PaintDetail.None;
    }

    /// <summary>How far worn layers push gear off the torso surface.</summary>
    private static float TorsoLayer(BuildContext c) =>
        (c.Spec.Torso == TorsoStyle.Jacket ? 0.5f : 0f) + c.Spec.Vest switch
        {
            VestStyle.PlateCarrier => 1.5f,
            VestStyle.ChestRig => 1.1f,
            _ => 0f,
        };

    // ---- straps --------------------------------------------------------------------------

    /// <summary>
    /// Path up the chest, over the shoulder and down the back at lateral position x, lifted
    /// <paramref name="lift"/> off the torso.
    /// </summary>
    private static List<Vector3> ShoulderPath(BuildContext c, float x, float frontZ, float backZ, float lift)
    {
        var torso = c.Torso;
        // Highest point where the shoulder slope is still wider than the strap position.
        var topZ = torso.ZMax;
        for (var z = torso.ZMax; z > 140f; z -= 0.25f)
            if (torso.At(z).HalfWidth * 0.93f > x) { topZ = z; break; }

        var path = new List<Vector3>();
        for (var z = frontZ; z < topZ - 1.5f; z += 6.5f)
            path.Add(new Vector3(x, torso.FrontY(z, x) - lift, z));
        path.Add(new Vector3(x, torso.FrontY(topZ, x) - lift * 0.7f, topZ + lift * 0.7f));
        path.Add(new Vector3(x, torso.BackY(topZ, x) + lift * 0.7f, topZ + lift * 0.7f));
        var back = new List<Vector3>();
        for (var z = backZ; z < topZ - 1.5f; z += 6.5f)
            back.Add(new Vector3(x, torso.BackY(z, x) + lift, z));
        back.Reverse();
        path.AddRange(back);
        return path;
    }

    private static void AddShoulderStraps(BuildContext c, float x, float frontZ, float backZ, float halfWidth, Slot slot)
    {
        var lift = TorsoLayer(c) + 0.5f;
        var detail = c.Mesh.Detail;
        c.Mesh.Detail = PaintDetail.Strap;
        c.Mesh.Mirrored(m =>
        {
            var path = ShoulderPath(c, x, frontZ, backZ, lift);
            const float root2 = 1.41421356f;
            m.AddPathLoft(path, _ => (halfWidth * root2, 0.4f * root2), Left, 4, MathF.PI / 4, slot, c.Spine);
        });
        c.Mesh.Detail = detail;
    }

    private static void AddTorsoWrap(BuildContext c, float[] zs, float offset, Slot slot)
    {
        var rings = zs.Select(z => c.Torso.RingAt(z, offset)).ToList();
        c.Mesh.AddLoft(rings, WrapSides, 0, slot, c.Spine, Cap.Flat, Cap.Flat);
    }

    // ---- vests ---------------------------------------------------------------------------

    private static void BuildVest(BuildContext c)
    {
        var mesh = c.Mesh;
        var torso = c.Torso;
        var under = c.Spec.Torso == TorsoStyle.Jacket ? 0.5f : 0f;
        switch (c.Spec.Vest)
        {
            case VestStyle.ChestRig:
            {
                mesh.Detail = PaintDetail.Webbing;
                AddTorsoWrap(c, [112f, 122f, 131f], under + 1.1f, Slot.Gear);
                AddShoulderStraps(c, 7.5f, 129f, 129f, 1.7f, Slot.GearTrim);
                // Magazine pouches across the front.
                mesh.Detail = PaintDetail.Pouch;
                foreach (var x in new[] { -7f, 0f, 7f })
                {
                    var centre = new Vector3(x, torso.FrontY(120f, x) - under - 1.1f - 1.5f, 120f);
                    mesh.AddBox(centre, new Vector3(2.9f, 1.6f, 5.2f), Slot.GearTrim, Skinning.RigidAt(c.Spine, centre), null, new Vector2(1f, 0.9f));
                }
                break;
            }
            case VestStyle.PlateCarrier:
            {
                mesh.Detail = PaintDetail.Webbing;
                AddTorsoWrap(c, [106f, 116f, 128f, 137f, 142.5f], under + 1.5f, Slot.Gear);
                AddShoulderStraps(c, 8.5f, 140f, 140f, 2.6f, Slot.Gear);
                // Admin pouch high on the chest and two utility pouches below it.
                mesh.Detail = PaintDetail.Pouch;
                var top = new Vector3(0, torso.FrontY(133f, 0) - under - 1.5f - 0.9f, 133f);
                mesh.AddBox(top, new Vector3(6.5f, 1.0f, 3.6f), Slot.GearTrim, Skinning.RigidAt(c.Spine, top));
                foreach (var x in new[] { -5.2f, 5.2f })
                {
                    var centre = new Vector3(x, torso.FrontY(116f, x) - under - 1.5f - 1.6f, 116f);
                    mesh.AddBox(centre, new Vector3(4.2f, 1.7f, 4.8f), Slot.GearTrim, Skinning.RigidAt(c.Spine, centre), null, new Vector2(1f, 0.9f));
                }
                break;
            }
        }
        mesh.Detail = PaintDetail.None;
    }

    // ---- backpacks -----------------------------------------------------------------------

    private static void BuildBackpack(BuildContext c)
    {
        var style = c.Spec.Backpack;
        if (style == BackpackStyle.None) return;

        var mesh = c.Mesh;
        var pack = c.Skin.Rigid("spine_04");
        // Y of the surface the pack rests against.
        var backY = MathF.Max(c.Torso.BackY(128f, 0), c.Torso.BackY(138f, 0)) + TorsoLayer(c) - 0.4f;

        void Body(float z0, float z1, float halfWidth, float halfDepth, Slot slot, float taper = 0.82f)
        {
            Ring R(float z, float k) => new(new Vector3(0, backY + halfDepth, z), Left, Back, halfWidth * k, halfDepth * (0.6f + 0.4f * k), BoxExp);
            mesh.AddLoft([R(z0, taper), R(z0 + 3f, 1f), R(z1 - 3.5f, 1f), R(z1, taper)],
                BuildContext.Sides, BuildContext.FlatFront, slot, pack, Cap.Flat, Cap.Flat, 0.4f, 0.8f);
        }

        mesh.Detail = PaintDetail.Panel;
        switch (style)
        {
            case BackpackStyle.Light:
            {
                Body(112f, 145f, 11f, 4.6f, Slot.Gear);
                mesh.Detail = PaintDetail.Pouch;
                mesh.AddBox(new Vector3(0, backY + 9.2f + 1.1f, 123f), new Vector3(7.5f, 1.3f, 6.5f), Slot.GearTrim, pack, null, new Vector2(0.9f, 0.8f));
                AddShoulderStraps(c, 8.5f, 121f, 138f, 1.8f, Slot.GearTrim);
                break;
            }
            case BackpackStyle.Rucksack:
            {
                Body(106f, 146f, 14.5f, 7.2f, Slot.Gear);
                // Lid, side pockets, back pocket and a bedroll on top.
                mesh.AddBox(new Vector3(0, backY + 7.4f, 146.5f), new Vector3(13.2f, 7.6f, 2.6f), Slot.GearTrim, pack, null, new Vector2(0.88f, 0.9f));
                mesh.Detail = PaintDetail.Pouch;
                mesh.Mirrored(m => m.AddBox(new Vector3(15.6f, backY + 7.4f, 119f), new Vector3(2.6f, 4.6f, 7.5f), Slot.GearTrim, pack, null, new Vector2(0.85f, 0.85f)));
                mesh.AddBox(new Vector3(0, backY + 14.4f + 1.4f, 121f), new Vector3(8.5f, 1.6f, 8f), Slot.GearTrim, pack, null, new Vector2(0.9f, 0.8f));
                mesh.Detail = PaintDetail.None;
                Ring Roll(float x, float r) => new(new Vector3(x, backY + 7.4f, 153f), Back, Up, r, r, 2f);
                mesh.AddLoft([Roll(-15f, 3.4f), Roll(-14f, 4.2f), Roll(14f, 4.2f), Roll(15f, 3.4f)],
                    BuildContext.Sides, BuildContext.FlatFront, Slot.Accent, pack);
                AddShoulderStraps(c, 8.5f, 119f, 139f, 2.2f, Slot.GearTrim);
                break;
            }
            case BackpackStyle.Hydration:
            {
                Body(116f, 147f, 6.8f, 2.6f, Slot.Gear, 0.7f);
                mesh.Detail = PaintDetail.None;
                mesh.AddBox(new Vector3(0, backY + 5.2f + 0.3f, 143f), new Vector3(1.6f, 0.6f, 1.6f), Slot.Dark, pack);
                AddShoulderStraps(c, 6.5f, 126f, 141f, 1.3f, Slot.GearTrim);
                break;
            }
            case BackpackStyle.Radio:
            {
                mesh.AddBox(new Vector3(0, backY + 4.6f, 131f), new Vector3(10f, 4.6f, 13.5f), Slot.Gear, pack);
                mesh.AddBox(new Vector3(0, backY + 4.6f, 145.3f), new Vector3(8.6f, 3.6f, 0.9f), Slot.GearTrim, pack);
                mesh.Detail = PaintDetail.None;
                mesh.AddBox(new Vector3(-4.5f, backY + 4.6f, 147f), new Vector3(1.2f, 1.2f, 1f), Slot.Metal, pack);
                mesh.AddBox(new Vector3(-0.8f, backY + 4.6f, 146.8f), new Vector3(0.9f, 0.9f, 0.8f), Slot.Metal, pack);
                // Whip antenna.
                mesh.AddBeam(new Vector3(6.4f, backY + 5.5f, 145f), new Vector3(7.6f, backY + 6.5f, 183f), 0.32f, 0.32f, Left, Slot.Dark, pack, new Vector2(0.5f, 0.5f));
                AddShoulderStraps(c, 8.5f, 121f, 139f, 1.9f, Slot.GearTrim);
                break;
            }
        }
        mesh.Detail = PaintDetail.None;
    }

    // ---- belts ---------------------------------------------------------------------------

    /// <summary>The belt itself is painted on the body; this adds what hangs from it.</summary>
    private static void BuildBelt(BuildContext c)
    {
        var style = c.Spec.Belt;
        if (style is BeltStyle.None or BeltStyle.Plain) return;

        var mesh = c.Mesh;
        var hips = c.Hips;
        var weights = BodyBuilder.HipWeights(c);
        // A jacket hem or skirt waistband already sits on the hips.
        var offset = c.Spec.Torso == TorsoStyle.Jacket ? 1.1f : c.Spec.Legs == LegsStyle.Skirt ? 0.8f : 0.15f;
        var zc = (c.Paint.BeltBottomZ + c.Paint.BeltTopZ) / 2;

        // Pouch on the belt at an angle around the waist (0 = left, 90 = back, 270 = front).
        void Pouch(float angleDegrees, Vector3 half, Slot slot, float drop = 0f, float taper = 0.9f)
        {
            var on = hips.Surface(zc, Deg(angleDegrees), offset);
            var outward = Vector3.Normalize(new Vector3(on.X, on.Y - hips.At(zc).CenterY, 0));
            var centre = new Vector3(on.X, on.Y, zc - drop) + outward * half.Y;
            mesh.AddBox(centre, half, slot, Skinning.RigidAt(weights, new Vector3(on.X, on.Y, zc)),
                Basis(Vector3.Cross(outward, Up), outward, Up), new Vector2(taper, taper));
        }

        mesh.Detail = PaintDetail.Pouch;
        if (style == BeltStyle.Pouches)
        {
            Pouch(232f, new Vector3(2.8f, 1.5f, 3.6f), Slot.GearTrim, 0.8f);
            Pouch(308f, new Vector3(2.8f, 1.5f, 3.6f), Slot.GearTrim, 0.8f);
            Pouch(5f, new Vector3(3.2f, 1.7f, 4.2f), Slot.GearTrim, 1.2f);
            Pouch(175f, new Vector3(3.2f, 1.7f, 4.2f), Slot.GearTrim, 1.2f);
            Pouch(90f, new Vector3(4.6f, 1.8f, 3.6f), Slot.GearTrim, 0.8f);
        }
        else
        {
            Pouch(236f, new Vector3(2.6f, 1.4f, 3.4f), Slot.GearTrim, 0.6f);
            Pouch(5f, new Vector3(3.2f, 1.7f, 4.4f), Slot.GearTrim, 1.4f);
            Pouch(62f, new Vector3(3.0f, 1.5f, 3.4f), Slot.GearTrim, 0.6f);
            mesh.Detail = PaintDetail.Panel;
            Pouch(118f, new Vector3(3.0f, 2.6f, 5.2f), Slot.Accent, 2.6f, 0.75f);   // canteen

            // Drop-leg holster on the right thigh, with a strap round the leg and one up to the belt.
            var leg = c.LegL;
            const float holsterZ = 76f;
            var centre = leg.Center(holsterZ);
            var x = -(centre.X + leg.RadiusU(holsterZ) + 1.3f);
            var thigh = c.Skin.Rigid("thigh_r");
            mesh.AddBox(new Vector3(x, centre.Y - 0.5f, holsterZ), new Vector3(1.5f, 3.6f, 6.5f), Slot.GearTrim, thigh, null, new Vector2(1f, 0.7f));
            mesh.Detail = PaintDetail.None;
            mesh.AddBox(new Vector3(x - 0.2f, centre.Y + 0.6f, holsterZ + 7.4f), new Vector3(1.1f, 1.6f, 2.2f), Slot.Dark, thigh);   // pistol grip

            mesh.Detail = PaintDetail.Strap;
            Vector3[] Thigh(float z, float extra)
            {
                var ring = leg.RingAt(z, leg.Clothing(z) + extra).Points(8, Deg(225f));
                return Array.ConvertAll(ring, p => new Vector3(-p.X, p.Y, p.Z));
            }
            mesh.AddRingStack([Thigh(holsterZ - 1.2f, -0.3f), Thigh(holsterZ - 1.2f, 0.45f), Thigh(holsterZ + 1.2f, 0.45f), Thigh(holsterZ + 1.2f, -0.3f)],
                Slot.Belt, thigh);
            var hipHalfWidth = hips.At(zc).HalfWidth + offset;
            mesh.AddBeam(new Vector3(x + 0.6f, centre.Y - 0.5f, holsterZ + 5f), new Vector3(-hipHalfWidth + 0.3f, hips.At(zc).CenterY, c.Paint.BeltBottomZ),
                1.3f, 0.3f, Back, Slot.Belt, Skinning.RigidAt(weights, new Vector3(-hipHalfWidth, 0, 92f)));
        }
        mesh.Detail = PaintDetail.None;
    }
}
