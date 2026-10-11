using System.Text;

namespace MarkdownPad.Services;

/// <summary>Minimal, allocation-friendly HTML escaping used by the renderer helpers.</summary>
internal static class HtmlEscape
{
    public static string Text(string? value) => Escape(value, attribute: false);

    public static string Attribute(string? value) => Escape(value, attribute: true);

    private static string Escape(string? value, bool attribute)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"' when attribute:
                    builder.Append("&quot;");
                    break;
                case '\'' when attribute:
                    builder.Append("&#39;");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }
}
