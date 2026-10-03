namespace LowPolyCharGen.Fbx;

/// <summary>
/// Double precision 4x4 transform in the row-vector layout FBX stores (translation in elements
/// 12..14). Bind matrices are composed from the exact local values written to the file so that the
/// bind pose and the node transforms agree to well within the FBX SDK's validation tolerance.
/// </summary>
internal readonly struct Matrix4d
{
    private readonly double[] _m;

    private Matrix4d(double[] m) => _m = m;

    public static Matrix4d Identity => new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

    public double[] ToArray() => (double[])_m.Clone();

    /// <summary>Uniform scale, then rotation by XYZ Euler angles (X first, then Y, then Z), then a translation.</summary>
    public static Matrix4d FromEulerXyzDegrees(double x, double y, double z, double tx, double ty, double tz, double scale = 1)
    {
        const double toRadians = Math.PI / 180.0;
        var (sx, cx) = Math.SinCos(x * toRadians);
        var (sy, cy) = Math.SinCos(y * toRadians);
        var (sz, cz) = Math.SinCos(z * toRadians);
        // Rows are the images of the local X, Y and Z axes (the transpose of Rz * Ry * Rx).
        var k = scale;
        return new Matrix4d(
        [
            k * cy * cz, k * cy * sz, -k * sy, 0,
            k * (sx * sy * cz - cx * sz), k * (sx * sy * sz + cx * cz), k * sx * cy, 0,
            k * (cx * sy * cz + sx * sz), k * (cx * sy * sz - sx * cz), k * cx * cy, 0,
            tx, ty, tz, 1,
        ]);
    }

    /// <summary>this followed by <paramref name="next"/> (row-vector convention).</summary>
    public Matrix4d Then(Matrix4d next)
    {
        var a = _m;
        var b = next._m;
        var r = new double[16];
        for (var row = 0; row < 4; row++)
            for (var col = 0; col < 4; col++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++) sum += a[row * 4 + k] * b[k * 4 + col];
                r[row * 4 + col] = sum;
            }
        return new Matrix4d(r);
    }

    /// <summary>Inverse of a uniform scale + rotation + translation matrix.</summary>
    public Matrix4d InvertRigid()
    {
        var m = _m;
        var r = new double[16];
        var scale2 = m[0] * m[0] + m[1] * m[1] + m[2] * m[2];   // squared length of the first axis
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
                r[row * 4 + col] = m[col * 4 + row] / scale2;
        for (var col = 0; col < 3; col++)
            r[12 + col] = -(m[12] * r[col] + m[13] * r[4 + col] + m[14] * r[8 + col]);
        r[15] = 1;
        return new Matrix4d(r);
    }
}
