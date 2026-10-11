using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MarkdownPad.Helpers;
using MarkdownPad.Services;
using Microsoft.UI.Xaml.Controls;

namespace MarkdownPad;

/// <summary>
/// Headless self test harness, activated with: MarkdownPad.exe --selftest
/// It drives the same code paths as the UI (editor transforms, markdown
/// rendering, preview payload, save/open round trip) without showing a window,
/// writes a report and exits with a non-zero code when something fails.
/// Set MARKDOWNPAD_TRACE to choose the report path.
/// </summary>
internal static class SelfTest
{
    private static readonly StringBuilder Report = new();
    private static int _failures;

    public static async Task<int> RunAsync(MainWindow window, TextBox editor, string outputDirectory)
    {
        _failures = 0;

        try
        {
            EditorTransforms(editor);
            MarkdownRendering();
            RenderRobustness();
            Localisation();
            WindowChrome(window);
            await SaveAndOpenAsync(window, outputDirectory);
        }
        catch (Exception ex)
        {
            _failures++;
            Line("EXCEPTION: " + ex);
        }

        Line(_failures == 0 ? "RESULT: ALL PASS" : $"RESULT: {_failures} FAILURE(S)");
        Write(outputDirectory);
        return _failures == 0 ? 0 : 1;
    }

    private static void EditorTransforms(TextBox editor)
    {
        Check("bold wraps selection", () =>
        {
            var r = MarkdownEditor.ToggleBold("hello", 0, 5);
            return r.Text == "**hello**" && r.SelectionStart == 2 && r.SelectionLength == 5;
        });

        Check("bold unwraps selection", () => MarkdownEditor.ToggleBold("**hello**", 2, 5).Text == "hello");

        Check("bold with empty selection", () =>
        {
            var r = MarkdownEditor.ToggleBold("ab", 1, 0);
            return r.Text == "a****b" && r.SelectionStart == 3;
        });

        Check("italic wraps", () => MarkdownEditor.ToggleItalic("hello", 0, 5).Text == "*hello*");
        Check("strikethrough wraps", () => MarkdownEditor.ToggleStrike("hello", 0, 5).Text == "~~hello~~");
        Check("inline code wraps", () => MarkdownEditor.ToggleInlineCode("x", 0, 1).Text == "`x`");

        Check("set heading level 2", () => MarkdownEditor.SetHeading("text", 0, 0, 2).Text == "## text");
        Check("replace heading level", () => MarkdownEditor.SetHeading("### text", 0, 0, 1).Text == "# text");
        Check("clear heading", () => MarkdownEditor.SetHeading("## text", 0, 0, 0).Text == "text");

        Check("bullet list add then remove", () =>
        {
            // First call: a real selection covering both lines (as drag-select would give).
            var added = MarkdownEditor.ToggleBulletList("a\nb", 0, 3);

            // Second call: caret at the start of the first bullet, no selection,
            // so the toggle removes the whole contiguous run.
            var removed = MarkdownEditor.ToggleBulletList(added.Text, 0, 0);
            return added.Text == "- a\n- b" && removed.Text == "a\nb";
        });

        Check("bullet list on single line with caret", () =>
        {
            var added = MarkdownEditor.ToggleBulletList("hello", 0, 0);
            var removed = MarkdownEditor.ToggleBulletList(added.Text, 0, 0);
            return added.Text == "- hello" && added.SelectionStart == 2 && removed.Text == "hello";
        });

        Check("quote run removal keeps plain text", () =>
        {
            var added = MarkdownEditor.ToggleQuote("a\nb", 0, 3);
            var removed = MarkdownEditor.ToggleQuote(added.Text, 0, 0);
            return added.Text == "> a\n> b" && removed.Text == "a\nb";
        });

        Check("quote prefix", () => MarkdownEditor.ToggleQuote("quoted", 0, 0).Text == "> quoted");
        Check("task list prefix", () => MarkdownEditor.ToggleTaskList("todo", 0, 0).Text == "- [ ] todo");
        Check("code fence wrap", () => MarkdownEditor.ToggleCodeBlock("var x = 1;", 0, 10).Text == "```\nvar x = 1;\n```");

        Check("link insertion places caret on url", () =>
        {
            var r = MarkdownEditor.InsertLink("see here", 4, 4);
            return r.Text == "see [here](url)" && r.SelectionStart == 11 && r.SelectionLength == 3;
        });

        Check("word statistics", () =>
        {
            var stats = TextStats.Measure("hello world\n你好世界");
            return stats.Words == 6 && stats.Lines == 2 && stats.Characters == 16 && stats.ReadingMinutes >= 1;
        });

        Check("editor text box round trip", () =>
        {
            editor.Text = "sample";
            editor.SelectionStart = 0;
            editor.SelectionLength = 6;
            var result = MarkdownEditor.ToggleBold(editor.Text, editor.SelectionStart, editor.SelectionLength);
            editor.Text = result.Text;
            editor.SelectionStart = result.SelectionStart;
            editor.SelectionLength = result.SelectionLength;
            return editor.Text == "**sample**" && editor.SelectionStart == 2 && editor.SelectionLength == 6;
        });
    }

