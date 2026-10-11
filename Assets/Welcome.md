# 欢迎使用 Markdown Pad

这是一个 **WinUI 3** 桌面 Markdown 编辑器：左侧编辑，右侧 **实时预览**。

> 直接在这里输入文字，右侧预览会在停止输入约 200 毫秒后自动刷新。

## 支持的语法

| 语法 | 写法 | 效果 |
| --- | --- | --- |
| 标题 | `# 标题` | 六级标题 |
| 强调 | `**粗体**` `*斜体*` `~~删除线~~` | 行内样式 |
| 列表 | `- 项目` / `1. 项目` | 无序与有序列表 |
| 任务 | `- [x] 完成` | 复选框 |
| 表格 | 竖线分隔 | 本文中的表格 |
| 引用 | `> 引用` | 引用块 |
| 代码 | 反引号 | 行内与围栏代码块 |
| 脚注 | `[^1]` | 页面底部注释 |

## 代码高亮

```csharp
// 预览中的代码块带语法高亮与行号
public sealed partial class MainWindow : Window
{
    private readonly MarkdownRenderer _renderer = new();

    public void UpdatePreview(string markdown)
        => _preview.CoreWebView2?.PostWebMessageAsJson(
            HtmlBuilder.Payload(markdown, _renderer));
}
```

```js
const el = document.getElementById('content');
el.innerHTML = MarkdownPad.render(markdown);   // 见 renderer.js
```

```python
def word_count(text: str) -> int:
    return len(text.split())
```

## 快捷键

| 按键 | 功能 |
| --- | --- |
| `Ctrl+N` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` | 新建 / 打开 / 保存 / 另存为 |
| `Ctrl+B` / `Ctrl+I` / `Ctrl+K` | 粗体 / 斜体 / 链接 |
| `Ctrl+1` … `Ctrl+4` | 一至四级标题 |
| `Ctrl+Shift+C` / `Ctrl+Shift+Q` | 代码块 / 引用 |
| `Ctrl+E` | 只显示编辑区或分栏 |
| `Ctrl+T` | 切换预览明暗主题 |
| `F11` | 全屏 |

## 任务列表

- [x] 编辑器与实时预览
- [x] 工具栏与快捷键
- [x] 拖放打开文件
- [ ] 导出 PDF

---

左侧编辑区的改动会**自动保存到草稿**，即使忘记保存也不会丢失内容。
