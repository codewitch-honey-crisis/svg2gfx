using System.Globalization;

namespace Svg2Gfx;

/// <summary>Small cursor over an attribute string, used by the path, transform, list and color parsers.</summary>
internal sealed class Scanner
{
    private readonly string _s;
    public int Pos;

    public Scanner(string s) { _s = s ?? string.Empty; Pos = 0; }

    public bool AtEnd => Pos >= _s.Length;
    public char Peek => Pos < _s.Length ? _s[Pos] : '\0';
    public string Rest => Pos < _s.Length ? _s[Pos..] : string.Empty;

    public static bool IsWsp(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';

    public void SkipWsp()
    {
        while (Pos < _s.Length && IsWsp(_s[Pos])) ++Pos;
    }

    /// <summary>Skips whitespace, at most one comma, then whitespace.</summary>
    public void SkipCommaWsp()
    {
        SkipWsp();
        if (Pos < _s.Length && _s[Pos] == ',') ++Pos;
        SkipWsp();
    }

    public bool TryChar(char c)
    {
        SkipWsp();
        if (Pos < _s.Length && _s[Pos] == c) { ++Pos; return true; }
        return false;
    }

    public bool NumberAhead()
    {
        SkipWsp();
        if (AtEnd) return false;
        char c = _s[Pos];
        if (char.IsAsciiDigit(c) || c == '.') return true;
        if ((c == '+' || c == '-') && Pos + 1 < _s.Length)
        {
            char n = _s[Pos + 1];
            return char.IsAsciiDigit(n) || n == '.';
        }
        return false;
    }

    /// <summary>
    /// Reads an SVG number: [+-] digits [. digits] [(e|E) [+-] digits].
    /// An exponent is only consumed when digits follow it, so "2em" reads as 2 followed by the unit "em".
    /// </summary>
    public bool TryNumber(out double value)
    {
        value = 0;
        SkipWsp();
        int start = Pos, i = Pos, n = _s.Length;
        if (i < n && (_s[i] == '+' || _s[i] == '-')) ++i;
        int digitsStart = i;
        while (i < n && char.IsAsciiDigit(_s[i])) ++i;
        bool hasInt = i > digitsStart;
        bool hasFrac = false;
        if (i < n && _s[i] == '.')
        {
            int f = i + 1;
            while (f < n && char.IsAsciiDigit(_s[f])) ++f;
            hasFrac = f > i + 1;
            if (hasFrac || hasInt) i = f;
        }
        if (!hasInt && !hasFrac) return false;
        if (i < n && (_s[i] == 'e' || _s[i] == 'E'))
        {
            int e = i + 1;
            if (e < n && (_s[e] == '+' || _s[e] == '-')) ++e;
            if (e < n && char.IsAsciiDigit(_s[e]))
            {
                while (e < n && char.IsAsciiDigit(_s[e])) ++e;
                i = e;
            }
        }
        if (!double.TryParse(_s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;
        Pos = i;
        return true;
    }

    /// <summary>Reads an arc flag ('0' or '1'), which in SVG path data may be packed without separators.</summary>
    public bool TryFlag(out bool value)
    {
        value = false;
        SkipCommaWsp();
        if (AtEnd) return false;
        char c = _s[Pos];
        if (c == '0' || c == '1') { value = c == '1'; ++Pos; return true; }
        return false;
    }

    public string ReadIdent()
    {
        SkipWsp();
        int start = Pos;
        while (Pos < _s.Length && (char.IsAsciiLetterOrDigit(_s[Pos]) || _s[Pos] == '-' || _s[Pos] == '_')) ++Pos;
        return _s[start..Pos];
    }

    /// <summary>Parses a whitespace/comma separated list of numbers. Returns null on garbage.</summary>
    public static List<double>? ParseNumberList(string s)
    {
        var sc = new Scanner(s);
        var list = new List<double>();
        sc.SkipWsp();
        while (!sc.AtEnd)
        {
            if (!sc.TryNumber(out double v)) return null;
            list.Add(v);
            sc.SkipCommaWsp();
        }
        return list;
    }
}
