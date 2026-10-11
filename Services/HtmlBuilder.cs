using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MarkdownPad.Services;

/// <summary>Messages pushed into the preview page through WebView2.</summary>
public sealed class PreviewMessage
{
    public string type { get; set; } = "update";

    public string? html { get; set; }

    public string? theme { get; set; }

    /// <summary>Optional copyable diagnostic shown above the document.</summary>
    public string? status { get; set; }

    public bool keepScroll { get; set; }
}

/// <summary>
/// Builds the HTML page hosted by the preview WebView. The shell (CSS and
/// scripts) is assembled once from the Assets folder and then reused; the
/// markdown payload is pushed as a JSON message so the page never reloads.
/// </summary>
public static class HtmlBuilder
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static string? _shell;
    private static string? _css;
    private static string? _rendererJs;

    public static string Shell => _shell ??= BuildShell();

    /// <summary>Serialises one preview update message.</summary>
    public static string Message(string html, string theme, bool keepScroll, string? status = null) =>
        JsonSerializer.Serialize(new PreviewMessage
        {
            type = "update",
            html = html,
            theme = theme,
            status = string.IsNullOrWhiteSpace(status) ? null : status,
            keepScroll = keepScroll,
        }, Json);

    public static string ThemeMessage(string theme) =>
        JsonSerializer.Serialize(new PreviewMessage { type = "theme", theme = theme }, Json);

    /// <summary>A self-contained document for "export to HTML" and print preview.</summary>
    public static string StandaloneDocument(string markdownHtml, string title, string theme)
    {
        var shell = Shell;
        var head = "<!DOCTYPE html><html lang=\"zh-CN\" data-theme=\"" + theme + "\"><head><meta charset=\"utf-8\" />"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />"
            + "<title>" + System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Markdown" : title) + "</title>"
            + "<style>" + Css + "</style></head><body><div id=\"page\"><article id=\"content\">"
            + markdownHtml
            + "</article></div>";

        var scripts = "<script>" + RendererJs + "</script>"
            + "<script>window.MarkdownPad && window.MarkdownPad.decorate(document.getElementById('content'));</script>";

        return head + scripts + "</body></html>";
    }

    /// <summary>Single line describing an exception, suitable for the status bar.</summary>
    public static string FormatError(Exception exception)
    {
        var message = exception.Message;
        if (message.Length > 240)
        {
            message = message[..240] + "…";
        }

        return exception.GetType().Name + ": " + message;
    }

    /// <summary>
    /// Rendered into the preview pane when a render fails, so the pane shows the
    /// actual reason instead of staying blank.
    /// </summary>
    public static string ErrorPage(string title, string error, string hint)
    {
        return "<div class=\"pad-error\">"
            + "<h2>" + System.Net.WebUtility.HtmlEncode(title) + "</h2>"
            + "<pre>" + System.Net.WebUtility.HtmlEncode(error) + "</pre>"
            + "<p>" + System.Net.WebUtility.HtmlEncode(hint) + "</p>"
            + "</div>";
    }

    private static string Css => _css ??= ReadAsset("theme.css");

    private static string RendererJs => _rendererJs ??= ReadAsset("renderer.js");

    private static string BuildShell()
    {
        var template = ReadAsset("shell.html");
        return template
            .Replace("@@CSS@@", Css, StringComparison.Ordinal)
            .Replace("@@RENDERER@@", RendererJs, StringComparison.Ordinal)
            .Replace("@@SHELL@@", ReadAsset("shell.js"), StringComparison.Ordinal);
    }

    private static string ReadAsset(string fileName)
    {
        var path = Path.Combine(AppPaths.AssetsDirectory, fileName);
        try
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
        }
        catch
        {
            // fall through to the diagnostics placeholder
        }

        return "/* missing asset: " + fileName + " */";
    }
}
