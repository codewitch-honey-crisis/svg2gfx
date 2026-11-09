using System.Xml.Linq;

namespace Svg2Gfx;

public enum PaintKind { None, Color, CurrentColor, Url }

public sealed record Paint(PaintKind Kind, Rgba Color = default, string? Url = null, Paint? Fallback = null)
{
    public static readonly Paint None = new(PaintKind.None);
    public static readonly Paint Black = new(PaintKind.Color, new Rgba(0, 0, 0, 255));

    public static Paint? TryParse(string value)
    {
        string v = value.Trim();
        string lower = v.ToLowerInvariant();
        if (lower == "none") return None;
        if (lower == "currentcolor") return new Paint(PaintKind.CurrentColor);
        if (lower.StartsWith("url("))
        {
            int close = v.IndexOf(')');
            if (close < 0) return null;
            string inner = v[4..close].Trim().Trim('\'', '"').Trim();
            if (!inner.StartsWith('#')) return null;
            string rest = v[(close + 1)..].Trim();
            Paint? fallback = rest.Length > 0 ? TryParse(rest) : null;
            if (fallback?.Kind == PaintKind.Url) fallback = null;
            return new Paint(PaintKind.Url, default, inner[1..], fallback);
        }
        if (SvgColor.TryParse(v, default, out var c)) return new Paint(PaintKind.Color, c);
        return null;
    }
}

/// <summary>Computed style for one element. Inherited properties are copied from the parent.</summary>
public sealed class ComputedStyle
{
    // inherited
    public Paint Fill = Paint.Black;
    public double FillOpacity = 1;
    public string FillRule = "non_zero";
    public Paint Stroke = Paint.None;
    public double StrokeWidth = 1;
    public double StrokeOpacity = 1;
    public string LineCap = "butt";
    public string LineJoin = "miter";
    public double MiterLimit = 4;
    public double[]? Dash;
    public double DashOffset;
    public Rgba Color = new(0, 0, 0, 255);
    public double FontSize = 16;
    public bool Visible = true;

    // not inherited
    public double OwnOpacity = 1;
    /// <summary>Product of this element's and all ancestors' opacity (an approximation of group opacity).</summary>
    public double Opacity = 1;
    public bool Display = true;
    public Rgba StopColor = new(0, 0, 0, 255);
    public double StopOpacity = 1;

    public ComputedStyle InheritFrom()
    {
        var c = (ComputedStyle)MemberwiseClone();
        c.OwnOpacity = 1;
        c.Display = true;
        c.StopColor = new Rgba(0, 0, 0, 255);
        c.StopOpacity = 1;
        return c;
    }
}

public sealed class StyleResolver
{
    public static readonly HashSet<string> Properties = new(StringComparer.Ordinal)
    {
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap",
        "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "color", "font-size",
        "visibility", "display", "opacity", "stop-color", "stop-opacity",
    };

    private readonly RenderContext _ctx;
    private readonly Dictionary<XElement, ComputedStyle> _cache = new();

    public StyleResolver(RenderContext ctx) { _ctx = ctx; }

