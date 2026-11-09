using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cli;
namespace Svg2Gfx
{
    [CliArgs]
    internal sealed partial class Options
    {
        [CmdArg(0, Description = "SVG input file",Required =false)]
        public TextReader Input { get; set; } = Console.In;

        [CmdArg(ShortName = 'o', ValueName = "file", Description = "Write output to a file")]
        public TextWriter Output { get; set; } = Console.Out;
        [CmdArg(ShortName ='n', ValueName = "ident", Description = "routine name (default: derived from the input file name)")]
        public string? Name { get; set; }
        [CmdArg(ShortName ='p', ValueName = "text", Description = "prepended to the derived routine name (e.g. draw_ -> draw_logo)")]
        public string? Prefix { get; set; }
        [CmdArg(ShortName ='d', ValueName ="value", Description = "DPI used for pt/pc/mm/cm/in units (default 96)")]
        public double Dpi { get; set; } = 96;
        [CmdArg(ShortName ='r', ValueName = "n", Description = "round coordinates to n decimal places (default: full float precision)")]
        public int? Precision { get; set; }
        [CmdArg(ShortName ='c', Description = "don't emit a comment per shape")]
        public bool NoComments { get; set; }
        [CmdArg(ShortName ='q', Description = "don't print warnings to stderr")]
        public bool Quiet { get; set; }
    }
}
