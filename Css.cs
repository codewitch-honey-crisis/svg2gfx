using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Svg2Gfx;

/// <summary>A single compound selector: optional tag, optional #id, zero or more .classes.</summary>
public sealed class CssSelector
{
    public string? Tag;
    public string? Id;
    public readonly List<string> Classes = new();

    /// <summary>CSS specificity packed as (ids, classes, tags).</summary>
    public int Specificity => (Id != null ? 10000 : 0) + Classes.Count * 100 + (Tag != null ? 1 : 0);

    public bool Matches(XElement e)
    {
        if (Tag != null && Tag != e.Name.LocalName) return false;
        if (Id != null && (string?)e.Attribute("id") != Id) return false;
        if (Classes.Count > 0)
        {
            var cls = ((string?)e.Attribute("class") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var c in Classes) if (!cls.Contains(c)) return false;
        }
        return true;
    }
}

public sealed class CssRule
{
    public required CssSelector Selector;
    public required List<KeyValuePair<string, string>> Declarations;
    public int Order;
}

/// <summary>
/// Minimal stylesheet support: rules with simple selectors (tag, #id, .class, combinations of those,
/// comma-separated lists, and '*'). Combinators and at-rules are skipped with a warning.
/// </summary>
public static class Css
{
    private static readonly Regex SimpleSelector = new(@"^(?<tag>\*|[A-Za-z][\w-]*)?(?<rest>(?:[#.][\w-]+)*)$", RegexOptions.Compiled);
    private static readonly Regex Part = new(@"([#.])([\w-]+)", RegexOptions.Compiled);

    public static List<KeyValuePair<string, string>> ParseDeclarations(string? text)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var decl in text.Split(';'))
        {
            int colon = decl.IndexOf(':');
            if (colon <= 0) continue;
            string name = decl[..colon].Trim().ToLowerInvariant();
            string value = decl[(colon + 1)..].Trim();
            int imp = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
            if (imp >= 0) value = value[..imp].Trim();
            if (name.Length > 0 && value.Length > 0) list.Add(new(name, value));
        }
        return list;
    }

    public static void ParseStylesheet(string text, List<CssRule> rules, List<string> warnings)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        int pos = 0;
        while (pos < text.Length)
        {
            int open = text.IndexOf('{', pos);
            if (open < 0) break;
            string selectorText = text[pos..open].Trim();
            // find the matching close brace (handles nested blocks in at-rules)
            int depth = 1, i = open + 1;
            while (i < text.Length && depth > 0)
            {
                if (text[i] == '{') ++depth;
                else if (text[i] == '}') --depth;
                ++i;
            }
            string body = text[(open + 1)..Math.Max(open + 1, i - 1)];
            pos = i;
            if (selectorText.StartsWith('@'))
            {
                warnings.Add($"CSS at-rule '{selectorText}' ignored");
                continue;
            }
            var decls = ParseDeclarations(body);
            foreach (var raw in selectorText.Split(','))
            {
                string sel = raw.Trim();
                if (sel.Length == 0) continue;
                var m = SimpleSelector.Match(sel);
                if (!m.Success)
                {
                    warnings.Add($"CSS selector '{sel}' is not supported (only tag, #id and .class) and was ignored");
                    continue;
                }
                var s = new CssSelector();
                if (m.Groups["tag"].Success && m.Groups["tag"].Value != "*") s.Tag = m.Groups["tag"].Value;
                foreach (Match p in Part.Matches(m.Groups["rest"].Value))
                {
                    if (p.Groups[1].Value == "#") s.Id = p.Groups[2].Value;
                    else s.Classes.Add(p.Groups[2].Value);
                }
                rules.Add(new CssRule { Selector = s, Declarations = decls, Order = rules.Count });
            }
        }
    }
}
