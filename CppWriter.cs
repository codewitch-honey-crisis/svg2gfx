using System.Globalization;
using System.Text;

namespace Svg2Gfx;

/// <summary>Indented line writer plus C++ literal formatting helpers.</summary>
public sealed class CppWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    public int? Precision;

    public void Indent() => ++_indent;
    public void Outdent() => --_indent;

    public void Line(string text = "")
    {
        if (text.Length > 0) _sb.Append(' ', _indent * 4).Append(text);
        _sb.Append('\n');
    }

    public override string ToString() => _sb.ToString();

    /// <summary>Formats a C++ float literal (shortest round-trip form, invariant culture).</summary>
    public string F(double v)
    {
        if (Precision is int p) v = Math.Round(v, p, MidpointRounding.AwayFromZero);
        float f = (float)v;
        if (float.IsNaN(f) || float.IsInfinity(f)) f = 0;
        if (f == 0) return "0.f"; // also folds -0
        string s = f.ToString("R", CultureInfo.InvariantCulture);
        if (s.Contains('E'))
        {
            s = s.Replace("E+", "e").Replace("E", "e");
        }
        else if (!s.Contains('.'))
        {
            s += ".";
        }
        return s + "f";
    }

    public string Pt(double x, double y) => $"gfx::pointf({F(x)}, {F(y)})";

    public string Mat(Matrix m) => $"gfx::matrix({F(m.A)}, {F(m.B)}, {F(m.C)}, {F(m.D)}, {F(m.E)}, {F(m.F)})";

    /// <summary>Emits "res = call; if (res != success) return res;" on one line.</summary>
    public void Checked(string call) =>
        Line($"res = {call}; if (res != gfx::gfx_result::success) return res;");

    /// <summary>Escapes text for use inside a // comment.</summary>
    public static string Comment(string s) => s.Replace("\r", " ").Replace("\n", " ").Replace("*/", "* /");
}
