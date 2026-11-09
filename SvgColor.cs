using System.Globalization;

namespace Svg2Gfx;

/// <summary>Non-premultiplied 8-bit RGBA color.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public Rgba(int r, int g, int b, int a) : this(Clamp(r), Clamp(g), Clamp(b), Clamp(a)) { }
    public static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
    public static byte Clamp(double v) => (byte)Math.Round(v < 0 ? 0 : v > 255 ? 255 : v, MidpointRounding.AwayFromZero);
    public Rgba WithAlpha(double factor) => new(R, G, B, Clamp(A * factor));

    /// <summary>gfx::vector_pixel takes channels in A, R, G, B order.</summary>
    public string ToCpp() => $"gfx::vector_pixel({A}, {R}, {G}, {B})";
}

public static class SvgColor
{
    /// <summary>
    /// Parses a CSS/SVG color. Returns false for invalid input. "currentColor" is resolved to
    /// <paramref name="currentColor"/>.
    /// </summary>
    public static bool TryParse(string? text, Rgba currentColor, out Rgba color)
    {
        color = default;
        if (text == null) return false;
        string s = text.Trim();
        if (s.Length == 0) return false;
        if (s[0] == '#') return TryParseHex(s[1..], out color);
        string lower = s.ToLowerInvariant();
        if (lower == "currentcolor") { color = currentColor; return true; }
        if (lower == "transparent") { color = new Rgba(0, 0, 0, 0); return true; }
        int paren = lower.IndexOf('(');
        if (paren > 0 && lower.EndsWith(")"))
        {
            string fn = lower[..paren].Trim();
            string args = lower[(paren + 1)..^1];
            if (fn == "rgb" || fn == "rgba") return TryParseRgbFunc(args, out color);
            if (fn == "hsl" || fn == "hsla") return TryParseHslFunc(args, out color);
            return false;
        }
        return Named.TryGetValue(lower, out color);
    }

    private static bool TryParseHex(string h, out Rgba color)
    {
        color = default;
        foreach (char c in h) if (!Uri.IsHexDigit(c)) return false;
        int H(int i) => Convert.ToInt32(h.Substring(i, 1), 16);
        int HH(int i) => Convert.ToInt32(h.Substring(i, 2), 16);
        switch (h.Length)
        {
            case 3: color = new Rgba(H(0) * 17, H(1) * 17, H(2) * 17, 255); return true;
            case 4: color = new Rgba(H(0) * 17, H(1) * 17, H(2) * 17, H(3) * 17); return true;
            case 6: color = new Rgba(HH(0), HH(2), HH(4), 255); return true;
            case 8: color = new Rgba(HH(0), HH(2), HH(4), HH(6)); return true;
            default: return false;
        }
    }

