using System;
using System.IO;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkdownPad.Services;

/// <summary>
/// Turns markdown text into the HTML fragment shown in the preview pane.
/// A pipeline is built per call so the code-block renderer can be stateless
/// and Markdig stays thread safe for the (single) UI thread caller.
/// </summary>
public sealed class MarkdownRenderer
{
    private readonly object _gate = new();

    public string ToHtml(string? markdown, string? basePath = null)
    {
        markdown ??= string.Empty;

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()   // tables, task lists, footnotes, auto links, emphasis extras
            .UseEmojiAndSmiley()
            .Build();

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);

        renderer.ObjectRenderers.Replace<CodeBlockRenderer>(new PadCodeBlockRenderer());
        renderer.ObjectRenderers.Replace<LinkInlineRenderer>(new PadLinkInlineRenderer(basePath));

        lock (_gate)
        {
            var document = Markdown.Parse(markdown, pipeline);
            renderer.Render(document);
            writer.Flush();
            return writer.ToString();
        }
    }
}

/// <summary>
/// Fenced code blocks get a wrapper, an optional line-number gutter (kept out of
/// the code element so JavaScript highlighting can still read the raw text) and
/// a language label. Token colouring itself happens in the preview page.
/// </summary>
internal sealed class PadCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
    protected override void Write(HtmlRenderer renderer, CodeBlock block)
    {
        var lines = block.Lines;
        var count = Math.Max(1, lines.Count);

        var language = (block as FencedCodeBlock)?.Info?.Trim() ?? string.Empty;
        var languageClass = string.Empty;
        if (language.Length > 0)
        {
            var first = language.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (first.Length > 0)
            {
                language = first[0];
                languageClass = " class=\"language-" + HtmlEscape.Attribute(language) + "\"";
            }
        }

        var withGutter = count > 3;
        renderer.Write("<div class=\"code-block");
        if (withGutter)
        {
            renderer.Write(" with-gutter");
        }

        renderer.Write("\">");

        if (withGutter)
        {
            var numbers = new StringBuilder();
            for (var i = 1; i <= count; i++)
            {
                numbers.Append(i);
                if (i < count)
                {
                    numbers.Append('\n');
                }
            }

            renderer.Write("<span class=\"gutter\">").Write(HtmlEscape.Text(numbers.ToString())).Write("</span>");
        }

        renderer.Write("<pre><code").Write(languageClass).Write(">");

        var index = 0;
        while (++index < count + 1)
        {
            renderer.Write(HtmlEscape.Text(lines.Lines[index].ToString()));
            if (index < count)
            {
                renderer.Write('\n');
            }
        }

        renderer.Write("</code></pre>");

        if (language.Length > 0)
        {
            renderer.Write("<span class=\"code-lang\">").Write(HtmlEscape.Text(language)).Write("</span>");
        }

        renderer.Write("</div>");
    }
}

/// <summary>
/// Resolves relative markdown links against the folder of the document being
/// edited, so "other.md" and "images/x.png" keep working from the preview host.
/// </summary>
internal sealed class PadLinkInlineRenderer : LinkInlineRenderer
{
    private readonly string? _basePath;

    public PadLinkInlineRenderer(string? basePath) => _basePath = basePath;

    protected override void Write(HtmlRenderer renderer, LinkInline link)
    {
        if (!link.IsImage && !string.IsNullOrWhiteSpace(link.Url) && !string.IsNullOrWhiteSpace(_basePath))
        {
            var url = link.Url!;
            var isAbsolute = url.Contains("://", StringComparison.Ordinal)
                || url.StartsWith('#')
                || url.StartsWith('/')
                || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);

            if (!isAbsolute)
            {
                try
                {
                    var withoutFragment = url;
                    var fragment = string.Empty;
                    var hash = url.IndexOf('#');
                    if (hash >= 0)
                    {
                        withoutFragment = url.Substring(0, hash);
                        fragment = url.Substring(hash);
                    }

                    var full = Path.GetFullPath(Path.Combine(_basePath!, withoutFragment));
                    link.Url = new Uri(full).AbsoluteUri + fragment;
                }
                catch
                {
                    // Unresolvable url: keep whatever the author wrote.
                }
            }
        }

        base.Write(renderer, link);
    }
}
