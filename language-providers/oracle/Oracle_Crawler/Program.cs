using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using HtmlAgilityPack;
using System.Net;
using System.IO;
using System.Diagnostics;

namespace Oracle_Crawler
{
    class Program
    {
        // http://docs.oracle.com/cd/E12825_01/epm.111/esb_techref/frameset.htm?mdx_grammar_rules.htm

        const string ROOT_URL_BASE = @"http://docs.oracle.com/cd/E12825_01/epm.111/esb_techref/";
        const string ROOT_URL_INDEX = @"mdx_grammar_rules.htm";
        const string ROOT_URL = ROOT_URL_BASE + ROOT_URL_INDEX;

        static string OUT_DIR;
        static string OUT_DIR_HTML;

        static void Main(string[] args)
        {
            Console.WriteLine("press [R] to download HTML from the web");
            Console.WriteLine("or any other key to use local html files");

            var key = Console.ReadKey().KeyChar;
            use_local_html = key == 'r';

            OUT_DIR = string.Format("{0:yyyy.MM.dd}", DateTime.Now);
            OUT_DIR_HTML = Path.Combine(OUT_DIR, "source_html");

            SafeCreate(OUT_DIR);
            SafeCreate(OUT_DIR_HTML);

            FetchUrls(ROOT_URL);

            foreach (var code in CodeNodes)
            {
                //var c = WebUtility.HtmlDecode(code.Html.InnerHtml);
                var c = code.Html.InnerHtml;

                const string OPEN_BRACE = "&lt;";
                const string CLOSE_BRACE = "&gt;";
                const string NOT_BNF = OPEN_BRACE + "@ NOT BNF @" + CLOSE_BRACE;
                const string ALT_BNF = OPEN_BRACE + "@ ALT BNF @" + CLOSE_BRACE;
                const string EMPTY_BNF_BODY = OPEN_BRACE + "@ EMPTY BNF BODY @" + CLOSE_BRACE;

                var decl_operator = "::=";

                var s = c.Split(new[] { decl_operator }, StringSplitOptions.RemoveEmptyEntries);

                int decl_pos = -1;
                int search_pos = 0;
                while ((decl_pos = c.IndexOf(decl_operator, search_pos)) > 0)
                {
                    // search elem
                    var elem_start = c.LastIndexOf(OPEN_BRACE, decl_pos);
                    var elem = c.Substring(elem_start, decl_pos - elem_start).Trim();

                    // search body
                    var decl_body = c.Substring(decl_pos + decl_operator.Length);
                    var decl_op_pos = decl_body.IndexOf(decl_operator);

                    if (decl_op_pos > 0)
                        decl_op_pos = decl_body.LastIndexOf(OPEN_BRACE, decl_op_pos);

                    // fallback if no more declarations are contained in this section
                    if (decl_op_pos < 0)
                        decl_op_pos = decl_body.Length;
                    
                    // cut out the declaration from the range that was found
                    //else
                    decl_body = decl_body.Substring(0, decl_op_pos).TrimEnd();

                    // this declaration doesn't fit the usual BNF pattern
                    if (decl_op_pos < 0 || decl_body.Length == 0)
                    {
                        // the more useful way, write out the actual documentation
                        decl_body = NOT_BNF + " ";
                        HtmlNode doc_node = code.Html;
                        while ((doc_node = doc_node.NextSibling) != null && !doc_node.InnerHtml.Contains("::="))
                        {
                            decl_body += doc_node.InnerHtml;
                        }

                        //// the simple fallback, just write out a note
                        //decl_body = NOT_BNF;
                    }

                    string existing_decl;
                    if (!Declarations.TryGetValue(elem, out existing_decl))
                    {
                        if (decl_body.Length == 0)
                            decl_body = EMPTY_BNF_BODY;

                        Declarations.Add(elem, decl_body);
                    }

                    search_pos = decl_pos + 1;
                }
            }

            var mdx_grammar_txt_path = Path.Combine(OUT_DIR, "mdx_grammar.txt");
            var mdx_grammar_html_path = Path.Combine(OUT_DIR, "mdx_grammar.html");

            using (TextWriter txt_wr = new StreamWriter(mdx_grammar_txt_path))
            using (TextWriter html_wr = new StreamWriter(mdx_grammar_html_path))
            {
                HtmlDocument decl_key_html = new HtmlDocument();
                HtmlDocument decl_value_html = new HtmlDocument();

                foreach (var decl in Declarations)
                {
                    decl_key_html.LoadHtml(decl.Key);
                    decl_value_html.LoadHtml(decl.Value);

                    var decl_key = WebUtility.HtmlDecode(decl_key_html.DocumentNode.InnerText);
                    var decl_value = WebUtility.HtmlDecode(decl_value_html.DocumentNode.InnerText);

                    txt_wr.WriteLine(decl_key + " ::= " + decl_value);

                    // fix URLs
                    var url_nodes = decl_value_html
                        .DocumentNode
                        .Descendants()
                        .Where(
                            x => x.Name == "a" &&
                            x.Attributes["href"] != null
                        )
                        .ToList();

                    foreach (var url_node in url_nodes)
                    {
                        var link = url_node.Attributes["href"].Value;

                        if (link == null)
                            continue;

                        link = "./source_html/" + link;
                        url_node.Attributes["href"].Value = link;
                    }

                    html_wr.WriteLine("<pre>" + decl_key_html.DocumentNode.InnerHtml + " ::= " + decl_value_html.DocumentNode.InnerHtml + "</pre>");
                }
            }
        }

