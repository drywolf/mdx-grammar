using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ranet_uilibrary_olap_Crawler
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> lexer_lines = new List<string>();
            List<string> parser_lines = new List<string>();

            HandleCodeFile("./GrammarSource/mdxLexer.cs", (line,stack) =>
            {
                var last_line = lexer_lines.LastOrDefault();

                if (last_line != null && last_line.Contains(line))
                    return false;

                string token_id = null;

                for (int i = stack.Count - 1; i > 0; i--)
                {
                    var stack_line = stack[i];
                    var token_prefix = @"    // $ANTLR start """;

                    if (!stack_line.StartsWith(token_prefix))
                        continue;

                    token_id = stack_line.Substring(token_prefix.Length).TrimEnd('\"');
                    break;
                }

                if (token_id == null)
                    token_id = "?????";

                lexer_lines.Add(token_id + " : " + line + ";");
                return true;
            });

            HandleCodeFile("./GrammarSource/mdxParser.cs", (line, stack) =>
            {
                if (!line.EndsWith(";"))
                    return false;

                parser_lines.Add(line);
                return true;
            });

            using (TextWriter tw = new StreamWriter("lexer.txt"))
            {
                foreach (var l in lexer_lines)
                    tw.WriteLine(l);
            }

            using (TextWriter parser_tw = new StreamWriter("parser.txt"))
            using (TextWriter cleaned_tw = new StreamWriter("parser_clean.txt"))
            {
                foreach (var line in parser_lines)
                {
                    parser_tw.WriteLine(line);

                    Regex return_object_regx = new Regex(@"\[(.)*\]");
                    Regex variables_regx = new Regex(@"[A-Za-z][0-9A-Za-z]*(_[0-9A-Za-z]+)*=");

                    var return_object_debug = return_object_regx.Matches(line).Cast<object>().ToList();
                    var variables_debug = variables_regx.Matches(line).Cast<object>().ToList();

                    var cleaned =
                        variables_regx
                        .Replace(line, "");

                    cleaned = 
                        return_object_regx
                        .Replace(cleaned, "")
                        .Replace(" returns ", "")
                        .Replace("  ", " "); // clean double spaces

                    cleaned_tw.WriteLine(cleaned);
                }
            }
        }

        static void HandleCodeFile(string file, Func<string, List<string>, bool> on_match)
        {
            List<string> line_stack = new List<string>();

            using(TextReader tr = new StreamReader(file))
            {
                string src_line = null;

                while ((src_line = tr.ReadLine()) != null)
                {
                    line_stack.Add(src_line);

                    var fn = "mdx.g:";
                    var start = src_line.IndexOf(fn);

                    if (start < 0)
                        continue;

                    var data = src_line.Substring(start + fn.Length);

                    var line_sep = data.IndexOf(':');

                    if (line_sep < 0)
                        continue;

                    var col_sep = data.IndexOf(':', line_sep + 1);

                    if (col_sep < 0)
                        continue;

                    var line_str = data.Substring(0, line_sep);
                    var col_str = data.Substring(line_sep + 1, col_sep - line_sep - 1);
                    var text = data.Substring(col_sep + 2);

                    if (on_match(text, line_stack))
                        line_stack.Clear();

                    //var line = int.Parse(line_str);
                    //var col = int.Parse(col_str);

                    //while (lines.Count <= line)
                    //    lines.Add("");

                    //var line_sb = lines[line];

                    //while (line_sb.Length <= col)
                    //    line_sb += " ";

                    //var line_prefix = line_sb.ToString().Substring(0, col);
                    //var line_suffix = line_sb.ToString().Substring(Math.Min(col + text.Length, line_sb.Length));
                    //var new_line = line_prefix + text + line_suffix;

                    //if (line_sb.Length > col + 2)
                    //    continue;

                    //lines[line] = new_line;
                }
            }
        }
    }
}
