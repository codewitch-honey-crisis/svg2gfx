namespace Svg2Gfx;

public enum LengthUnit { None, Px, Pt, Pc, Mm, Cm, In, Em, Ex, Percent }

/// <summary>What a percentage is relative to.</summary>
public enum LengthAxis { X, Y, Other }

public readonly record struct SvgLength(double Value, LengthUnit Unit)
{
    public static bool TryParse(string? text, out SvgLength length)
    {
        length = default;
        if (text == null) return false;
        var sc = new Scanner(text.Trim());
        if (!sc.TryNumber(out double v)) return false;
        string unit = sc.Rest.Trim().ToLowerInvariant();
        LengthUnit u;
        switch (unit)
        {
            case "": u = LengthUnit.None; break;
            case "px": u = LengthUnit.Px; break;
            case "pt": u = LengthUnit.Pt; break;
            case "pc": u = LengthUnit.Pc; break;
            case "mm": u = LengthUnit.Mm; break;
            case "cm": u = LengthUnit.Cm; break;
            case "in": u = LengthUnit.In; break;
            case "em": u = LengthUnit.Em; break;
            case "ex": u = LengthUnit.Ex; break;
            case "%": u = LengthUnit.Percent; break;
            default: return false;
        }
        length = new SvgLength(v, u);
        return true;
    }

    /// <summary>Converts to user units.</summary>
    public double Resolve(RenderContext ctx, LengthAxis axis, double fontSize)
    {
        switch (Unit)
        {
            case LengthUnit.None:
            case LengthUnit.Px: return Value;
            case LengthUnit.Pt: return Value / 72.0 * ctx.Dpi;
            case LengthUnit.Pc: return Value / 6.0 * ctx.Dpi;
            case LengthUnit.Mm: return Value / 25.4 * ctx.Dpi;
            case LengthUnit.Cm: return Value / 2.54 * ctx.Dpi;
            case LengthUnit.In: return Value * ctx.Dpi;
            case LengthUnit.Em: return Value * fontSize;
            case LengthUnit.Ex: return Value * fontSize * 0.5;
            case LengthUnit.Percent:
                double basis = axis switch
                {
                    LengthAxis.X => ctx.ViewportWidth,
                    LengthAxis.Y => ctx.ViewportHeight,
                    _ => Math.Sqrt(ctx.ViewportWidth * ctx.ViewportWidth + ctx.ViewportHeight * ctx.ViewportHeight) / Math.Sqrt(2),
                };
                return Value / 100.0 * basis;
        }
        return Value;
    }

    /// <summary>Resolves a length as a fraction (objectBoundingBox units / gradient offsets).</summary>
    public double AsFraction() => Unit == LengthUnit.Percent ? Value / 100.0 : Value;
}
