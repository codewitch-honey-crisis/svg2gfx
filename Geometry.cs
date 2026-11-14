namespace Svg2Gfx;

public enum SegKind { Move, Line, Quad, Cubic, Arc, Close }

/// <summary>
/// One absolute path segment. (Sx,Sy) is the start point (the current point before the segment),
/// (X,Y) is the end point. Quad uses (X1,Y1); Cubic uses (X1,Y1),(X2,Y2).
/// Arc uses Rx, Ry, AngleDeg, Large, Sweep.
/// </summary>
public sealed class Seg
{
    public SegKind Kind;
    public double Sx, Sy, X1, Y1, X2, Y2, X, Y;
    public double Rx, Ry, AngleDeg;
    public bool Large, Sweep;
}

public struct Bounds
{
    public double MinX, MinY, MaxX, MaxY;
    public bool Valid;

    public void Add(double x, double y)
    {
        if (!Valid) { MinX = MaxX = x; MinY = MaxY = y; Valid = true; return; }
        if (x < MinX) MinX = x; if (x > MaxX) MaxX = x;
        if (y < MinY) MinY = y; if (y > MaxY) MaxY = y;
    }
    public double Width => Valid ? MaxX - MinX : 0;
    public double Height => Valid ? MaxY - MinY : 0;
}

/// <summary>Drawable geometry in the element's local user space.</summary>
public abstract class Geometry
{
    public abstract Bounds GetBounds();
}

public sealed class PathGeometry : Geometry
{
    public readonly List<Seg> Segs = new();

    public bool HasDrawing => Segs.Any(s => s.Kind != SegKind.Move);

    public void MoveTo(double x, double y, double sx, double sy) =>
        Segs.Add(new Seg { Kind = SegKind.Move, Sx = sx, Sy = sy, X = x, Y = y });
    public void LineTo(double x, double y, double sx, double sy) =>
        Segs.Add(new Seg { Kind = SegKind.Line, Sx = sx, Sy = sy, X = x, Y = y });
    public void CubicTo(double x1, double y1, double x2, double y2, double x, double y, double sx, double sy) =>
        Segs.Add(new Seg { Kind = SegKind.Cubic, Sx = sx, Sy = sy, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, X = x, Y = y });
    public void Close() => Segs.Add(new Seg { Kind = SegKind.Close });

    public override Bounds GetBounds()
    {
        var b = new Bounds();
        foreach (var s in Segs)
        {
            switch (s.Kind)
            {
                case SegKind.Move:
                case SegKind.Line:
                    b.Add(s.X, s.Y);
                    break;
                case SegKind.Quad:
                    b.Add(s.Sx, s.Sy); b.Add(s.X, s.Y);
                    foreach (var t in QuadExtrema(s.Sx, s.X1, s.X)) b.Add(QuadAt(s.Sx, s.X1, s.X, t), QuadAt(s.Sy, s.Y1, s.Y, t));
                    foreach (var t in QuadExtrema(s.Sy, s.Y1, s.Y)) b.Add(QuadAt(s.Sx, s.X1, s.X, t), QuadAt(s.Sy, s.Y1, s.Y, t));
                    break;
                case SegKind.Cubic:
                    b.Add(s.Sx, s.Sy); b.Add(s.X, s.Y);
                    foreach (var t in CubicExtrema(s.Sx, s.X1, s.X2, s.X))
                        b.Add(CubicAt(s.Sx, s.X1, s.X2, s.X, t), CubicAt(s.Sy, s.Y1, s.Y2, s.Y, t));
                    foreach (var t in CubicExtrema(s.Sy, s.Y1, s.Y2, s.Y))
                        b.Add(CubicAt(s.Sx, s.X1, s.X2, s.X, t), CubicAt(s.Sy, s.Y1, s.Y2, s.Y, t));
                    break;
                case SegKind.Arc:
                    b.Add(s.Sx, s.Sy); b.Add(s.X, s.Y);
                    foreach (var (x, y) in SampleArc(s, 64)) b.Add(x, y);
                    break;
            }
        }
        return b;
    }