        static void SafeCreate(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        static string DownloadHtml_Remote(string url)
        {
            HttpClient http = new HttpClient();
            var response = http.GetByteArrayAsync(url);
            Task.WaitAll(response);
            var source = Encoding.GetEncoding("utf-8").GetString(response.Result, 0, response.Result.Length);
            return source;
        }

        static string DownloadHtml_Local(string url)
        {
            const string LOCAL_URL_BASE = @"./latest_source_html/";
            url = url.Replace(ROOT_URL_BASE, LOCAL_URL_BASE);

            using (TextReader tr = new StreamReader(url))
                return tr.ReadToEnd();
        }

        static void FetchUrls(string curr_url)
        {
            string source;

            if (!use_local_html)
                source = DownloadHtml_Remote(curr_url);
            else
                source = DownloadHtml_Local(curr_url);

            HtmlDocument resultat = new HtmlDocument();
            resultat.LoadHtml(source);

            var rel_url = curr_url.Replace(ROOT_URL_BASE, "");

            if (rel_url.Contains('/') || rel_url.Contains('\\'))
                throw new NotSupportedException("crawled sub url contains path separator (/ or \\) ... this is currently not supported");

            resultat.Save(Path.Combine(OUT_DIR_HTML, rel_url));

            var code_nodes =
                resultat
                .DocumentNode
                .Descendants()
                .Where(
                    x => x.Name == "pre" &&
                    x.Attributes["class"] != null &&
                    x.Attributes["class"].Value.Contains("programlisting")
                )
                .Select(x => new CodeRef(curr_url, x))
                .ToList();

            CodeNodes.AddRange(code_nodes);

            //var referenced_urls =
            //    code_nodes
            //    .SelectMany(
            //        x => x.Html.Descendants()
            //        .Where(
            //            y => y.Name == "span" && 
            //            y.Attributes["class"] != null &&
            //            y.Attributes["class"].Value.Contains("FunctionName") && 
            //            y.ParentNode != null &&
            //            y.ParentNode.Name == "a"
            //        )
            //        .Select(y => y.ParentNode)
            //    )
            //    .ToList();

            var referenced_urls =
                resultat
                .DocumentNode
                .Descendants()
                .Where(
                    y => y.Name == "a" &&
                    y.Attributes["href"] != null &&
                    y.Attributes["href"].Value.Contains(".htm")
                )
                .ToList();

            foreach (var url in referenced_urls)
            {
                var href = GetHref(url);

                if (href == null || UrlNodes.ContainsKey(href))
                    continue;

                UrlNodes.Add(href, url);
            }

            VisitedUrls.Add(curr_url);

            Console.Clear();
            Console.WriteLine("current URL: " + curr_url);
            Console.WriteLine("code_nodes: " + CodeNodes.Count);
            Console.WriteLine("total urls: " + UrlNodes.Count);
            Console.WriteLine("visited urls: " + VisitedUrls.Count);

            foreach (var url_node in referenced_urls)
            {
                var href = GetHref(url_node);

                if (href == null)
                    continue;

                if (VisitedUrls.Any(x => x.Contains(href)))
                    continue;

                var full_url = ROOT_URL_BASE + href;

                FetchUrls(full_url);
            }
        }

        private static string GetHref(HtmlNode node)
        {
            var href = node.Attributes["href"];

            if (href == null)
                return null;

            var link = href.Value;

            if (link == null)
                return null;

            var len = 0;
            var pos = link.IndexOf(".html");

            if (pos >= 0)
                len = 5;
            else
            {
                pos = link.IndexOf(".htm");

                if (pos >= 0)
                    len = 4;
            }


            if (pos < 0)
                // should not happen ?!
                return null;

            link = link.Substring(0, pos + len);
            return link;
        }

        static bool use_local_html = false;
        static List<CodeRef> CodeNodes = new List<CodeRef>();
        static Dictionary<string, HtmlNode> UrlNodes = new Dictionary<string, HtmlNode>();
        static HashSet<string> VisitedUrls = new HashSet<string>();
        static SortedDictionary<string, string> Declarations = new SortedDictionary<string, string>();
    }

    class CodeRef
    {
        public CodeRef(string url, HtmlNode html)
        {
            Url = url;
            Html = html;
        }

        public string Url;
        public HtmlNode Html;
    }
}