    /// <summary>Splits "a, b, c" / "a b c / d" into components; each component keeps a trailing '%'.</summary>
    private static List<string>? SplitArgs(string args)
    {
        var parts = new List<string>();
        foreach (var raw in args.Replace("/", " / ").Split(new[] { ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw == "/") continue;
            parts.Add(raw);
        }
        return parts.Count is 3 or 4 ? parts : null;
    }

    private static bool TryComponent(string p, double percentScale, out double v)
    {
        bool pct = p.EndsWith('%');
        if (!double.TryParse(pct ? p[..^1] : p, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
        if (pct) v = v / 100.0 * percentScale;
        return true;
    }

    private static bool TryAlpha(List<string> parts, out double a)
    {
        a = 1;
        if (parts.Count < 4) return true;
        if (!TryComponent(parts[3], 1.0, out a)) return false;
        a = Math.Clamp(a, 0, 1);
        return true;
    }

    private static bool TryParseRgbFunc(string args, out Rgba color)
    {
        color = default;
        var p = SplitArgs(args);
        if (p == null) return false;
        if (!TryComponent(p[0], 255, out double r) || !TryComponent(p[1], 255, out double g) ||
            !TryComponent(p[2], 255, out double b) || !TryAlpha(p, out double a)) return false;
        color = new Rgba(Rgba.Clamp(r), Rgba.Clamp(g), Rgba.Clamp(b), Rgba.Clamp(a * 255));
        return true;
    }

    private static bool TryParseHslFunc(string args, out Rgba color)
    {
        color = default;
        var p = SplitArgs(args);
        if (p == null) return false;
        string hs = p[0].EndsWith("deg") ? p[0][..^3] : p[0];
        if (!double.TryParse(hs, NumberStyles.Float, CultureInfo.InvariantCulture, out double h)) return false;
        if (!TryComponent(p[1], 1, out double s) || !TryComponent(p[2], 1, out double l) || !TryAlpha(p, out double a)) return false;
        // values without '%' are tolerated as percentages
        if (!p[1].EndsWith('%')) s /= 100; if (!p[2].EndsWith('%')) l /= 100;
        s = Math.Clamp(s, 0, 1); l = Math.Clamp(l, 0, 1);
        h = ((h % 360) + 360) % 360 / 360.0;
        double HueToRgb(double m1, double m2, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1.0 / 6) return m1 + (m2 - m1) * t * 6;
            if (t < 0.5) return m2;
            if (t < 2.0 / 3) return m1 + (m2 - m1) * (2.0 / 3 - t) * 6;
            return m1;
        }
        double q = l <= 0.5 ? l * (1 + s) : l + s - l * s;
        double pp = 2 * l - q;
        color = new Rgba(Rgba.Clamp(HueToRgb(pp, q, h + 1.0 / 3) * 255), Rgba.Clamp(HueToRgb(pp, q, h) * 255),
                         Rgba.Clamp(HueToRgb(pp, q, h - 1.0 / 3) * 255), Rgba.Clamp(a * 255));
        return true;
    }

    public static readonly Dictionary<string, Rgba> Named = new(StringComparer.Ordinal)
    {
        ["red"] = new Rgba(255, 0, 0, 255),
        ["green"] = new Rgba(0, 128, 0, 255),
        ["blue"] = new Rgba(0, 0, 255, 255),
        ["yellow"] = new Rgba(255, 255, 0, 255),
        ["cyan"] = new Rgba(0, 255, 255, 255),
        ["magenta"] = new Rgba(255, 0, 255, 255),
        ["black"] = new Rgba(0, 0, 0, 255),
        ["grey"] = new Rgba(128, 128, 128, 255),
        ["gray"] = new Rgba(128, 128, 128, 255),
        ["white"] = new Rgba(255, 255, 255, 255),
        ["aliceblue"] = new Rgba(240, 248, 255, 255),
        ["antiquewhite"] = new Rgba(250, 235, 215, 255),
        ["aqua"] = new Rgba(0, 255, 255, 255),
        ["aquamarine"] = new Rgba(127, 255, 212, 255),
        ["azure"] = new Rgba(240, 255, 255, 255),
        ["beige"] = new Rgba(245, 245, 220, 255),
        ["bisque"] = new Rgba(255, 228, 196, 255),
        ["blanchedalmond"] = new Rgba(255, 235, 205, 255),
        ["blueviolet"] = new Rgba(138, 43, 226, 255),
        ["brown"] = new Rgba(165, 42, 42, 255),
        ["burlywood"] = new Rgba(222, 184, 135, 255),
        ["cadetblue"] = new Rgba(95, 158, 160, 255),
        ["chartreuse"] = new Rgba(127, 255, 0, 255),
        ["chocolate"] = new Rgba(210, 105, 30, 255),
        ["coral"] = new Rgba(255, 127, 80, 255),
        ["cornflowerblue"] = new Rgba(100, 149, 237, 255),
        ["cornsilk"] = new Rgba(255, 248, 220, 255),
        ["crimson"] = new Rgba(220, 20, 60, 255),
        ["darkblue"] = new Rgba(0, 0, 139, 255),
        ["darkcyan"] = new Rgba(0, 139, 139, 255),
        ["darkgoldenrod"] = new Rgba(184, 134, 11, 255),
        ["darkgray"] = new Rgba(169, 169, 169, 255),
        ["darkgreen"] = new Rgba(0, 100, 0, 255),
        ["darkgrey"] = new Rgba(169, 169, 169, 255),
        ["darkkhaki"] = new Rgba(189, 183, 107, 255),
        ["darkmagenta"] = new Rgba(139, 0, 139, 255),
        ["darkolivegreen"] = new Rgba(85, 107, 47, 255),
        ["darkorange"] = new Rgba(255, 140, 0, 255),
        ["darkorchid"] = new Rgba(153, 50, 204, 255),
        ["darkred"] = new Rgba(139, 0, 0, 255),
        ["darksalmon"] = new Rgba(233, 150, 122, 255),
        ["darkseagreen"] = new Rgba(143, 188, 143, 255),
        ["darkslateblue"] = new Rgba(72, 61, 139, 255),
        ["darkslategray"] = new Rgba(47, 79, 79, 255),
        ["darkslategrey"] = new Rgba(47, 79, 79, 255),
        ["darkturquoise"] = new Rgba(0, 206, 209, 255),
        ["darkviolet"] = new Rgba(148, 0, 211, 255),
        ["deeppink"] = new Rgba(255, 20, 147, 255),
        ["deepskyblue"] = new Rgba(0, 191, 255, 255),
        ["dimgray"] = new Rgba(105, 105, 105, 255),
        ["dimgrey"] = new Rgba(105, 105, 105, 255),
        ["dodgerblue"] = new Rgba(30, 144, 255, 255),
        ["firebrick"] = new Rgba(178, 34, 34, 255),
        ["floralwhite"] = new Rgba(255, 250, 240, 255),
        ["forestgreen"] = new Rgba(34, 139, 34, 255),
        ["fuchsia"] = new Rgba(255, 0, 255, 255),
        ["gainsboro"] = new Rgba(220, 220, 220, 255),
        ["ghostwhite"] = new Rgba(248, 248, 255, 255),
        ["gold"] = new Rgba(255, 215, 0, 255),
        ["goldenrod"] = new Rgba(218, 165, 32, 255),
        ["greenyellow"] = new Rgba(173, 255, 47, 255),
        ["honeydew"] = new Rgba(240, 255, 240, 255),
        ["hotpink"] = new Rgba(255, 105, 180, 255),
        ["indianred"] = new Rgba(205, 92, 92, 255),
        ["indigo"] = new Rgba(75, 0, 130, 255),
        ["ivory"] = new Rgba(255, 255, 240, 255),
        ["khaki"] = new Rgba(240, 230, 140, 255),
        ["lavender"] = new Rgba(230, 230, 250, 255),
        ["lavenderblush"] = new Rgba(255, 240, 245, 255),
        ["lawngreen"] = new Rgba(124, 252, 0, 255),
        ["lemonchiffon"] = new Rgba(255, 250, 205, 255),
        ["lightblue"] = new Rgba(173, 216, 230, 255),
        ["lightcoral"] = new Rgba(240, 128, 128, 255),
        ["lightcyan"] = new Rgba(224, 255, 255, 255),
        ["lightgoldenrodyellow"] = new Rgba(250, 250, 210, 255),
        ["lightgray"] = new Rgba(211, 211, 211, 255),
        ["lightgreen"] = new Rgba(144, 238, 144, 255),
        ["lightgrey"] = new Rgba(211, 211, 211, 255),
        ["lightpink"] = new Rgba(255, 182, 193, 255),
        ["lightsalmon"] = new Rgba(255, 160, 122, 255),
        ["lightseagreen"] = new Rgba(32, 178, 170, 255),
        ["lightskyblue"] = new Rgba(135, 206, 250, 255),
        ["lightslategray"] = new Rgba(119, 136, 153, 255),
        ["lightslategrey"] = new Rgba(119, 136, 153, 255),
        ["lightsteelblue"] = new Rgba(176, 196, 222, 255),
        ["lightyellow"] = new Rgba(255, 255, 224, 255),
        ["lime"] = new Rgba(0, 255, 0, 255),
        ["limegreen"] = new Rgba(50, 205, 50, 255),
        ["linen"] = new Rgba(250, 240, 230, 255),
        ["maroon"] = new Rgba(128, 0, 0, 255),
        ["mediumaquamarine"] = new Rgba(102, 205, 170, 255),
        ["mediumblue"] = new Rgba(0, 0, 205, 255),
        ["mediumorchid"] = new Rgba(186, 85, 211, 255),
        ["mediumpurple"] = new Rgba(147, 112, 219, 255),
        ["mediumseagreen"] = new Rgba(60, 179, 113, 255),
        ["mediumslateblue"] = new Rgba(123, 104, 238, 255),
        ["mediumspringgreen"] = new Rgba(0, 250, 154, 255),
        ["mediumturquoise"] = new Rgba(72, 209, 204, 255),
        ["mediumvioletred"] = new Rgba(199, 21, 133, 255),
        ["midnightblue"] = new Rgba(25, 25, 112, 255),
        ["mintcream"] = new Rgba(245, 255, 250, 255),
        ["mistyrose"] = new Rgba(255, 228, 225, 255),
        ["moccasin"] = new Rgba(255, 228, 181, 255),
        ["navajowhite"] = new Rgba(255, 222, 173, 255),
        ["navy"] = new Rgba(0, 0, 128, 255),
        ["oldlace"] = new Rgba(253, 245, 230, 255),
        ["olive"] = new Rgba(128, 128, 0, 255),
        ["olivedrab"] = new Rgba(107, 142, 35, 255),
        ["orange"] = new Rgba(255, 165, 0, 255),
        ["orangered"] = new Rgba(255, 69, 0, 255),
        ["orchid"] = new Rgba(218, 112, 214, 255),
        ["palegoldenrod"] = new Rgba(238, 232, 170, 255),
        ["palegreen"] = new Rgba(152, 251, 152, 255),
        ["paleturquoise"] = new Rgba(175, 238, 238, 255),
        ["palevioletred"] = new Rgba(219, 112, 147, 255),
        ["papayawhip"] = new Rgba(255, 239, 213, 255),
        ["peachpuff"] = new Rgba(255, 218, 185, 255),
        ["peru"] = new Rgba(205, 133, 63, 255),
        ["pink"] = new Rgba(255, 192, 203, 255),
        ["plum"] = new Rgba(221, 160, 221, 255),
        ["powderblue"] = new Rgba(176, 224, 230, 255),
        ["purple"] = new Rgba(128, 0, 128, 255),
        ["rosybrown"] = new Rgba(188, 143, 143, 255),
        ["royalblue"] = new Rgba(65, 105, 225, 255),
        ["saddlebrown"] = new Rgba(139, 69, 19, 255),
        ["salmon"] = new Rgba(250, 128, 114, 255),
        ["sandybrown"] = new Rgba(244, 164, 96, 255),
        ["seagreen"] = new Rgba(46, 139, 87, 255),
        ["seashell"] = new Rgba(255, 245, 238, 255),
        ["sienna"] = new Rgba(160, 82, 45, 255),
        ["silver"] = new Rgba(192, 192, 192, 255),
        ["skyblue"] = new Rgba(135, 206, 235, 255),
        ["slateblue"] = new Rgba(106, 90, 205, 255),
        ["slategray"] = new Rgba(112, 128, 144, 255),
        ["slategrey"] = new Rgba(112, 128, 144, 255),
        ["snow"] = new Rgba(255, 250, 250, 255),
        ["springgreen"] = new Rgba(0, 255, 127, 255),
        ["steelblue"] = new Rgba(70, 130, 180, 255),
        ["tan"] = new Rgba(210, 180, 140, 255),
        ["teal"] = new Rgba(0, 128, 128, 255),
        ["thistle"] = new Rgba(216, 191, 216, 255),
        ["tomato"] = new Rgba(255, 99, 71, 255),
        ["turquoise"] = new Rgba(64, 224, 208, 255),
        ["violet"] = new Rgba(238, 130, 238, 255),
        ["wheat"] = new Rgba(245, 222, 179, 255),
        ["whitesmoke"] = new Rgba(245, 245, 245, 255),
        ["yellowgreen"] = new Rgba(154, 205, 50, 255),
    };

    static SvgColor()
    {
        Named.TryAdd("rebeccapurple", new Rgba(102, 51, 153, 255));
        Named.TryAdd("aqua", new Rgba(0, 255, 255, 255));
        Named.TryAdd("fuchsia", new Rgba(255, 0, 255, 255));
        Named.TryAdd("darkgrey", new Rgba(169, 169, 169, 255));
        Named.TryAdd("darkslategrey", new Rgba(47, 79, 79, 255));
        Named.TryAdd("dimgrey", new Rgba(105, 105, 105, 255));
        Named.TryAdd("grey", new Rgba(128, 128, 128, 255));
        Named.TryAdd("lightgrey", new Rgba(211, 211, 211, 255));
        Named.TryAdd("lightslategrey", new Rgba(119, 136, 153, 255));
        Named.TryAdd("slategrey", new Rgba(112, 128, 144, 255));
    }
}
