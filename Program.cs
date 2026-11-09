using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Svg2Gfx;

public static class Program
{
    public static void Main(string[] args)
    {
        Options? toDispose = null;
        try
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (!Options.TryCreate(args, out var options, out var error))
            {
                if (!error!.IsHelpRequest)
                {
                    Console.Error.WriteLine("Error: " + error.Message);
                    Console.Error.WriteLine();
                }
                Options.PrintUsage();
                return;
            }
            toDispose = options;
            var isStdin = options.Input == Console.In;
            var inputName = isStdin ? "<stdin>" : Path.GetFileNameWithoutExtension(CliUtility.GetFilename(options.Input));
            XDocument doc;
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using var reader = XmlReader.Create(options.Input, settings);
                doc = XDocument.Load(reader);
            }
            catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
            {
                throw new FileNotFoundException($"svg2gfx: cannot read {inputName}: {ex.Message}");
                
            }

            var gen = new Generator();
            string code;
            try
            {
                code = gen.Generate(doc, new GeneratorOptions
                {
                    FunctionName = options.Name ?? MakeIdentifier((options.Prefix ?? "") + (isStdin?"draw":inputName)),
                    SourceName = inputName,
                    Dpi = options.Dpi,
                    Precision = options.Precision,
                    Comments = !options.NoComments,
                });
            }
            catch (InvalidDataException ex)
            {
                throw new FormatException($"svg2gfx: {inputName}: {ex.Message}");
            }

            if (!options.Quiet)
                foreach (var w in gen.Warnings) Console.Error.WriteLine($"svg2gfx: warning: {w}");

            options.Output.Write(code);
        }
        catch (ArgumentException argex)
        {
            Console.Error.WriteLine("Error: " + argex.Message);
            Console.Error.WriteLine();
            Options.PrintUsage();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return;
        }
        finally
        {
            toDispose?.Dispose();
        }
        
    }

    private static readonly HashSet<string> CppKeywords = new(StringComparer.Ordinal)
    {
        "alignas","alignof","and","and_eq","asm","auto","bitand","bitor","bool","break","case","catch","char",
        "char16_t","char32_t","class","compl","const","constexpr","const_cast","continue","decltype","default",
        "delete","do","double","dynamic_cast","else","enum","explicit","export","extern","false","float","for",
        "friend","goto","if","inline","int","long","mutable","namespace","new","noexcept","not","not_eq",
        "nullptr","operator","or","or_eq","private","protected","public","register","reinterpret_cast","return",
        "short","signed","sizeof","static","static_assert","static_cast","struct","switch","template","this",
        "thread_local","throw","true","try","typedef","typeid","typename","union","unsigned","using","virtual",
        "void","volatile","wchar_t","while","xor","xor_eq",
    };

    /// <summary>Turns a file name into a valid C++ identifier ("my-icon (2)" -> "my_icon_2").</summary>
    public static string MakeIdentifier(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        string id = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        if (id.Length == 0) id = "svg";
        if (char.IsAsciiDigit(id[0])) id = "svg_" + id;
        if (CppKeywords.Contains(id)) id += "_svg";
        return id;
    }
}
