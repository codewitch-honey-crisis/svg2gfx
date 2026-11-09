namespace Svg2Gfx;

public static class SvgTransform
{
    /// <summary>
    /// Parses an SVG transform list. "A B C" means points go through C, then B, then A,
    /// which in gfx's row-vector order is C.Then(B).Then(A).
    /// Returns false on a syntax error (per SVG, an invalid transform list is ignored entirely).
    /// </summary>
    public static bool TryParse(string? text, out Matrix result)
    {
        result = Matrix.Identity;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var sc = new Scanner(text);
        var m = Matrix.Identity;
        sc.SkipWsp();
        while (!sc.AtEnd)
        {
            string name = sc.ReadIdent();
            if (name.Length == 0) return false;
            if (!sc.TryChar('(')) return false;
            var args = new List<double>();
            sc.SkipWsp();
            while (sc.NumberAhead())
            {
                if (!sc.TryNumber(out double v)) return false;
                args.Add(v);
                sc.SkipCommaWsp();
            }
            if (!sc.TryChar(')')) return false;
            Matrix t;
            switch (name)
            {
                case "matrix":
                    if (args.Count != 6) return false;
                    t = new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]);
                    break;
                case "translate":
                    if (args.Count == 1) t = Matrix.Translate(args[0], 0);
                    else if (args.Count == 2) t = Matrix.Translate(args[0], args[1]);
                    else return false;
                    break;
                case "scale":
                    if (args.Count == 1) t = Matrix.Scale(args[0], args[0]);
                    else if (args.Count == 2) t = Matrix.Scale(args[0], args[1]);
                    else return false;
                    break;
                case "rotate":
                    if (args.Count == 1) t = Matrix.Rotate(Deg(args[0]));
                    else if (args.Count == 3)
                        t = Matrix.Translate(-args[1], -args[2]).Then(Matrix.Rotate(Deg(args[0]))).Then(Matrix.Translate(args[1], args[2]));
                    else return false;
                    break;
                case "skewX":
                    if (args.Count != 1) return false;
                    t = Matrix.SkewX(Deg(args[0]));
                    break;
                case "skewY":
                    if (args.Count != 1) return false;
                    t = Matrix.SkewY(Deg(args[0]));
                    break;
                default:
                    return false;
            }
            m = t.Then(m);
            sc.SkipCommaWsp();
        }
        result = m;
        return true;
    }

    public static double Deg(double d) => d * Math.PI / 180.0;
}