    private static double QuadAt(double p0, double p1, double p2, double t) =>
        (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * p1 + t * t * p2;

    private static IEnumerable<double> QuadExtrema(double p0, double p1, double p2)
    {
        double d = p0 - 2 * p1 + p2;
        if (Math.Abs(d) > 1e-12)
        {
            double t = (p0 - p1) / d;
            if (t > 0 && t < 1) yield return t;
        }
    }

    private static double CubicAt(double p0, double p1, double p2, double p3, double t)
    {
        double mt = 1 - t;
        return mt * mt * mt * p0 + 3 * mt * mt * t * p1 + 3 * mt * t * t * p2 + t * t * t * p3;
    }

    private static IEnumerable<double> CubicExtrema(double p0, double p1, double p2, double p3)
    {
        // derivative: 3[(p1-p0)(1-t)^2 + 2(p2-p1)(1-t)t + (p3-p2)t^2] -> a t^2 + b t + c
        double a = -p0 + 3 * p1 - 3 * p2 + p3;
        double b = 2 * (p0 - 2 * p1 + p2);
        double c = p1 - p0;
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) > 1e-12)
            {
                double t = -c / b;
                if (t > 0 && t < 1) yield return t;
            }
            yield break;
        }
        double disc = b * b - 4 * a * c;
        if (disc < 0) yield break;
        double sq = Math.Sqrt(disc);
        double t1 = (-b + sq) / (2 * a), t2 = (-b - sq) / (2 * a);
        if (t1 > 0 && t1 < 1) yield return t1;
        if (t2 > 0 && t2 < 1) yield return t2;
    }

    /// <summary>Endpoint-to-center arc conversion (SVG spec F.6.5/F.6.6), sampled.</summary>
    public static IEnumerable<(double, double)> SampleArc(Seg s, int steps)
    {
        double rx = Math.Abs(s.Rx), ry = Math.Abs(s.Ry);
        if (rx < 1e-12 || ry < 1e-12) yield break;
        double phi = SvgTransform.Deg(s.AngleDeg);
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx2 = (s.Sx - s.X) / 2, dy2 = (s.Sy - s.Y) / 2;
        double x1p = cos * dx2 + sin * dy2;
        double y1p = -sin * dx2 + cos * dy2;
        double lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lambda > 1) { double l = Math.Sqrt(lambda); rx *= l; ry *= l; }
        double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        double coef = den == 0 ? 0 : Math.Sqrt(Math.Max(0, num / den));
        if (s.Large == s.Sweep) coef = -coef;
        double cxp = coef * rx * y1p / ry;
        double cyp = -coef * ry * x1p / rx;
        double cx = cos * cxp - sin * cyp + (s.Sx + s.X) / 2;
        double cy = sin * cxp + cos * cyp + (s.Sy + s.Y) / 2;
        double Angle(double ux, double uy, double vx, double vy)
        {
            double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }
        double theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double dtheta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!s.Sweep && dtheta > 0) dtheta -= 2 * Math.PI;
        else if (s.Sweep && dtheta < 0) dtheta += 2 * Math.PI;
        for (int i = 0; i <= steps; ++i)
        {
            double th = theta1 + dtheta * i / steps;
            double ct = Math.Cos(th), st = Math.Sin(th);
            yield return (cx + rx * ct * cos - ry * st * sin, cy + rx * ct * sin + ry * st * cos);
        }
    }
}

/// <summary>A &lt;rect&gt;, drawn with the canvas's rectangle()/rounded_rectangle() primitives.</summary>
public sealed class RectGeometry : Geometry
{
    public double X, Y, W, H, Rx, Ry;
    /// <summary>Either corner radius being zero gives square corners.</summary>
    public bool IsRounded => Rx > 0 && Ry > 0;

    public override Bounds GetBounds()
    {
        var b = new Bounds();
        b.Add(X, Y);
        b.Add(X + W, Y + H);
        return b;
    }
}

public sealed class EllipseGeometry : Geometry
{
    public double Cx, Cy, Rx, Ry;
    public bool IsCircle => Rx == Ry;

    public override Bounds GetBounds()
    {
        var b = new Bounds();
        b.Add(Cx - Rx, Cy - Ry);
        b.Add(Cx + Rx, Cy + Ry);
        return b;
    }
}

/// <summary>SVG path data ("d" attribute) parser producing absolute segments.</summary>
public static class PathData
{
    private static bool IsCommand(char c) => "MmZzLlHhVvCcSsQqTtAa".IndexOf(c) >= 0;

