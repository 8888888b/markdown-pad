using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace MarkdownPad.Services;

/// <summary>
/// Simple in-process UI localisation. Strings are looked up by key for the
/// active language; <see cref="LanguageChanged"/> lets the window refresh every
/// piece of chrome without restarting.
/// </summary>
public static class Loc
{
    public const string Chinese = "zh-CN";

    public const string English = "en-US";

    private static readonly Dictionary<string, (string Zh, string En)> Strings = new(StringComparer.Ordinal)
    {
        // application / title bar
        ["app.title"] = ("Markdown Pad", "Markdown Pad"),
        ["app.untitled"] = ("未命名", "Untitled"),
        ["app.windowTitle"] = ("{0} - Markdown Pad", "{0} - Markdown Pad"),
        ["app.theme.tooltip"] = ("切换应用主题（浅色 / 深色）", "Toggle app theme (light / dark)"),
        ["app.language.tooltip"] = ("界面语言", "Interface language"),
        ["app.language.zh"] = ("中文（简体）", "Chinese (Simplified)"),
        ["app.language.en"] = ("English", "English"),
        ["app.theme.light"] = ("浅色", "Light"),
        ["app.theme.dark"] = ("深色", "Dark"),

        // pane headers
        ["pane.markdown"] = ("MARKDOWN", "MARKDOWN"),
        ["pane.preview"] = ("实时预览", "LIVE PREVIEW"),
        ["pane.dirty"] = ("未保存", "UNSAVED"),
        ["pane.synced"] = ("已同步", "SYNCED"),

        // preview status
        ["preview.starting"] = ("正在启动预览…", "Starting preview…"),
        ["preview.failed"] = ("预览不可用", "Preview unavailable"),
        ["preview.crashed"] = ("预览进程异常", "Preview process failed"),
        ["preview.renderFailed"] = ("渲染失败", "Render failed"),
        ["preview.errorTitle"] = ("预览渲染失败", "Preview render failed"),
        ["preview.errorHint"] = (
            "正文内容没有丢失，编辑区仍可正常使用。可尝试撤回最后一次修改、换一份文件，或用 MarkdownPad.exe --diag 查看日志。",
            "Your text is intact and the editor still works. Try undoing the last edit, opening another file, or run MarkdownPad.exe --diag for a log."),
        ["preview.statusLabel"] = ("预览异常：{0}", "Preview problem: {0}"),

        // toolbar tooltips
        ["cmd.new"] = ("新建 (Ctrl+N)", "New (Ctrl+N)"),
        ["cmd.open"] = ("打开 (Ctrl+O)", "Open (Ctrl+O)"),
        ["cmd.save"] = ("保存 (Ctrl+S)", "Save (Ctrl+S)"),
        ["cmd.saveas"] = ("另存为 (Ctrl+Shift+S)", "Save as (Ctrl+Shift+S)"),
        ["cmd.export"] = ("导出为 HTML (Ctrl+Shift+E)", "Export as HTML (Ctrl+Shift+E)"),
        ["cmd.heading"] = ("标题级别 (Ctrl+1 ~ Ctrl+4)", "Heading level (Ctrl+1 to Ctrl+4)"),
        ["cmd.h1"] = ("一级标题   Ctrl+1", "Heading 1   Ctrl+1"),
        ["cmd.h2"] = ("二级标题   Ctrl+2", "Heading 2   Ctrl+2"),
        ["cmd.h3"] = ("三级标题   Ctrl+3", "Heading 3   Ctrl+3"),
        ["cmd.h4"] = ("四级标题   Ctrl+4", "Heading 4   Ctrl+4"),
        ["cmd.paragraph"] = ("正文（清除标题）", "Body text (clear heading)"),
        ["cmd.bold"] = ("加粗 (Ctrl+B)", "Bold (Ctrl+B)"),
        ["cmd.italic"] = ("斜体 (Ctrl+I)", "Italic (Ctrl+I)"),
        ["cmd.strike"] = ("删除线", "Strikethrough"),
        ["cmd.code"] = ("行内代码 (Ctrl+D)", "Inline code (Ctrl+D)"),
        ["cmd.link"] = ("链接 (Ctrl+K)", "Link (Ctrl+K)"),
        ["cmd.image"] = ("图片", "Image"),
        ["cmd.ul"] = ("无序列表 (Ctrl+Shift+L)", "Bullet list (Ctrl+Shift+L)"),
        ["cmd.ol"] = ("有序列表", "Numbered list"),
        ["cmd.task"] = ("任务列表", "Task list"),
        ["cmd.quote"] = ("引用 (Ctrl+Shift+Q)", "Blockquote (Ctrl+Shift+Q)"),
        ["cmd.codeblock"] = ("代码块 (Ctrl+Shift+C)", "Code block (Ctrl+Shift+C)"),
        ["cmd.table"] = ("插入表格", "Insert table"),
        ["cmd.hr"] = ("分隔线", "Horizontal rule"),
        ["cmd.prevsect"] = ("上一段 (Ctrl+Up)", "Previous heading (Ctrl+Up)"),
        ["cmd.nextsect"] = ("下一段 (Ctrl+Down)", "Next heading (Ctrl+Down)"),
        ["cmd.preview"] = ("显示 / 隐藏预览 (Ctrl+E)", "Show / hide preview (Ctrl+E)"),
        ["cmd.wrap"] = ("自动换行", "Word wrap"),
        ["cmd.previewTheme"] = ("预览明暗主题 (Ctrl+T)", "Preview light / dark theme (Ctrl+T)"),

        // editor
        ["editor.placeholder"] = ("在这里输入 Markdown…", "Type Markdown here…"),

        // status bar
        ["status.ready"] = ("就绪", "Ready"),
        ["status.new"] = ("新建文档", "New document"),
        ["status.restoredFile"] = ("已恢复上次编辑的文件", "Restored the last edited file"),
        ["status.restoredDraft"] = ("已恢复上次的未保存草稿", "Restored the unsaved draft"),
        ["status.opened"] = ("已打开 {0}", "Opened {0}"),
        ["status.saved"] = ("已保存到 {0}", "Saved to {0}"),
        ["status.exported"] = ("已导出 {0}", "Exported {0}"),
        ["status.openFailed"] = ("打开文件失败：{0}", "Could not open the file: {0}"),
        ["status.missingFile"] = ("找不到文件：{0}", "File not found: {0}"),
        ["status.saveFailed"] = ("保存失败：{0}", "Could not save the file: {0}"),
        ["status.exportFailed"] = ("导出失败：{0}", "Could not export the file: {0}"),
        ["status.unsupported"] = ("不支持的文件类型：{0}", "Unsupported file type: {0}"),
        ["status.noHeadings"] = ("当前文档没有标题", "This document has no headings"),
        ["status.previewDark"] = ("预览：深色", "Preview: dark"),
        ["status.previewLight"] = ("预览：浅色", "Preview: light"),
        ["status.previewUnavailable"] = ("WebView2 初始化失败：{0}", "WebView2 initialization failed: {0}"),
        ["status.dropped"] = ("松开鼠标打开 Markdown 文件", "Drop to open a Markdown file"),
        ["status.dropHint"] = ("支持 .md / .markdown / .txt", "Supports .md / .markdown / .txt"),
        ["welcome.fallbackTitle"] = ("Markdown Pad", "Markdown Pad"),
        ["welcome.fallbackBody"] = ("开始输入 **Markdown**，右侧会实时预览。", "Start typing **Markdown**; the preview updates as you go."),
        ["status.line"] = (
            "第 {0} 行 第 {1} 列 · {2} 词 · {3} 字符 · {4} 行 · 约 {5} 分钟",
            "Ln {0}, Col {1} · {2} words · {3} chars · {4} lines · ~{5} min"),

        // file dialogs
        ["dialog.markdown"] = ("Markdown", "Markdown"),
        ["dialog.text"] = ("纯文本", "Plain text"),
        ["dialog.html"] = ("HTML", "HTML"),
        ["dialog.untitledFile"] = ("未命名.md", "untitled.md"),
        ["dialog.untitledExport"] = ("未命名", "untitled"),
    };