    /// <summary>
    /// The cascade: presentation attributes, then matching stylesheet rules (by specificity, then
    /// document order), then the inline style attribute.
    /// </summary>
    public Dictionary<string, string> Declared(XElement e)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in e.Attributes())
        {
            if (a.Name.Namespace != XNamespace.None) continue;
            if (Properties.Contains(a.Name.LocalName)) d[a.Name.LocalName] = a.Value;
        }
        foreach (var rule in _ctx.Rules.Where(r => r.Selector.Matches(e))
                     .OrderBy(r => r.Selector.Specificity).ThenBy(r => r.Order))
        {
            foreach (var kv in rule.Declarations)
                if (Properties.Contains(kv.Key)) d[kv.Key] = kv.Value;
        }
        foreach (var kv in Css.ParseDeclarations((string?)e.Attribute("style")))
            if (Properties.Contains(kv.Key)) d[kv.Key] = kv.Value;
        return d;
    }

    /// <summary>Computes the style for an element anywhere in the tree (ancestors are resolved and cached).</summary>
    public ComputedStyle ComputeFor(XElement e)
    {
        if (_cache.TryGetValue(e, out var cached)) return cached;
        var parent = e.Parent != null ? ComputeFor(e.Parent) : new ComputedStyle();
        var s = Compute(e, parent);
        _cache[e] = s;
        return s;
    }

    public ComputedStyle Compute(XElement e, ComputedStyle parent)
    {
        if (_cache.TryGetValue(e, out var cached)) return cached;
        var d = Declared(e);
        var s = parent.InheritFrom();
        string? Get(string name) => d.TryGetValue(name, out var v) ? v.Trim() : null;
        bool IsInherit(string? v) => v != null && v.Equals("inherit", StringComparison.OrdinalIgnoreCase);

        // color and font-size first: other properties depend on them
        var color = Get("color");
        if (color != null && !IsInherit(color) && SvgColor.TryParse(color, parent.Color, out var col)) s.Color = col;

        var fs = Get("font-size");
        if (fs != null && !IsInherit(fs) && SvgLength.TryParse(fs, out var fsl))
        {
            s.FontSize = fsl.Unit == LengthUnit.Percent ? parent.FontSize * fsl.Value / 100.0
                : fsl.Unit is LengthUnit.Em or LengthUnit.Ex ? fsl.Resolve(_ctx, LengthAxis.Other, parent.FontSize)
                : fsl.Resolve(_ctx, LengthAxis.Other, parent.FontSize);
        }

        foreach (var (name, raw) in d)
        {
            string v = raw.Trim();
            if (name is "color" or "font-size") continue;
            if (IsInherit(v))
            {
                switch (name)
                {
                    case "opacity": s.OwnOpacity = parent.OwnOpacity; break;
                    case "display": s.Display = parent.Display; break;
                    case "stop-color": s.StopColor = parent.StopColor; break;
                    case "stop-opacity": s.StopOpacity = parent.StopOpacity; break;
                }
                continue; // inherited properties already hold the parent's value
            }
            string lv = v.ToLowerInvariant();
            switch (name)
            {
                case "fill": { var p = Paint.TryParse(v); if (p != null) s.Fill = p; else Warn(e, name, v); break; }
                case "stroke": { var p = Paint.TryParse(v); if (p != null) s.Stroke = p; else Warn(e, name, v); break; }
                case "fill-opacity": if (TryOpacity(v, out var fo)) s.FillOpacity = fo; break;
                case "stroke-opacity": if (TryOpacity(v, out var so)) s.StrokeOpacity = so; break;
                case "opacity": if (TryOpacity(v, out var o)) s.OwnOpacity = o; break;
                case "stop-opacity": if (TryOpacity(v, out var sto)) s.StopOpacity = sto; break;
                case "stop-color": if (SvgColor.TryParse(v, s.Color, out var sc)) s.StopColor = sc; else Warn(e, name, v); break;
                case "fill-rule":
                    if (lv == "evenodd") s.FillRule = "even_odd"; else if (lv == "nonzero") s.FillRule = "non_zero";
                    break;
                case "stroke-linecap":
                    if (lv is "butt" or "round" or "square") s.LineCap = lv;
                    break;
                case "stroke-linejoin":
                    if (lv is "miter" or "round" or "bevel") s.LineJoin = lv;
                    else if (lv is "miter-clip" or "arcs") s.LineJoin = "miter";
                    break;
                case "stroke-miterlimit":
                    if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ml) && ml >= 1)
                        s.MiterLimit = ml;
                    break;
                case "stroke-width":
                    if (SvgLength.TryParse(v, out var sw))
                    {
                        double w = sw.Resolve(_ctx, LengthAxis.Other, s.FontSize);
                        if (w >= 0) s.StrokeWidth = w;
                    }
                    break;
                case "stroke-dashoffset":
                    if (SvgLength.TryParse(v, out var dofs)) s.DashOffset = dofs.Resolve(_ctx, LengthAxis.Other, s.FontSize);
                    break;
                case "stroke-dasharray":
                    s.Dash = ParseDashArray(v, s.FontSize, out bool ok);
                    if (!ok) Warn(e, name, v);
                    break;
                case "visibility":
                    if (lv == "visible") s.Visible = true; else if (lv is "hidden" or "collapse") s.Visible = false;
                    break;
                case "display":
                    s.Display = lv != "none";
                    break;
            }
        }
        s.Opacity = parent.Opacity * s.OwnOpacity;
        _cache[e] = s;
        return s;
    }

    private double[]? ParseDashArray(string v, double fontSize, out bool ok)
    {
        ok = true;
        if (v.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = v.Split(new[] { ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<double>();
        foreach (var p in parts)
        {
            if (!SvgLength.TryParse(p, out var l)) { ok = false; return null; }
            double d = l.Resolve(_ctx, LengthAxis.Other, fontSize);
            if (d < 0) { ok = false; return null; }   // negative values make the whole list invalid
            list.Add(d);
        }
        if (list.Count == 0 || list.All(x => x == 0)) return null; // all zero => solid
        if (list.Count % 2 == 1) list.AddRange(list.ToArray());      // odd count repeats to make it even
        return list.ToArray();
    }

    private static bool TryOpacity(string v, out double o)
    {
        v = v.Trim();
        bool pct = v.EndsWith('%');
        if (!double.TryParse(pct ? v[..^1] : v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out o))
            return false;
        if (pct) o /= 100;
        o = Math.Clamp(o, 0, 1);
        return true;
    }

    private void Warn(XElement e, string prop, string value) =>
        _ctx.Warn($"<{e.Name.LocalName}>: invalid {prop} value '{value}' ignored");
}
