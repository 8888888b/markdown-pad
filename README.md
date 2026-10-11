# Markdown Pad

一个用 **WinUI 3**（Windows App SDK 2.2 / .NET 9）写的 Windows 桌面 Markdown 编辑器，**左侧编辑、右侧实时预览**。

## 功能

**实时预览**

- 停止输入约 180 ms 后自动刷新，滚动位置不会跳回顶部
- 预览页面只加载一次，之后通过 WebView2 消息推送 HTML，不整页重载
- 预览完全离线：CSS/JS 内联在应用内，不访问网络
- Markdig 高级扩展：表格、任务列表、脚注、自动链接、删除线、表情等
- 代码块带语言标签、行号（超过 3 行时）与轻量语法高亮（C#、JS/TS、Python、Go、Rust、SQL、Bash 等）
- 浅色 / 深色预览主题，跟随应用窗口（`Ctrl+T` 切换）

**界面**

- 标题栏自带按钮放在**左侧**、文档标题自动截断，右侧只留系统最小化/最大化/关闭三个按钮的位置
  （宽度按 `AppWindowTitleBar.RightInset` 实时同步，DPI 或窗口状态变化都会重新计算），不会互相压住
- 这三个系统按钮显式着色：浅色主题下为黑色、深色主题下为白色，并带可见的悬停/按下填充，不会"看不见"
- 右上角另有"界面语言"按钮，可在**中文（简体）**与 **English** 之间即时切换，无需重启，选择会记住

**编辑**

- 工具栏：标题级别、粗体、斜体、删除线、行内代码、链接、图片、无序/有序/任务列表、引用、代码块、表格、分隔线、上一段/下一段
- 快捷键见下表；Tab 插入 4 个空格而不是切换焦点；`Ctrl+Z/Y` 走 TextBox 原生撤销栈
- 状态栏显示行号、列号、词数、字符数、行数与预计阅读时间（中英文混排按汉字计词）

**文件**

- 打开 / 保存 / 另存为 / 导出为独立 HTML（含样式与高亮脚本，可直接分享）
- 拖放 `.md` / `.markdown` / `.mdown` / `.txt` 到窗口即可打开
- 相对路径链接按当前文档所在目录解析，点击链接用默认浏览器打开
- 未保存内容每 2 秒写入草稿，并有带时间戳的备份；崩溃或误关后可恢复
- 窗口大小、位置、分栏宽度、主题等设置持久化到 `%LOCALAPPDATA%\MarkdownPad\settings.json`

## 快捷键

| 按键 | 功能 | 按键 | 功能 |
| --- | --- | --- | --- |
| `Ctrl+N` | 新建 | `Ctrl+B` | 粗体 |
| `Ctrl+O` | 打开 | `Ctrl+I` | 斜体 |
| `Ctrl+S` | 保存 | `Ctrl+K` | 链接 |
| `Ctrl+Shift+S` | 另存为 | `Ctrl+D` | 行内代码 |
| `Ctrl+Shift+E` | 导出 HTML | `Ctrl+Shift+C` | 代码块 |
| `Ctrl+1` ~ `Ctrl+4` | 一至四级标题 | `Ctrl+Shift+Q` | 引用 |
| `Ctrl+↑` / `Ctrl+↓` | 上一段 / 下一段标题 | `Ctrl+Shift+L` | 无序列表 |
| `Ctrl+E` | 显示 / 隐藏预览 | `Ctrl+T` | 预览明暗主题 |
| `F11` | 全屏 | `Tab` | 插入缩进 |

## 构建与运行

要求：Windows 10 1809+ 或 Windows 11，**.NET 9 SDK**（或更高），VS 2022 可选。

```powershell
# 还原 + 构建
dotnet build src\MarkdownPad\MarkdownPad.csproj -c Debug -p:Platform=x64

# 运行
.\src\MarkdownPad\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\MarkdownPad.exe
```

发布单目录版本（自包含 Windows App SDK，目标机不需要预装运行时）：

```powershell
dotnet publish src\MarkdownPad\MarkdownPad.csproj -c Release -p:Platform=x64 `
  -p:WindowsAppSDKSelfContained=true -p:SelfContained=false -o publish
```

用 Visual Studio 打开 `MarkdownPad.sln` 亦可（配置选 `x64`）。

## 打包 MSIX 与签名

一条命令完成：生成证书 → 构建 MSIX → 签名 → 验证 → （可选）信任证书并安装。

```powershell
# 只构建并签名
powershell -ExecutionPolicy Bypass -File .\tools\build-msix.ps1

# 构建 + 签名 + 机器级信任证书 + 安装（需要管理员）
powershell -ExecutionPolicy Bypass -File .\tools\build-msix.ps1 -Version 1.0.4.0 -TrustCertificate -Install