    private static string _language = Chinese;

    /// <summary>All known localisation keys; used by the self test.</summary>
    public static IReadOnlyCollection<string> Keys => Strings.Keys;

    /// <summary>Returns the raw (unformatted) text for a key, for validation.</summary>
    public static string Raw(string key) => Strings.TryGetValue(key, out var entry) ? entry.Zh + "\u0000" + entry.En : string.Empty;

    /// <summary>Raised after <see cref="Language"/> changes.</summary>
    public static event EventHandler? LanguageChanged;

    public static string Language
    {
        get => _language;
        set
        {
            var normalized = string.Equals(value, English, StringComparison.OrdinalIgnoreCase) ? English : Chinese;
            if (string.Equals(normalized, _language, StringComparison.Ordinal))
            {
                return;
            }

            _language = normalized;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Adds or overrides a string at runtime.</summary>
    public static void AddItem(string key, string zh, string en) => Strings[key] = (zh, en);

    public static bool IsChinese => string.Equals(_language, Chinese, StringComparison.Ordinal);

    /// <summary>Looks up <paramref name="key"/> and formats it with <paramref name="args"/>.</summary>
    public static string T(string key, params object?[] args)
    {
        if (!Strings.TryGetValue(key, out var entry))
        {
            return key;
        }

        var text = IsChinese ? entry.Zh : entry.En;
        return args.Length == 0 ? text : string.Format(text, args);
    }
}