    private static void MarkdownRendering()
    {
        var renderer = new MarkdownRenderer();
        var html = renderer.ToHtml(
            "# Title\n\nSome **bold** text and `code`.\n\n```csharp\nvar x = 1;\n```\n\n| a | b |\n| - | - |\n| 1 | 2 |\n\n- [x] done\n- [ ] todo\n\n> quoted\n");

        Check("heading rendered", () => html.Contains("<h1", StringComparison.Ordinal) && html.Contains("Title", StringComparison.Ordinal));
        Check("emphasis rendered", () => html.Contains("<strong>bold</strong>", StringComparison.Ordinal));
        Check("inline code rendered", () => html.Contains("<code>code</code>", StringComparison.Ordinal));
        Check("code fence keeps language", () => html.Contains("language-csharp", StringComparison.Ordinal) && html.Contains("code-block", StringComparison.Ordinal));
        Check("table rendered", () => html.Contains("<table>", StringComparison.Ordinal));
        Check("task list rendered", () => html.Contains("task-list-item", StringComparison.Ordinal));
        Check("blockquote rendered", () => html.Contains("<blockquote>", StringComparison.Ordinal));

        Check("html is escaped", () =>
        {
            var escaped = renderer.ToHtml("a < b & c");
            return escaped.Contains("&lt;", StringComparison.Ordinal) && escaped.Contains("&amp;", StringComparison.Ordinal);
        });

        Check("shell is self contained", () =>
        {
            var shell = HtmlBuilder.Shell;
            return shell.Contains("data-theme", StringComparison.Ordinal)
                && shell.Contains("--content-width", StringComparison.Ordinal)
                && shell.Contains("MarkdownPad.decorate", StringComparison.Ordinal)
                && !shell.Contains("@@", StringComparison.Ordinal)
                && !shell.Contains("missing asset", StringComparison.Ordinal);
        });

        Check("preview message payload", () =>
        {
            var message = HtmlBuilder.Message("<p>hi</p>", "dark", true);
            using var document = System.Text.Json.JsonDocument.Parse(message);
            var root = document.RootElement;
            return root.GetProperty("type").GetString() == "update"
                && root.GetProperty("html").GetString() == "<p>hi</p>"
                && root.GetProperty("theme").GetString() == "dark"
                && root.GetProperty("keepScroll").GetBoolean();
        });

        Check("standalone export document", () =>
        {
            var page = HtmlBuilder.StandaloneDocument("<p>exported</p>", "Doc", "light");
            return page.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal)
                && page.Contains("<p>exported</p>", StringComparison.Ordinal)
                && page.Contains("</html>", StringComparison.Ordinal);
        });
    }

    private static void Localisation()
    {
        Check("both languages are complete", () =>
        {
            var missing = new List<string>();
            foreach (var key in Loc.Keys)
            {
                var raw = Loc.Raw(key);
                var separator = raw.IndexOf('\u0000');
                var zh = raw[..separator];
                var en = raw[(separator + 1)..];

                if (string.IsNullOrWhiteSpace(zh) || string.IsNullOrWhiteSpace(en))
                {
                    missing.Add(key);
                }
            }

            if (missing.Count > 0)
            {
                Line("    incomplete keys: " + string.Join(", ", missing));
            }

            return missing.Count == 0;
        });

        Check("keys used by the window exist", () =>
        {
            string[] used =
            [
                "app.title", "app.untitled", "app.windowTitle", "app.theme.tooltip", "app.language.tooltip",
                "app.theme.light", "app.theme.dark", "pane.markdown", "pane.preview", "pane.dirty", "pane.synced",
                "preview.starting", "preview.failed", "preview.crashed", "preview.renderFailed",
                "editor.placeholder", "status.ready", "status.new", "status.restoredFile", "status.restoredDraft",
                "status.opened", "status.saved", "status.exported", "status.openFailed", "status.saveFailed",
                "status.exportFailed", "status.unsupported", "status.noHeadings", "status.previewDark",
                "status.previewLight", "status.previewUnavailable", "status.dropped", "status.dropHint",
                "status.line", "dialog.markdown", "dialog.text", "dialog.html", "dialog.untitledFile",
                "dialog.untitledExport", "welcome.fallbackBody",
            ];

            var missing = used.Where(key => Loc.Raw(key).Length == 0).ToList();
            if (missing.Count > 0)
            {
                Line("    missing keys: " + string.Join(", ", missing));
            }

            return missing.Count == 0;
        });

        Check("every toolbar tooltip key exists", () =>
        {
            string[] commands =
            [
                "new", "open", "save", "saveas", "export", "bold", "italic", "strike", "code", "link", "image",
                "ul", "ol", "task", "quote", "codeblock", "table", "hr", "prevsect", "nextsect", "preview",
                "wrap", "previewTheme", "heading", "h1", "h2", "h3", "h4", "paragraph",
            ];

            var missing = commands.Where(c => Loc.Raw("cmd." + c).Length == 0).ToList();
            if (missing.Count > 0)
            {
                Line("    missing tooltip keys: " + string.Join(", ", missing));
            }

            return missing.Count == 0;
        });

        Check("pseudo language flips every string", () =>
        {
            var original = Loc.Language;
            try
            {
                Loc.Language = Loc.Chinese;
                var zhKeys = Loc.Keys.ToDictionary(key => key, Loc.Raw);

                Loc.Language = "pseudo-test";
                var leftBehind = zhKeys.Keys.Where(key => Loc.Raw(key) != zhKeys[key]).ToList();

                Loc.Language = Loc.English;
                var enUntitled = Loc.T("app.untitled");

                Loc.Language = Loc.Chinese;
                var zhUntitled = Loc.T("app.untitled");

                if (leftBehind.Count > 0)
                {
                    Line("    language switch changed raw text for: " + string.Join(", ", leftBehind.Take(5)));
                }

                return leftBehind.Count == 0 && enUntitled == "Untitled" && zhUntitled == "未命名";
            }
            finally
            {
                Loc.Language = original;
            }
        });

        Check("status line formats in both languages", () =>
        {
            var original = Loc.Language;
            try
            {
                Loc.Language = Loc.English;
                var en = Loc.T("status.line", 3, 7, 120, 480, 12, 1);
                Loc.Language = Loc.Chinese;
                var zh = Loc.T("status.line", 3, 7, 120, 480, 12, 1);
                return en.Contains("Ln 3", StringComparison.Ordinal) && en.Contains("120 words", StringComparison.Ordinal)
                    && zh.Contains("第 3 行", StringComparison.Ordinal) && zh.Contains("120 词", StringComparison.Ordinal);
            }
            finally
            {
                Loc.Language = original;
            }
        });
    }

    /// <summary>Exercises the live window: localisation refresh and title bar layout.</summary>
    private static void WindowChrome(MainWindow window)
    {
        var original = Loc.Language;

        try
        {
            Loc.Language = Loc.Chinese;
            window.DebugApplyLanguage();
            var zhTitle = window.DebugTitle;
            var zhTooltip = window.DebugFirstToolbarTooltip;
            var zhStatus = window.DebugStatusLeft;
            var zhHeader = window.DebugEditorHeader;

            Loc.Language = Loc.English;
            window.DebugApplyLanguage();
            var enTitle = window.DebugTitle;
            var enTooltip = window.DebugFirstToolbarTooltip;
            var enStatus = window.DebugStatusLeft;
            var enHeader = window.DebugEditorHeader;

            Check("window title follows the language", () =>
            {
                Line($"    title zh='{zhTitle}' en='{enTitle}'");
                return zhTitle.Contains("未命名", StringComparison.Ordinal)
                    && enTitle.Contains("Untitled", StringComparison.Ordinal);
            });

            Check("toolbar tooltips follow the language", () =>
            {
                Line($"    tooltip zh='{zhTooltip}' en='{enTooltip}'");
                var zhHasCjk = zhTooltip.Any(c => c >= 0x4E00 && c <= 0x9FFF);
                var enHasCjk = enTooltip.Any(c => c >= 0x4E00 && c <= 0x9FFF);
                return zhHasCjk && !enHasCjk && enTooltip.Contains("Ctrl+N", StringComparison.Ordinal);
            });

            Check("status bar follows the language", () =>
            {
                Line($"    status zh='{zhStatus}' en='{enStatus}'");
                return zhStatus == "就绪" && enStatus == "Ready";
            });

            Check("pane header follows the language", () =>
            {
                Line($"    header zh='{zhHeader}' en='{enHeader}'");
                return zhHeader == enHeader;
            });

            Check("caption buttons are visible and reserved", () =>
            {
                var (foreground, rightInset) = window.DebugTitleBarState;
                var spacer = window.DebugCaptionSpacerWidth;
                Line($"    titlebar: foreground={foreground} rightInset={rightInset} spacer={spacer}");
                return foreground is not null
                    && foreground.Value.A == 0xFF
                    && (spacer == 140 || spacer >= rightInset);
            });

            // Failure injection: a render error must become visible instead of
            // leaving a blank pane with no explanation.
            Loc.Language = Loc.Chinese;
            window.DebugApplyLanguage();
            window.DebugInjectRenderFailure(new InvalidOperationException("injected render failure"));
            window.DebugRender();

            Check("render failure is reported (status)", () =>
            {
                var status = window.DebugPreviewStatus;
                Line($"    preview status key='{window.DebugPreviewStatusKey}' detail='{window.DebugPreviewStatusDetail}' text='{status}'");
                return status.Contains("渲染失败", StringComparison.Ordinal)
                    && status.Contains("InvalidOperationException", StringComparison.Ordinal)
                    && status.Contains("injected render failure", StringComparison.Ordinal);
            });

            Check("render failure is reported (error page)", () =>
            {
                var page = window.DebugErrorPage;
                var detail = window.DebugPreviewError;
                Line($"    error detail: '{detail}'");
                return page.Contains("pad-error", StringComparison.Ordinal)
                    && page.Contains("injected render failure", StringComparison.Ordinal)
                    && detail.Contains("InvalidOperationException", StringComparison.Ordinal);
            });

            Check("clearing the failure restores the preview", () =>
            {
                window.DebugInjectRenderFailure(new InvalidOperationException("cleanup"));
                window.DebugRender();
                window.DebugInjectRenderFailure(null);
                window.DebugRender();
                var status = window.DebugPreviewStatus;
                Line($"    preview status after recovery: '{status}'");
                return status.Length == 0;
            });
        }
        catch (Exception ex)
        {
            _failures++;
            Line("window chrome FAILED: " + ex);
        }
        finally
        {
            Loc.Language = original;
            window.DebugApplyLanguage();
        }
    }

    /// <summary>Regression checks for the inputs that used to break the render path.</summary>
    private static void RenderRobustness()
    {
        var renderer = new MarkdownRenderer();

        Check("renders with an empty base path", () =>
        {
            var html = renderer.ToHtml("[link](other.md)", string.Empty);
            return html.Contains("<a ", StringComparison.Ordinal);
        });

        Check("renders with a null base path", () =>
        {
            var html = renderer.ToHtml("[link](other.md)", null);
            return html.Contains("<a ", StringComparison.Ordinal);
        });

        Check("renders with a relative base path", () =>
        {
            var html = renderer.ToHtml("[link](other.md)", "relative-folder");
            return html.Contains("<a ", StringComparison.Ordinal);
        });

        Check("renders with an invalid base path", () =>
        {
            var html = renderer.ToHtml("[link](other.md)", "Z:\\does\\not\\exist\\");
            return html.Contains("<a ", StringComparison.Ordinal);
        });

        Check("renders text without a base path", () =>
        {
            var html = renderer.ToHtml("# 标题\n\n正文 **加粗**");
            return html.Contains("<h1", StringComparison.Ordinal) && html.Contains("<strong>", StringComparison.Ordinal);
        });

        Check("renders a relative link with a fragment", () =>
        {
            var html = renderer.ToHtml("[x](other.md#section)", AppContext.BaseDirectory);
            return html.Contains("#section", StringComparison.Ordinal);
        });

        Check("renders a 1 MB document", () =>
        {
            var block = "## Section\n\nSome **bold** text with a [link](https://example.com) and `code`.\n\n- item one\n- item two\n\n```csharp\nvar x = 1;\n```\n\n";
            var builder = new StringBuilder();
            while (builder.Length < 1_000_000)
            {
                builder.Append(block);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var html = renderer.ToHtml(builder.ToString(), AppContext.BaseDirectory);
            stopwatch.Stop();
            Line($"    1 MB document rendered in {stopwatch.ElapsedMilliseconds} ms, {html.Length} chars");
            return html.Length > 100_000;
        });

        Check("renders deeply nested lists and quotes", () =>
        {
            var builder = new StringBuilder();
            for (var i = 0; i < 60; i++)
            {
                builder.Append(new string(' ', i * 2)).Append("- level ").Append(i).Append('\n');
            }

            for (var i = 0; i < 40; i++)
            {
                builder.Append(new string('>', i + 1)).Append(" quote\n");
            }

            var html = renderer.ToHtml(builder.ToString(), AppContext.BaseDirectory);
            return html.Length > 0;
        });

        Check("renders unterminated fences and html", () =>
        {
            var html = renderer.ToHtml("```\nunterminated code\n\n<b>raw html</b>\n\n[link](", AppContext.BaseDirectory);
            return html.Length > 0;
        });

        Check("preview message survives a large payload", () =>
        {
            var html = renderer.ToHtml(new string('x', 500_000), AppContext.BaseDirectory);
            var message = HtmlBuilder.Message(html, "light", true);
            using var document = System.Text.Json.JsonDocument.Parse(message);
            return document.RootElement.GetProperty("html").GetString()?.Length > 100_000;
        });

        Check("error page reports the reason", () =>
        {
            var error = HtmlBuilder.FormatError(new InvalidOperationException("boom <script>"));
            var page = HtmlBuilder.ErrorPage("title", error, "hint");
            return error.StartsWith("InvalidOperationException: boom", StringComparison.Ordinal)
                && page.Contains("pad-error", StringComparison.Ordinal)
                && page.Contains("&lt;script&gt;", StringComparison.Ordinal)
                && !page.Contains("<script>", StringComparison.Ordinal);
        });

        Check("format error truncates long messages", () =>
        {
            var error = HtmlBuilder.FormatError(new Exception(new string('x', 1000)));
            return error.Length < 300;
        });
    }

    private static async Task SaveAndOpenAsync(MainWindow window, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var file = Path.Combine(outputDirectory, "selftest-output.md");
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        try
        {
            var editor = MainWindow.EditorBox;
            editor.Text = "# 自测文档\n\n保存与重新打开的往返测试。";

            await window.SaveToPathForTestAsync(file);

            Check("save writes the file", () => File.Exists(file));
            Check("saved content round trip", () =>
                File.ReadAllText(file, Encoding.UTF8).Contains("自测文档", StringComparison.Ordinal));
            Check("save clears the dirty flag", () => !window.IsDirtyForTest);

            editor.Text = string.Empty;
            await window.LoadFromPathForTestAsync(file);

            Check("open restores the content", () =>
                (MainWindow.EditorBox.Text ?? string.Empty).Contains("保存与重新打开的往返测试", StringComparison.Ordinal));
            Check("open clears the dirty flag", () => !window.IsDirtyForTest);
        }
        catch (Exception ex)
        {
            _failures++;
            Line("save/open FAILED: " + ex);
        }
    }

    private static void Check(string name, Func<bool> assertion)
    {
        bool ok;
        string? detail = null;

        try
        {
            ok = assertion();
        }
        catch (Exception ex)
        {
            ok = false;
            detail = ex.GetType().Name + ": " + ex.Message;
        }

        if (!ok)
        {
            _failures++;
        }

        Line($"[{(ok ? "PASS" : "FAIL")}] {name}" + (detail is null ? string.Empty : "  -> " + detail));
    }

    private static void Line(string message)
    {
        Debug.WriteLine(message);
        Report.AppendLine(message);
    }

    private static void Write(string outputDirectory)
    {
        try
        {
            var target = Environment.GetEnvironmentVariable("MARKDOWNPAD_TRACE");
            var path = string.IsNullOrWhiteSpace(target)
                ? Path.Combine(outputDirectory, "selftest-report.txt")
                : target!;
            File.WriteAllText(path, Report.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("report write failed: " + ex.Message);
        }
    }
}