# 用正式证书出包（分发给别人时）
powershell -ExecutionPolicy Bypass -File .\tools\build-msix.ps1 `
  -CertificateSubject 'CN=Contoso Ltd' -PfxPath C:\certs\contoso.pfx -PfxPassword '***'
```

产物：

```
artifacts/msix/MarkdownPad_<版本>_x64_Test/
├── MarkdownPad_<版本>_x64.msix     已签名，约 51 MB（含自包含 Windows App SDK）
├── Install.ps1 / Add-AppDevPackage.ps1   微软生成的安装脚本
artifacts/certs/
├── MarkdownPad.Dev.pfx   开发用自签名证书（含私钥，已在 .gitignore 中排除）
└── MarkdownPad.Dev.cer   公钥证书，分发给测试机用
```

### 直接在命令行打包（不用脚本）

```powershell
dotnet build src\MarkdownPad\MarkdownPad.csproj -c Release -p:Platform=x64 `
  -p:PackageMsix=true `
  -p:AppxPackageVersion=1.0.4.0 `
  "-p:AppxPackagePublisher=`"CN=MarkdownPad Development`""
```

`Package.appxmanifest` 里的 `__VERSION__` / `__PUBLISHER__` 由构建期生成替换
（`obj\Package.generated.appxmanifest`），所以版本号和签名主体都不用改文件。

### 关于签名

- 默认用的是**自签名开发证书** `CN=MarkdownPad Development`，3 年有效期，只适合本机/内网测试。
  要分发给外部用户，必须换成受信任 CA 的代码签名证书（否则别人装不上）。
- **清单里的 Publisher 必须和证书主体完全一致**，否则 `signtool` 直接拒绝签名。脚本会自动把
  `-CertificateSubject` 同时写进清单，保持两者一致。
- 签名用 SHA-256 + RFC3161 时间戳（`http://timestamp.digicert.com`）。有时间戳意味着
  **证书过期后旧包依然有效**；脚本在时间戳服务器不可达时会自动降级为不加时间戳并给出警告。
- 另一台机器要安装这个包，需要先把 `.cer` 装进"受信任的根证书颁发机构"和"受信任的人"：

  ```powershell
  # 目标机（管理员）
  certutil -addstore Root .\artifacts\certs\MarkdownPad.Dev.cer
  certutil -addstore TrustedPeople .\artifacts\certs\MarkdownPad.Dev.cer
  Add-AppxPackage .\artifacts\msix\MarkdownPad_1.0.3.0_x64_Test\MarkdownPad_1.0.3.0_x64.msix
  ```

  双击 `.msix` 安装则需要在弹出的对话框里点"信任"。

### 包内容

- 自包含 Windows App SDK：目标机**不需要**预装 Windows App Runtime，代价是包体约 51 MB
- 完整信任（`runFullTrust`）+ `windows.fileTypeAssociation`：`.md/.markdown/.mdown/.txt` 可关联，
  双击即用 `MarkdownPad.exe <文件>` 打开（命令行参数已在应用内处理）
- 图标资源由 `tools/generate-assets.py` 用 Pillow 生成（44/71/150/310 方块、310×150 宽版、
  50 商店图标、620×300 启动画面），改配色或图形后重新跑一次即可
- 升级安装用 `Add-AppxPackage -ForceUpdateFromAnyVersion`，或直接把包版本号提高

## 项目结构

```
src/MarkdownPad/
├── App.xaml(.cs)                 应用入口与全局异常诊断
├── MainWindow.xaml(.cs)          UI 布局、WebView2 预览、文件与命令处理
├── Controls/SplitterHandle.cs    可拖拽分栏（暴露受保护的鼠标指针属性）├── Helpers/
│   ├── MarkdownEditor.cs         纯函数式 Markdown 文本变换（加粗/标题/列表…）
│   └── TextStats.cs              中英文混排的字数与阅读时间统计
├── Services/
│   ├── MarkdownRenderer.cs       Markdig 管线、代码块与链接渲染器
│   ├── HtmlBuilder.cs            预览外壳组装与消息负载
│   ├── Loc.cs                    界面文案表与语言切换
│   ├── AppSettings.cs            设置、草稿与备份路径
│   └── Diag.cs                   可选诊断日志
├── SelfTest.cs                   无界面自测（--selftest）
├── Themes/Styles.xaml            工具栏按钮外观与主题色
└── Assets/                       预览用 CSS/JS、欢迎文档
```

## 自测

内置无界面自测，覆盖文本变换、Markdown 渲染、预览消息负载与文件读写往返：

```powershell
.\src\MarkdownPad\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\MarkdownPad.exe --selftest
# 退出码 0 表示全部通过；报告写在可执行文件目录的 selftest-report.txt
```

## 增加界面语言

所有界面文案集中在 `Services/Loc.cs` 的一张表里，每项是 `(中文, English)`。加一门语言：

