using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Sehr einfacher, sicherer Markdown-Renderer. Nur die häufigsten Elemente werden
/// unterstützt (Kopfzeilen, Fett/Kursiv, Aufzählungen, Links). Eingabe wird HTML-
/// konform maskiert, um XSS zu verhindern.
/// </summary>
public static class MarkdownRenderer
{
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None);
        var html = new StringBuilder();
        var inList = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (inList)
                {
                    html.AppendLine("</ul>");
                    inList = false;
                }
                html.AppendLine("<br/>");
                continue;
            }

            var trimmed = line.TrimEnd();
            var t = trimmed.TrimStart();

            if (t.StartsWith("- ") || t.StartsWith("* "))
            {
                if (!inList)
                {
                    html.AppendLine("<ul>");
                    inList = true;
                }
                html.AppendLine($"<li>{FormatInline(t[2..])}</li>");
                continue;
            }

            if (inList)
            {
                html.AppendLine("</ul>");
                inList = false;
            }

            if (t.StartsWith("### "))
            {
                html.AppendLine($"<h3>{FormatInline(t[4..])}</h3>");
                continue;
            }
            if (t.StartsWith("## "))
            {
                html.AppendLine($"<h2>{FormatInline(t[3..])}</h2>");
                continue;
            }
            if (t.StartsWith("# "))
            {
                html.AppendLine($"<h1>{FormatInline(t[2..])}</h1>");
                continue;
            }

            html.AppendLine($"<p>{FormatInline(t)}</p>");
        }

        if (inList)
        {
            html.AppendLine("</ul>");
        }

        return html.ToString();
    }

    private static string FormatInline(string text)
    {
        var s = WebUtility.HtmlEncode(text);
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<strong>$1</strong>", RegexOptions.Singleline);
        s = Regex.Replace(s, @"\*(.+?)\*", "<em>$1</em>", RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[(.+?)\]\((.+?)\)", "<a href=\"$2\" rel=\"noopener noreferrer\">$1</a>", RegexOptions.Singleline);
        return s;
    }
}