using System.Globalization;
using System.Xml.Linq;

namespace Svg2Gfx;

public sealed class RenderContext
{
    public double Dpi = 96;
    public double ViewportWidth = 300, ViewportHeight = 150;
    public readonly List<CssRule> Rules = new();
    public readonly Dictionary<string, XElement> Ids = new(StringComparer.Ordinal);
    public readonly List<string> Warnings = new();
    public StyleResolver Styles = null!;

    public void Warn(string message)
    {
        if (!Warnings.Contains(message)) Warnings.Add(message);
    }
}

public sealed record GradientStop(double Offset, Rgba Color);

/// <summary>A gradient after following its href chain, ready to be laid out against a shape.</summary>
public sealed class ResolvedGradient
{
    public bool Linear;
    public bool ObjectBoundingBox = true;
    public Matrix Transform = Matrix.Identity;
    public string Spread = "pad";
    public SvgLength X1 = new(0, LengthUnit.Percent), Y1 = new(0, LengthUnit.Percent);
    public SvgLength X2 = new(100, LengthUnit.Percent), Y2 = new(0, LengthUnit.Percent);
    public SvgLength Cx = new(50, LengthUnit.Percent), Cy = new(50, LengthUnit.Percent), R = new(50, LengthUnit.Percent);
    public SvgLength? Fx, Fy;
    public SvgLength Fr = new(0, LengthUnit.Percent);
    public readonly List<GradientStop> Stops = new();
}

public static class Gradients
{
    public static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    public static string? Href(XElement e)
    {
        string? h = (string?)e.Attribute(XLink + "href") ?? (string?)e.Attribute("href");
        h = h?.Trim();
        return h != null && h.StartsWith('#') ? h[1..] : null;
    }

    public static bool IsGradient(XElement e) => e.Name.LocalName is "linearGradient" or "radialGradient";

    public static ResolvedGradient? Resolve(RenderContext ctx, string id)
    {
        if (!ctx.Ids.TryGetValue(id, out var head) || !IsGradient(head)) return null;

        // gather the href chain (cycle-safe)
        var chain = new List<XElement>();
        var seen = new HashSet<XElement>();
        for (var e = head; e != null && seen.Add(e);)
        {
            if (IsGradient(e)) chain.Add(e);
            else break;
            string? next = Href(e);
            e = next != null && ctx.Ids.TryGetValue(next, out var n) ? n : null;
        }

        string? Attr(string name)
        {
            foreach (var e in chain)
            {
                var a = (string?)e.Attribute(name);
                if (a != null) return a;
            }
            return null;
        }
        bool Len(string name, ref SvgLength target)
        {
            var a = Attr(name);
            if (a != null && SvgLength.TryParse(a, out var l)) { target = l; return true; }
            return false;
        }

        var g = new ResolvedGradient { Linear = head.Name.LocalName == "linearGradient" };
        if (Attr("gradientUnits")?.Trim() == "userSpaceOnUse") g.ObjectBoundingBox = false;
        var gt = Attr("gradientTransform");
        if (gt != null)
        {
            if (SvgTransform.TryParse(gt, out var m)) g.Transform = m;
            else ctx.Warn($"gradient '{id}': invalid gradientTransform ignored");
        }
        var spread = Attr("spreadMethod")?.Trim();
        if (spread is "reflect" or "repeat") g.Spread = spread;

        if (g.Linear)
        {
            Len("x1", ref g.X1); Len("y1", ref g.Y1); Len("x2", ref g.X2); Len("y2", ref g.Y2);
        }
        else
        {
            Len("cx", ref g.Cx); Len("cy", ref g.Cy); Len("r", ref g.R); Len("fr", ref g.Fr);
            SvgLength f = default;
            if (Len("fx", ref f)) g.Fx = f;
            if (Len("fy", ref f)) g.Fy = f;
        }

        // stops come from the first gradient in the chain that has any
        foreach (var e in chain)
        {
            var stops = e.Elements().Where(s => s.Name.LocalName == "stop").ToList();
            if (stops.Count == 0) continue;
            double last = 0;
            foreach (var s in stops)
            {
                double off = 0;
                var os = ((string?)s.Attribute("offset"))?.Trim();
                if (os != null)
                {
                    bool pct = os.EndsWith('%');
                    if (double.TryParse(pct ? os[..^1] : os, NumberStyles.Float, CultureInfo.InvariantCulture, out var ov))
                        off = pct ? ov / 100 : ov;
                }
                off = Math.Clamp(off, 0, 1);
                if (off < last) off = last;   // offsets must be non-decreasing
                last = off;
                var st = ctx.Styles.ComputeFor(s);
                g.Stops.Add(new GradientStop(off, st.StopColor.WithAlpha(st.StopOpacity)));
            }
            break;
        }
        return g;
    }
}