    /// <summary>
    /// Parses path data. Per the SVG error-handling rules, rendering stops at the first error,
    /// so whatever was parsed up to that point is returned and <paramref name="error"/> is set.
    /// </summary>
    public static PathGeometry Parse(string? d, out string? error)
    {
        error = null;
        var g = new PathGeometry();
        if (string.IsNullOrWhiteSpace(d)) return g;
        var sc = new Scanner(d);
        double cx = 0, cy = 0, sx = 0, sy = 0;   // current point, subpath start
        double lcx = 0, lcy = 0;                  // last control point (for S/T)
        char cmd = '\0', prev = '\0';             // prev = upper-case command of previous segment
        bool first = true, needMove = false;
        double[] a = new double[7];

        bool Nums(int n)
        {
            for (int i = 0; i < n; ++i)
            {
                if (i > 0) sc.SkipCommaWsp();
                if (!sc.TryNumber(out a[i])) return false;
            }
            return true;
        }

        while (true)
        {
            sc.SkipCommaWsp();
            if (sc.AtEnd) break;
            char c = sc.Peek;
            if (IsCommand(c)) { cmd = c; ++sc.Pos; }
            else if (cmd == '\0' || !sc.NumberAhead()) { error = $"unexpected '{c}' at offset {sc.Pos}"; break; }

            if (first)
            {
                if (cmd != 'M' && cmd != 'm') { error = "path data must begin with a moveto"; break; }
                first = false;
            }
            bool rel = char.IsLower(cmd);
            char up = char.ToUpperInvariant(cmd);
            if (up == 'Z')
            {
                g.Close();
                cx = sx; cy = sy;
                prev = 'Z';
                needMove = true;
                cmd = '\0'; // numbers directly after Z are an error
                continue;
            }
            if (needMove && up != 'M')
            {
                g.MoveTo(sx, sy, cx, cy);
            }
            needMove = false;
            double ox = rel ? cx : 0, oy = rel ? cy : 0;
            switch (up)
            {
                case 'M':
                    if (!Nums(2)) { error = "bad moveto"; goto done; }
                    cx = sx = a[0] + ox; cy = sy = a[1] + oy;
                    g.MoveTo(cx, cy, cx, cy);
                    cmd = rel ? 'l' : 'L';
                    break;
                case 'L':
                    if (!Nums(2)) { error = "bad lineto"; goto done; }
                    g.LineTo(a[0] + ox, a[1] + oy, cx, cy);
                    cx = a[0] + ox; cy = a[1] + oy;
                    break;
                case 'H':
                    if (!Nums(1)) { error = "bad horizontal lineto"; goto done; }
                    g.LineTo(a[0] + ox, cy, cx, cy);
                    cx = a[0] + ox;
                    break;
                case 'V':
                    if (!Nums(1)) { error = "bad vertical lineto"; goto done; }
                    g.LineTo(cx, a[0] + oy, cx, cy);
                    cy = a[0] + oy;
                    break;
                case 'C':
                    if (!Nums(6)) { error = "bad curveto"; goto done; }
                    g.CubicTo(a[0] + ox, a[1] + oy, a[2] + ox, a[3] + oy, a[4] + ox, a[5] + oy, cx, cy);
                    lcx = a[2] + ox; lcy = a[3] + oy;
                    cx = a[4] + ox; cy = a[5] + oy;
                    break;
                case 'S':
                {
                    if (!Nums(4)) { error = "bad smooth curveto"; goto done; }
                    double x1 = cx, y1 = cy;
                    if (prev == 'C' || prev == 'S') { x1 = 2 * cx - lcx; y1 = 2 * cy - lcy; }
                    g.CubicTo(x1, y1, a[0] + ox, a[1] + oy, a[2] + ox, a[3] + oy, cx, cy);
                    lcx = a[0] + ox; lcy = a[1] + oy;
                    cx = a[2] + ox; cy = a[3] + oy;
                    break;
                }
                case 'Q':
                    if (!Nums(4)) { error = "bad quadratic curveto"; goto done; }
                    g.Segs.Add(new Seg { Kind = SegKind.Quad, Sx = cx, Sy = cy, X1 = a[0] + ox, Y1 = a[1] + oy, X = a[2] + ox, Y = a[3] + oy });
                    lcx = a[0] + ox; lcy = a[1] + oy;
                    cx = a[2] + ox; cy = a[3] + oy;
                    break;
                case 'T':
                {
                    if (!Nums(2)) { error = "bad smooth quadratic curveto"; goto done; }
                    double x1 = cx, y1 = cy;
                    if (prev == 'Q' || prev == 'T') { x1 = 2 * cx - lcx; y1 = 2 * cy - lcy; }
                    g.Segs.Add(new Seg { Kind = SegKind.Quad, Sx = cx, Sy = cy, X1 = x1, Y1 = y1, X = a[0] + ox, Y = a[1] + oy });
                    lcx = x1; lcy = y1;
                    cx = a[0] + ox; cy = a[1] + oy;
                    break;
                }
                case 'A':
                {
                    if (!Nums(3)) { error = "bad arc"; goto done; }
                    if (!sc.TryFlag(out bool large) || !sc.TryFlag(out bool sweep)) { error = "bad arc flags"; goto done; }
                    sc.SkipCommaWsp();
                    if (!sc.TryNumber(out double ex)) { error = "bad arc"; goto done; }
                    sc.SkipCommaWsp();
                    if (!sc.TryNumber(out double ey)) { error = "bad arc"; goto done; }
                    ex += ox; ey += oy;
                    double rx = Math.Abs(a[0]), ry = Math.Abs(a[1]);
                    if (ex == cx && ey == cy)
                    {
                        // zero-length arc: omitted per spec
                    }
                    else if (rx == 0 || ry == 0)
                    {
                        g.LineTo(ex, ey, cx, cy);
                    }
                    else
                    {
                        g.Segs.Add(new Seg { Kind = SegKind.Arc, Sx = cx, Sy = cy, Rx = rx, Ry = ry, AngleDeg = a[2], Large = large, Sweep = sweep, X = ex, Y = ey });
                    }
                    cx = ex; cy = ey;
                    break;
                }
            }
            prev = up;
        }
    done:
        return g;
    }
}