1. 在 `Loc` 里把该语言的码加进 `Language` 的归一化分支，并在表里补一列；
2. 在标题栏的语言 `MenuFlyout` 里加一个 `MenuFlyoutItem`，`Tag` 填语言码。

状态栏消息以 key 形式记录（`SetStatus("status.saved", name)`），切换语言时会用新语言重新格式化，
所以切换后连"已保存到 …"这类瞬时消息也会跟着变。

## 预览显示"渲染失败"怎么办

**先看原因，不要猜。** 渲染失败不会被静默吞掉，三个地方都能看到具体异常：

1. **预览窗格顶部**（可直接选中复制）：
   ```
   渲染失败：异常类型 + 异常消息
   ```
2. **预览区标题栏右侧**：`预览异常：渲染失败 - <异常类型>: <消息>`
3. **日志**（需要 `--diag`）：`MarkdownPad-trace.log` 里有完整堆栈

拿到这句异常消息，基本就能定位了。

### 常见原因与处理

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 预览区显示 `WebView2 初始化失败：…` | 缺 Edge WebView2 运行时（精简版系统常见） | 装 [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)，或改用 Chromium 版 Edge |
| 预览一直是空白、状态栏无报错 | 预览还在启动（首次拉起 WebView2 可能 1–3 秒） | 等下；仍空白就用 `--diag` 看是否有 `preview shell ready` |
| `渲染失败 - IOException` / `UnauthorizedAccessException` | 文档所在目录不可读（网络盘断开、权限变更、文件被删） | 另存到本地可写目录再打开 |
| 文档极大（数十 MB）后变卡或报错 | 预览消息体过大 | 拆分文档；预览只用于日常书写，超大文件用别的工具看 |
| 打开文件后完全没有反应 | 用了旧版 exe | 从 bin 目录重新构建；现版本支持 `MarkdownPad.exe <文件路径>` |

### 自助恢复

```powershell
# 1) 打开诊断日志并复现一次
MarkdownPad.exe --diag

# 2) 看日志尾部
Get-Content .\MarkdownPad-trace.log -Tail 40

# 3) 如果设置或草稿疑似损坏，清掉它们（正文若在文件里就不会丢）
Remove-Item "$env:LOCALAPPDATA\MarkdownPad\settings.json"
Remove-Item "$env:LOCALAPPDATA\MarkdownPad\draft.md"
```

未保存的正文还会每 2 秒写到 `%LOCALAPPDATA%\MarkdownPad\draft.md`，按分钟另存到 `backups\`，
所以即便预览出问题，**你的文字一般不会丢**。

### 已做的加固（针对这类故障）

- 相对链接解析的基准目录改为 `ResolveBasePath()`：文档目录 → 当前目录 → 程序目录逐级回退，
  不再可能把 `null` / 空字符串交给 `Path.Combine`、`Path.GetFullPath` 而抛异常
- 渲染异常不再只写 `Debug`：状态、预览区、日志三处同时给出异常类型与消息
- 失败页会说明"正文没有丢失"，并保留原文在编辑区
- 成功后自动清掉诊断横幅，恢复正常预览

## 排错

启动或预览异常时打开诊断日志：

```powershell
MarkdownPad.exe --diag        # 或 set MARKDOWNPAD_DIAG=1
# 日志：<可执行文件目录>\MarkdownPad-trace.log
# 也可用 MARKDOWNPAD_TRACE=<路径> 指定输出文件
```

## 命令行

```powershell
MarkdownPad.exe                  # 恢复上次的文件或草稿
MarkdownPad.exe D:\notes\a.md    # 直接打开该文件（双击 .md / "打开方式" 也是这条路径）
MarkdownPad.exe --selftest       # 无界面自测
MarkdownPad.exe --diag           # 打开诊断日志
```

## 实现要点

- **预览不重载**：`NavigateToString` 加载一次自带样式与脚本的外壳，之后用 `PostWebMessageAsJson` 推送
  `{ type, html, theme, keepScroll, status }`，因此输入时不会闪烁、也不会丢失预览滚动位置。
- **离线渲染**：样式表与脚本在 `HtmlBuilder` 里内联进外壳，躲避虚拟主机导航在网络受限环境下的失败。
- **渲染分两段**：Markdig 负责 Markdown → HTML（含代码块包裹、行号槽、语言标签），预览页里的 JS 负责
  语法着色、标题锚点与链接拦截。
- **UI 线程安全**：预览更新由 180 ms 防抖计时器串行触发，用 `_renderPending` 防止重入；草稿写入由
  2 秒计时器单独负责。
- **失败可见**：渲染异常写入 `preview.renderFailed` + 异常摘要，既进状态栏也进预览消息的 `status` 字段，
  由预览页顶部的 `#status` 元素展示（可复制）。
