namespace Svg2Gfx;

/// <summary>
/// 2D affine matrix with the exact same layout and multiplication order as gfx::matrix.
/// Points are row vectors: x' = x*A + y*C + E, y' = x*B + y*D + F.
/// <c>Multiply(l, r)</c> applies <c>l</c> first, then <c>r</c>.
/// </summary>
public readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
{
    public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

    public static Matrix Multiply(Matrix l, Matrix r) => new(
        l.A * r.A + l.B * r.C,
        l.A * r.B + l.B * r.D,
        l.C * r.A + l.D * r.C,
        l.C * r.B + l.D * r.D,
        l.E * r.A + l.F * r.C + r.E,
        l.E * r.B + l.F * r.D + r.F);

    /// <summary>Apply this, then <paramref name="next"/>.</summary>
    public Matrix Then(Matrix next) => Multiply(this, next);

    public static Matrix Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static Matrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);
    public static Matrix Rotate(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new(c, s, -s, c, 0, 0);
    }
    public static Matrix SkewX(double radians) => new(1, 0, Math.Tan(radians), 1, 0, 0);
    public static Matrix SkewY(double radians) => new(1, Math.Tan(radians), 0, 1, 0, 0);

    public (double X, double Y) Map(double x, double y) => (x * A + y * C + E, x * B + y * D + F);

    public bool IsIdentity =>
        A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;
}
