using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MarkdownPad.Controls;
using MarkdownPad.Helpers;
using MarkdownPad.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
using WinRT.Interop;

namespace MarkdownPad;

public sealed partial class MainWindow : Window
{

    private readonly AppSettings _settings;
    private readonly MarkdownRenderer _renderer = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _draftTimer;

    private bool _previewInitialized;
    private bool _previewReady;
    private bool _suppressPreview;
    private bool _isDirty;
    private bool _renderPending;
    private bool _isDraggingSplitter;
    private bool _isClosing;
    private string? _filePath;
    private string? _lastStatusKey;
    private object?[] _lastStatusArgs = Array.Empty<object?>();
    private string? _previewStatusKey;
    private string? _previewStatusDetail;
    private string? _previewErrorDetail;
    private Exception? _renderTestException;
    private string _previewTheme;
    private bool _appDark;
    private DateTime _lastBackup = DateTime.MinValue;

    public MainWindow()
    {
        Diag.Log("MainWindow ctor enter");
        _settings = SettingsStore.Load();
        _previewTheme = string.Equals(_settings.PreviewTheme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        Loc.Language = _settings.Language;

        try
        {
            InitializeComponent();
            Diag.Log("MainWindow InitializeComponent done");
        }
        catch (Exception ex)
        {
            Diag.Log(ex, "MainWindow.InitializeComponent FAILED");
            throw;
        }

        Splitter.Cursor = SplitterHandle.ResizeCursor;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview(keepScroll: true);
        };

        _draftTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _draftTimer.Tick += async (_, _) =>
        {
            _draftTimer.Stop();
            await SaveDraftAsync();
        };

        SetupAccelerators();
        Diag.Log("MainWindow accelerators done");
        RestoreSettings();
        Diag.Log("MainWindow RestoreSettings done");
        ApplyLanguage();
        UpdateStatistics();
        UpdateDirtyVisuals();

        AppWindow.Changed += AppWindow_Changed;
        Loc.LanguageChanged += (_, _) => ApplyLanguage();
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        Diag.Log("MainWindow ctor done");
    }

    // ==================================================================
    // localisation
    // ==================================================================

    /// <summary>Applies the active language to every piece of chrome.</summary>
    private void ApplyLanguage()
    {
        ToolTipService.SetToolTip(ThemeButton, Loc.T("app.theme.tooltip"));
        ToolTipService.SetToolTip(LanguageButton, Loc.T("app.language.tooltip"));

        LanguageZhItem.Text = Loc.T("app.language.zh");
        LanguageEnItem.Text = Loc.T("app.language.en");

        // Toolbar buttons carry their localisation key in Tag.
        foreach (var child in ToolbarPanel.Children)
        {
            if (child is FrameworkElement { Tag: string command })
            {
                ToolTipService.SetToolTip(child, Loc.T("cmd." + command));
            }
        }

        ToolTipService.SetToolTip(HeadingButton, Loc.T("cmd.heading"));
        Heading1Item.Text = Loc.T("cmd.h1");
        Heading2Item.Text = Loc.T("cmd.h2");
        Heading3Item.Text = Loc.T("cmd.h3");
        Heading4Item.Text = Loc.T("cmd.h4");
        ParagraphItem.Text = Loc.T("cmd.paragraph");

        ToolTipService.SetToolTip(PreviewToggle, Loc.T("cmd.preview"));
        ToolTipService.SetToolTip(WrapToggle, Loc.T("cmd.wrap"));
        ToolTipService.SetToolTip(PreviewThemeToggle, Loc.T("cmd.previewTheme"));

        Editor.PlaceholderText = Loc.T("editor.placeholder");
        EditorHeaderText.Text = Loc.T("pane.markdown");
        PreviewHeaderText.Text = Loc.T("pane.preview");
        DropTitleText.Text = Loc.T("status.dropped");
        DropHintText.Text = Loc.T("status.dropHint");

        if (_previewStatusKey is { } previewKey)
        {
            PreviewStatusText.Text = _previewStatusDetail is { } detail
                ? Loc.T("preview.statusLabel", Loc.T(previewKey) + " - " + detail)
                : Loc.T(previewKey);
        }

        UpdateDirtyVisuals();
        UpdateDocumentTitle();
        UpdateStatistics();

        // Re-render the last status message in the new language.
        if (_lastStatusKey is { } key)
        {
            SetStatus(key, _lastStatusArgs);
        }
        else
        {
            SetStatus("status.ready");
        }
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string language })
        {
            return;
        }

        Loc.Language = language;
        _settings.Language = Loc.Language;
        _ = SettingsStore.SaveAsync(_settings);
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange && !args.DidPresenterChange)
        {
            return;
        }

        UpdateCaptionSpacer();
    }

    /// <summary>Keeps custom title bar content clear of the system caption buttons.</summary>
    private void UpdateCaptionSpacer()
    {
        try
        {
            var inset = AppWindow.TitleBar.RightInset;
            CaptionSpacer.Width = inset > 0 ? inset : 140;
        }
        catch
        {
            CaptionSpacer.Width = 140;
        }
    }

    // ==================================================================
    // startup
    // ==================================================================

    private void SetupAccelerators()
    {
        AddAccelerator(Windows.System.VirtualKey.B, Windows.System.VirtualKeyModifiers.Control, "bold");
        AddAccelerator(Windows.System.VirtualKey.I, Windows.System.VirtualKeyModifiers.Control, "italic");
        AddAccelerator(Windows.System.VirtualKey.K, Windows.System.VirtualKeyModifiers.Control, "link");
        AddAccelerator(Windows.System.VirtualKey.D, Windows.System.VirtualKeyModifiers.Control, "code");
        AddAccelerator(Windows.System.VirtualKey.Number1, Windows.System.VirtualKeyModifiers.Control, "h1");
        AddAccelerator(Windows.System.VirtualKey.Number2, Windows.System.VirtualKeyModifiers.Control, "h2");
        AddAccelerator(Windows.System.VirtualKey.Number3, Windows.System.VirtualKeyModifiers.Control, "h3");
        AddAccelerator(Windows.System.VirtualKey.Number4, Windows.System.VirtualKeyModifiers.Control, "h4");
        AddAccelerator(Windows.System.VirtualKey.C, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, "codeblock");
        AddAccelerator(Windows.System.VirtualKey.Q, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, "quote");
        AddAccelerator(Windows.System.VirtualKey.L, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, "ul");
        AddAccelerator(Windows.System.VirtualKey.E, Windows.System.VirtualKeyModifiers.Control, "preview");
        AddAccelerator(Windows.System.VirtualKey.T, Windows.System.VirtualKeyModifiers.Control, "theme");
        AddAccelerator(Windows.System.VirtualKey.N, Windows.System.VirtualKeyModifiers.Control, "new");
        AddAccelerator(Windows.System.VirtualKey.O, Windows.System.VirtualKeyModifiers.Control, "open");
        AddAccelerator(Windows.System.VirtualKey.S, Windows.System.VirtualKeyModifiers.Control, "save");
        AddAccelerator(Windows.System.VirtualKey.S, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, "saveas");
        AddAccelerator(Windows.System.VirtualKey.E, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, "export");
        AddAccelerator(Windows.System.VirtualKey.Up, Windows.System.VirtualKeyModifiers.Control, "prevsect");
        AddAccelerator(Windows.System.VirtualKey.Down, Windows.System.VirtualKeyModifiers.Control, "nextsect");
        AddAccelerator(Windows.System.VirtualKey.F11, Windows.System.VirtualKeyModifiers.None, "fullscreen");
    }

    private void AddAccelerator(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers, string command)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (sender, args) =>
        {
            args.Handled = true;
            ExecuteCommand(command);
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Diag.Log("Loaded enter");
        if (_previewInitialized)
        {
            return;
        }

        _previewInitialized = true;
        await InitializePreviewAsync();
        Diag.Log("preview init returned");

        if (!string.IsNullOrWhiteSpace(StartupFilePath))
        {
            await OpenStartupFileAsync(StartupFilePath!);
        }
        else
        {
            await LoadInitialDocumentAsync();
        }

        Diag.Log("initial document loaded");
    }

    /// <summary>File passed on the command line; opened once the preview is up.</summary>
    internal string? StartupFilePath { get; set; }
    private void RestoreSettings()
    {
        Title = Loc.T("app.title");
        AppTitleBar.Height = 48;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            var appWindow = AppWindow;
            appWindow.Title = Loc.T("app.title");
            appWindow.Resize(new SizeInt32(
                Math.Clamp((int)_settings.WindowWidth, 640, 6000),
                Math.Clamp((int)_settings.WindowHeight, 420, 4000)));

            var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
            if (area is not null && _settings.WindowX != int.MinValue && _settings.WindowY != int.MinValue)
            {
                var x = Math.Clamp(_settings.WindowX, area.WorkArea.X - 40, area.WorkArea.X + area.WorkArea.Width - 200);
                var y = Math.Clamp(_settings.WindowY, area.WorkArea.Y - 10, area.WorkArea.Y + area.WorkArea.Height - 120);
                appWindow.Move(new PointInt32(x, y));
            }

            if (_settings.WindowMaximized && appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            if (appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.PreferredMinimumWidth = 760;
                overlapped.PreferredMinimumHeight = 480;
            }
        }
        catch
        {
            // Window plumbing is best effort; the app still works without it.
        }

        EditorColumn.Width = new GridLength(Math.Clamp(_settings.EditorColumnWidth, 240, 4000));
        PreviewToggle.IsChecked = _settings.ShowPreview;
        WrapToggle.IsChecked = _settings.WordWrap;
        Editor.TextWrapping = _settings.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        PreviewThemeToggle.IsChecked = _previewTheme == "dark";
        Editor.FontFamily = new FontFamily(_settings.EditorFontFamily);
        Editor.FontSize = Math.Clamp(_settings.EditorFontSize, 10, 28);
        ApplyPreviewColumnVisibility(_settings.ShowPreview);
        UpdatePreviewThemeBadge();
        _appDark = ResolveAppDark();
        ApplyAppTheme(_appDark);
    }

    /// <summary>"system" follows Windows; anything else is an explicit choice.</summary>
    private bool ResolveAppDark() => _settings.AppTheme switch
    {
        "dark" => true,
        "light" => false,
        _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
    };

    private async Task LoadInitialDocumentAsync()
    {
        var restored = false;

        if (!string.IsNullOrWhiteSpace(_settings.LastFilePath) && File.Exists(_settings.LastFilePath))
        {
            try
            {
                var text = await File.ReadAllTextAsync(_settings.LastFilePath!, Encoding.UTF8);
                SetDocument(text, _settings.LastFilePath, markDirty: false);
                restored = true;
                SetStatus("status.restoredFile");
            }
            catch
            {
                // Fall through to the draft / welcome document.
            }
        }

        if (!restored && File.Exists(AppPaths.DraftFile))
        {
            try
            {
                var draft = await File.ReadAllTextAsync(AppPaths.DraftFile, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(draft))
                {
                    SetDocument(draft, null, markDirty: true);
                    restored = true;
                    SetStatus("status.restoredDraft");
                }
            }
            catch
            {
                // Ignore and show the welcome document.
            }
        }

        if (!restored)
        {
            SetDocument(ReadWelcomeDocument(), null, markDirty: false);
            Editor.SelectionStart = 0;
            Editor.SelectionLength = 0;
            SetStatus("status.new");
        }

        UpdatePreview(keepScroll: false);
    }

    private static string ReadWelcomeDocument()
    {
        try
        {
            var path = Path.Combine(AppPaths.AssetsDirectory, "Welcome.md");
            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
        }
        catch
        {
            // fall through
        }

        return Loc.T("app.title") + "\n\n" + Loc.T("welcome.fallbackBody") + "\n";
    }

    // ==================================================================
    // preview
    // ==================================================================

    private async Task InitializePreviewAsync()
    {
        try
        {
            SetPreviewStatus("preview.starting");
            Diag.Log("EnsureCoreWebView2Async enter");
            await Preview.EnsureCoreWebView2Async();
            Diag.Log("EnsureCoreWebView2Async done");

            var core = Preview.CoreWebView2;

            var settings = core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = true;
            settings.AreDevToolsEnabled = Debugger.IsAttached;
            settings.IsWebMessageEnabled = true;

            core.WebMessageReceived += Core_WebMessageReceived;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.ProcessFailed += (_, args) =>
            {
                SetPreviewStatus("preview.crashed");
                Diag.Log("WebView2 process failed: " + args.ProcessFailedKind);
                Debug.WriteLine($"WebView2 failed: {args.ProcessFailedKind}");
            };

            // NavigateToString keeps the preview entirely offline: the shell is
            // self-contained (CSS and scripts are inlined) and later updates are
            // pushed as JSON messages instead of reloading the page.
            core.NavigateToString(HtmlBuilder.Shell);
        }
        catch (Exception ex)
        {
            SetPreviewStatus("preview.failed");
            SetStatus("status.previewUnavailable", ex.Message);
            Diag.Log("preview init failed: " + ex);
            Debug.WriteLine(ex);
        }
    }

    private void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type))
            {
                return;
            }

            switch (type.GetString())
            {
                case "ready":
                    _previewReady = true;
                    PreviewStatusText.Text = string.Empty;
                    Diag.Log("preview shell ready");
                    UpdatePreview(keepScroll: false);
                    break;

                case "openLink":
                    if (root.TryGetProperty("href", out var href))
                    {
                        OpenExternal(href.GetString());
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        OpenExternal(args.Uri);
    }

    private static void OpenExternal(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            else if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async void UpdatePreview(bool keepScroll)
    {
        if (!_previewReady || _renderPending)
        {
            return;
        }

        try
        {
            _renderPending = true;

            if (_renderTestException is { } injected)
            {
                throw injected;
            }

            var markdown = Editor.Text ?? string.Empty;
            var html = _renderer.ToHtml(markdown, ResolveBasePath());
            PostPreview(html, keepScroll);

            // A successful render clears the copyable diagnostics banner.
            if (_previewErrorDetail is not null)
            {
                _previewErrorDetail = null;
                _previewStatusKey = null;
                _previewStatusDetail = null;
                PostPreview(html, keepScroll);
            }
        }
        catch (Exception ex)
        {
            // Never swallow a render failure silently again: report it in the
            // status text, in the preview pane and (when enabled) in the log.
            _previewErrorDetail = HtmlBuilder.FormatError(ex);
            Diag.Log(ex, "UpdatePreview");
            Debug.WriteLine(ex);
            SetPreviewStatus("preview.renderFailed", _previewErrorDetail);

            try
            {
                PostPreview(BuildErrorPage(), keepScroll: false);
            }
            catch (Exception nested)
            {
                Diag.Log(nested, "UpdatePreview error page");
            }
        }
        finally
        {
            _renderPending = false;
        }

        await Task.CompletedTask;
    }

    /// <summary>Pushes one update message into the preview page.</summary>
    private void PostPreview(string html, bool keepScroll)
    {
        var message = HtmlBuilder.Message(html, _previewTheme, keepScroll, _previewErrorDetail);
        Preview.CoreWebView2?.PostWebMessageAsJson(message);
        Diag.Log($"preview updated: {html.Length} chars, keepScroll={keepScroll}");

        if (_previewStatusKey is null)
        {
            PreviewStatusText.Text = string.Empty;
        }
    }

    private string BuildErrorPage() => HtmlBuilder.ErrorPage(
        Loc.T("preview.errorTitle"),
        _previewErrorDetail ?? Loc.T("preview.renderFailed"),
        Loc.T("preview.errorHint"));

    /// <summary>
    /// Folder used to resolve relative links and images. Never returns null or an
    /// empty string, because Path.Combine/GetFullPath throw on those.
    /// </summary>
    private string ResolveBasePath()
    {
        try
        {
            var directory = string.IsNullOrWhiteSpace(_filePath) ? null : Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory!;
            }

            var current = Directory.GetCurrentDirectory();
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            {
                return current;
            }
        }
        catch (Exception ex)
        {
            Diag.Log(ex, "ResolveBasePath");
        }

        return AppPaths.AppDirectory;
    }

    private void ApplyPreviewColumnVisibility(bool visible)
    {
        PreviewColumn.Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PreviewPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            // Keep the editor usable: it takes the whole width.
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            EditorColumn.Width = new GridLength(Math.Clamp(_settings.EditorColumnWidth, 240, 4000));
        }
    }

    private void UpdatePreviewThemeBadge()
    {
        PreviewThemeToggle.IsChecked = _previewTheme == "dark";
    }

    private void PushPreviewTheme()
    {
        try
        {
            if (Preview.CoreWebView2 is { } core && _previewReady)
            {
                core.PostWebMessageAsJson(HtmlBuilder.ThemeMessage(_previewTheme));
                core.Profile.PreferredColorScheme = _previewTheme == "dark"
                    ? CoreWebView2PreferredColorScheme.Dark
                    : CoreWebView2PreferredColorScheme.Light;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    // ==================================================================
    // editor events
    // ==================================================================

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        MarkDirty();
        UpdateStatistics();
        SchedulePreview();
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateStatistics();

    private void Editor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Tab)
        {
            // Tab inserts a soft tab instead of moving focus.
            var result = MarkdownEditor.InsertSnippet(Editor.Text ?? string.Empty, Editor.SelectionStart, Editor.SelectionLength, "    ", 4);
            ApplyEdit(result);
            e.Handled = true;
        }
    }

    // ==================================================================
    // toolbar / commands
    // ==================================================================

    private void Toolbar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command })
        {
            ExecuteCommand(command);
        }
    }

    private void ExecuteCommand(string command)
    {
        var text = Editor.Text ?? string.Empty;
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;

        switch (command)
        {
            case "new":
                _ = NewDocumentAsync();
                return;
            case "open":
                _ = OpenDocumentAsync();
                return;
            case "save":
                _ = SaveAsync(forceDialog: false);
                return;
            case "saveas":
                _ = SaveAsync(forceDialog: true);
                return;
            case "export":
                _ = ExportHtmlAsync();
                return;
            case "bold":
                ApplyEdit(MarkdownEditor.ToggleBold(text, start, length));
                return;
            case "italic":
                ApplyEdit(MarkdownEditor.ToggleItalic(text, start, length));
                return;
            case "strike":
                ApplyEdit(MarkdownEditor.ToggleStrike(text, start, length));
                return;
            case "code":
                ApplyEdit(MarkdownEditor.ToggleInlineCode(text, start, length));
                return;
            case "link":
                ApplyEdit(MarkdownEditor.InsertLink(text, start, length));
                return;
            case "image":
                ApplyEdit(MarkdownEditor.InsertImage(text, start, length));
                return;
            case "ul":
                ApplyEdit(MarkdownEditor.ToggleBulletList(text, start, length));
                return;
            case "ol":
                ApplyOrderedList();
                return;
            case "task":
                ApplyEdit(MarkdownEditor.ToggleTaskList(text, start, length));
                return;
            case "quote":
                ApplyEdit(MarkdownEditor.ToggleQuote(text, start, length));
                return;
            case "codeblock":
                ApplyEdit(MarkdownEditor.ToggleCodeBlock(text, start, length));
                return;
            case "h1":
                ApplyEdit(MarkdownEditor.SetHeading(text, start, length, 1));
                return;
            case "h2":
                ApplyEdit(MarkdownEditor.SetHeading(text, start, length, 2));
                return;
            case "h3":
                ApplyEdit(MarkdownEditor.SetHeading(text, start, length, 3));
                return;
            case "h4":
                ApplyEdit(MarkdownEditor.SetHeading(text, start, length, 4));
                return;
            case "paragraph":
                ApplyEdit(MarkdownEditor.SetHeading(text, start, length, 0));
                return;
            case "table":
                ApplyEdit(MarkdownEditor.InsertSnippet(text, start, length, MarkdownEditor.TableSnippet + "\n", 2, 5));
                return;
            case "hr":
                ApplyEdit(MarkdownEditor.InsertSnippet(text, start, length, "\n---\n", 5));
                return;
            case "task-snippet":
                ApplyEdit(MarkdownEditor.InsertSnippet(text, start, length, MarkdownEditor.TaskSnippet, 6, 4));
                return;
            case "prevsect":
                MoveToHeading(-1);
                return;
            case "nextsect":
                MoveToHeading(1);
                return;
            case "preview":
                SetPreviewVisible(PreviewToggle.IsChecked != true);
                return;
            case "theme":
                SetPreviewTheme(PreviewThemeToggle.IsChecked == true ? "light" : "dark");
                return;
            case "fullscreen":
                ToggleFullScreen();
                return;
        }
    }

    private void ApplyOrderedList()
    {
        var text = Editor.Text ?? string.Empty;
        var (lineStart, end) = GetLineBounds(Editor.SelectionStart, Editor.SelectionLength);
        var block = text.Substring(lineStart, end - lineStart);
        var lines = block.Split('\n');
        var builder = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            var isNumbered = trimmed.Length > 1 && char.IsDigit(trimmed[0]) && trimmed.Contains(". ", StringComparison.Ordinal);
            var body = isNumbered ? trimmed[(trimmed.IndexOf(". ", StringComparison.Ordinal) + 2)..] : lines[i];
            builder.Append(i + 1).Append(". ").Append(body);
            if (i < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        var replacement = builder.ToString();
        var newText = string.Concat(text.AsSpan(0, lineStart), replacement, text.AsSpan(end));
        ApplyEdit(new EditResult(newText, lineStart + replacement.Length, 0));
    }

    private (int Start, int End) GetLineBounds(int start, int length)
    {
        var text = Editor.Text ?? string.Empty;
        start = Math.Clamp(start, 0, text.Length);
        var end = Math.Clamp(start + length, 0, text.Length);

        while (start > 0 && text[start - 1] != '\n')
        {
            start--;
        }

        if (end > start && end <= text.Length && text[end - 1] == '\n')
        {
            end--;
        }

        return (start, Math.Max(end, start));
    }

    private void ApplyEdit(EditResult result)
    {
        _suppressPreview = true;
        Editor.Text = result.Text;
        Editor.SelectionStart = Math.Clamp(result.SelectionStart, 0, result.Text.Length);
        Editor.SelectionLength = Math.Clamp(result.SelectionLength, 0, result.Text.Length - Editor.SelectionStart);
        _suppressPreview = false;

        MarkDirty();
        UpdateStatistics();
        SchedulePreview();

        if (!App.IsSelfTest)
        {
            Editor.Focus(FocusState.Programmatic);
        }
    }

    private void MoveToHeading(int direction)
    {
        var text = Editor.Text ?? string.Empty;
        var caret = Editor.SelectionStart;
        var positions = new List<int>();
        var index = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.StartsWith('#'))
            {
                var hashes = 0;
                while (hashes < line.Length && line[hashes] == '#')
                {
                    hashes++;
                }

                if (hashes <= 6 && hashes < line.Length && line[hashes] == ' ')
                {
                    positions.Add(index);
                }
            }

            index += raw.Length + 1;
        }

        if (positions.Count == 0)
        {
            SetStatus("status.noHeadings");
            return;
        }

        var target = direction < 0
            ? positions.LastOrDefault(p => p < caret - 1, -1)
            : positions.FirstOrDefault(p => p > caret, -1);

        if (target < 0)
        {
            target = direction < 0 ? positions[0] : positions[^1];
        }

        Editor.SelectionStart = Math.Clamp(target, 0, text.Length);
        Editor.SelectionLength = 0;
        Editor.Focus(FocusState.Programmatic);
    }

    private void ToggleFullScreen()
    {
        try
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }

            if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            }
            else
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    // ==================================================================
    // splitter
    // ==================================================================

    private void Splitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SplitterGrip.Width = 2;
    }

    private void Splitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SplitterGrip.Width = 1;
    }

    private void Splitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingSplitter = Splitter.CapturePointer(e.Pointer);
        SplitterGrip.Width = 2;
    }

    private void Splitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSplitter)
        {
            return;
        }

        var point = e.GetCurrentPoint(RootGrid);
        var width = point.Position.X;
        var available = RootGrid.ActualWidth - Splitter.ActualWidth - 220;

        if (PreviewPane.Visibility == Visibility.Visible)
        {
            EditorColumn.Width = new GridLength(Math.Clamp(width, 240, Math.Max(240, available)));
        }
        else
        {
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
        }
    }

    private void Splitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingSplitter)
        {
            Splitter.ReleasePointerCapture(e.Pointer);
            _isDraggingSplitter = false;
            _settings.EditorColumnWidth = EditorColumn.ActualWidth;
        }
    }

    // ==================================================================
    // toggles
    // ==================================================================

    private void TogglePreview_Click(object sender, RoutedEventArgs e) =>
        SetPreviewVisible(PreviewToggle.IsChecked == true);

    private void SetPreviewVisible(bool visible)
    {
        PreviewToggle.IsChecked = visible;
        _settings.ShowPreview = visible;
        ApplyPreviewColumnVisibility(visible);

        if (visible)
        {
            UpdatePreview(keepScroll: false);
        }
    }

    private void ToggleWrap_Click(object sender, RoutedEventArgs e)
    {
        var wrap = WrapToggle.IsChecked == true;
        _settings.WordWrap = wrap;
        Editor.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    private void TogglePreviewTheme_Click(object sender, RoutedEventArgs e) =>
        SetPreviewTheme(PreviewThemeToggle.IsChecked == true ? "dark" : "light");

    private void SetPreviewTheme(string theme)
    {
        _previewTheme = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        _settings.PreviewTheme = _previewTheme;
        UpdatePreviewThemeBadge();
        PushPreviewTheme();
        SetStatus(_previewTheme == "dark" ? "status.previewDark" : "status.previewLight");
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e) => ApplyAppTheme(!_appDark);

    private void ApplyAppTheme(bool dark)
    {
        if (RootGrid is null)
        {
            return;
        }

        _appDark = dark;
        _settings.AppTheme = dark ? "dark" : "light";
        RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeButtonIcon.Glyph = dark ? "\uE706" : "\uE793";
        ApplyCaptionButtonColors();
    }

    private void ApplyCaptionButtonColors()
    {
        try
        {
            if (AppWindow.TitleBar is not { } titleBar)
            {
                return;
            }

            // A visible (dark on light chrome, light on dark chrome) glyph and a
            // real hover fill, so the system buttons never wash out.
            var foreground = _appDark ? Colors.White : Colors.Black;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = _appDark
                ? Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x99, 0x00, 0x00, 0x00);
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = _appDark
                ? Windows.UI.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x14, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedBackgroundColor = _appDark
                ? Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x24, 0x00, 0x00, 0x00);

            UpdateCaptionSpacer();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    // ==================================================================
    // drag & drop
    // ==================================================================

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.T("cmd.open");
            e.DragUIOverride.IsCaptionVisible = true;
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void RootGrid_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        _ = HandleDropAsync(e);
    }

    private async Task HandleDropAsync(DragEventArgs e)
    {
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<StorageFile>().FirstOrDefault();
            if (file is null)
            {
                return;
            }

            var extension = Path.GetExtension(file.Name);
            if (!IsMarkdownExtension(extension))
            {
                SetStatus("status.unsupported", extension);
                return;
            }

            var text = await FileIO.ReadTextAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            _suppressPreview = true;
            SetDocument(text, file.Path, markDirty: false);
            _suppressPreview = false;
            UpdatePreview(keepScroll: false);
            SetStatus("status.opened", file.Name);
        }
        catch (Exception ex)
        {
            SetStatus("status.openFailed", ex.Message);
        }
    }

    private static bool IsMarkdownExtension(string? extension) =>
        extension is not null &&
        (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".mdown", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".txt", StringComparison.OrdinalIgnoreCase));

    // ==================================================================
    // files
    // ==================================================================

    private async Task NewDocumentAsync()
    {
        await SaveDraftAsync();
        SetDocument(string.Empty, null, markDirty: false);
        Editor.Focus(FocusState.Programmatic);
        UpdatePreview(keepScroll: false);
        SetStatus("status.new");
    }

    private async Task OpenDocumentAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");
            picker.FileTypeFilter.Add(".mdown");
            picker.FileTypeFilter.Add(".txt");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            await LoadFromPathAsync(file.Path);
            SetStatus("status.opened", file.Name);
        }
        catch (Exception ex)
        {
            SetStatus("status.openFailed", ex.Message);
            Diag.Log(ex, "OpenDocumentAsync");
        }
    }

    private async Task LoadFromPathAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
        _suppressPreview = true;
        SetDocument(text, path, markDirty: false);
        _suppressPreview = false;
        _settings.LastFilePath = path;
        UpdatePreview(keepScroll: false);
        await SettingsStore.SaveAsync(_settings);
    }

    /// <summary>Opens a file passed on the command line (double-click / "open with").</summary>
    internal async Task OpenStartupFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            SetStatus("status.missingFile", path);
            return;
        }

        if (!IsMarkdownExtension(Path.GetExtension(path)))
        {
            SetStatus("status.unsupported", Path.GetExtension(path));
            return;
        }

        try
        {
            await LoadFromPathAsync(path);
            SetStatus("status.opened", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            SetStatus("status.openFailed", ex.Message);
            Diag.Log(ex, "OpenStartupFileAsync");
        }
    }

    private async Task SaveAsync(bool forceDialog)
    {
        try
        {
            var path = _filePath;

            if (forceDialog || string.IsNullOrWhiteSpace(path))
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = string.IsNullOrWhiteSpace(path)
                        ? Loc.T("dialog.untitledFile")
                        : Path.GetFileNameWithoutExtension(path),
                };
                picker.FileTypeChoices.Add(Loc.T("dialog.markdown"), new List<string> { ".md" });
                picker.FileTypeChoices.Add(Loc.T("dialog.text"), new List<string> { ".txt" });
                picker.DefaultFileExtension = ".md";
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

                var file = await picker.PickSaveFileAsync();
                if (file is null)
                {
                    return;
                }

                path = file.Path;
            }

            await SaveToPathAsync(path!);
            SetStatus("status.saved", Path.GetFileName(path!));
        }
        catch (Exception ex)
        {
            SetStatus("status.saveFailed", ex.Message);
            Diag.Log(ex, "SaveAsync");
        }
    }

    private async Task SaveToPathAsync(string path)
    {
        await File.WriteAllTextAsync(path, Editor.Text ?? string.Empty, new UTF8Encoding(false));

        _filePath = path;
        _settings.LastFilePath = path;
        SetDirty(false);
        await SettingsStore.SaveAsync(_settings);
    }

    private async Task ExportHtmlAsync()
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = _filePath is null
                    ? Loc.T("dialog.untitledExport")
                    : Path.GetFileNameWithoutExtension(_filePath),
            };
            picker.FileTypeChoices.Add(Loc.T("dialog.html"), new List<string> { ".html" });
            picker.DefaultFileExtension = ".html";
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            var basePath = _filePath is not null ? Path.GetDirectoryName(_filePath) : Directory.GetCurrentDirectory();
            var html = _renderer.ToHtml(Editor.Text ?? string.Empty, basePath);
            var document = HtmlBuilder.StandaloneDocument(html, Path.GetFileNameWithoutExtension(file.Name), _previewTheme);

            await File.WriteAllTextAsync(file.Path, document, new UTF8Encoding(false));
            SetStatus("status.exported", file.Name);
        }
        catch (Exception ex)
        {
            SetStatus("status.exportFailed", ex.Message);
        }
    }

    // ==================================================================
    // test seams (used by SelfTest --selftest)
    // ==================================================================

    /// <summary>The editor control, exposed for the self test harness.</summary>
    internal TextBox DebugEditor => Editor;

    /// <summary>Applies the active language to the live window (self test).</summary>
    internal void DebugApplyLanguage() => ApplyLanguage();

    /// <summary>Current localised window title (self test).</summary>
    internal string DebugTitle => Title;

    /// <summary>Localised editor pane header (self test).</summary>
    internal string DebugEditorHeader => EditorHeaderText.Text;

    /// <summary>Localised status bar text (self test).</summary>
    internal string DebugStatusLeft => StatusLeft.Text;

    /// <summary>Localised statistics text (self test).</summary>
    internal string DebugStatusRight => StatusRight.Text;

    /// <summary>Localised tooltip of the first toolbar button (self test).</summary>
    internal string DebugFirstToolbarTooltip =>
        ToolbarPanel.Children.FirstOrDefault() is { } first
            ? ToolTipService.GetToolTip(first)?.ToString() ?? string.Empty
            : string.Empty;

    /// <summary>Width reserved for the system caption buttons (self test).</summary>
    internal double DebugCaptionSpacerWidth => CaptionSpacer.Width;

    /// <summary>System title bar configuration (self test).</summary>
    internal (Windows.UI.Color? Foreground, double RightInset) DebugTitleBarState
    {
        get
        {
            try
            {
                return (AppWindow.TitleBar.ButtonForegroundColor, AppWindow.TitleBar.RightInset);
            }
            catch
            {
                return (null, -1);
            }
        }
    }

    /// <summary>Current localised preview status text (self test).</summary>
    internal string DebugPreviewStatus => PreviewStatusText.Text;

    /// <summary>Current copyable preview error detail (self test).</summary>
    internal string DebugPreviewError => _previewErrorDetail ?? string.Empty;

    /// <summary>Current preview status key (self test).</summary>
    internal string DebugPreviewStatusKey => _previewStatusKey ?? string.Empty;

    /// <summary>Current preview status detail (self test).</summary>
    internal string DebugPreviewStatusDetail => _previewStatusDetail ?? string.Empty;

    /// <summary>Rendered failure page (self test).</summary>
    internal string DebugErrorPage => BuildErrorPage();

    /// <summary>Forces a render failure (self test).</summary>
    internal void DebugInjectRenderFailure(Exception? exception) => _renderTestException = exception;

    /// <summary>Runs one preview update synchronously (self test).</summary>
    internal void DebugRender()
    {
        _previewReady = true;
        UpdatePreview(keepScroll: false);
    }

    internal static TextBox EditorBox => App.MainWindow!.Editor;

    internal bool IsDirtyForTest => _isDirty;

    internal Task SaveToPathForTestAsync(string path) => SaveToPathAsync(path);

    internal async Task LoadFromPathForTestAsync(string path) => await LoadFromPathAsync(path);

    // ==================================================================
    // document state
    // ==================================================================

    private void SetDocument(string text, string? path, bool markDirty)
    {
        _suppressPreview = true;
        Editor.Text = text;
        Editor.SelectionStart = 0;
        Editor.SelectionLength = 0;
        _suppressPreview = false;

        _filePath = path;
        _settings.LastFilePath = path;
        SetDirty(markDirty);
        UpdateStatistics();
        UpdateDocumentTitle();
    }

    private void MarkDirty()
    {
        if (_suppressPreview)
        {
            return;
        }

        SetDirty(true);
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private void SetDirty(bool dirty)
    {
        _isDirty = dirty;
        UpdateDirtyVisuals();
    }

    private void UpdateDirtyVisuals()
    {
        if (DirtyIndicator is null)
        {
            return;
        }

        DirtyIndicator.Visibility = _isDirty ? Visibility.Visible : Visibility.Collapsed;
        EditorDirtyText.Text = Loc.T(_isDirty ? "pane.dirty" : "pane.synced");
        UpdateDocumentTitle();
    }

    private void UpdateDocumentTitle()
    {
        if (DocumentTitleText is null)
        {
            return;
        }

        var name = _filePath is null ? Loc.T("app.untitled") : Path.GetFileName(_filePath);
        DocumentTitleText.Text = name;
        EditorHeaderText.Text = _filePath is null ? Loc.T("pane.markdown") : name.ToUpperInvariant();
        Title = (_isDirty ? "* " : string.Empty) + Loc.T("app.windowTitle", name);
    }

    private void UpdateStatistics()
    {
        if (StatusRight is null)
        {
            return;
        }

        var text = Editor.Text ?? string.Empty;
        var stats = TextStats.Measure(text);
        var line = 1;
        var column = 1;

        try
        {
            var caret = Math.Clamp(Editor.SelectionStart, 0, text.Length);
            line = text.AsSpan(0, caret).Count('\n') + 1;
            var lastBreak = text.LastIndexOf('\n', Math.Max(0, caret - 1));
            column = caret - lastBreak;
        }
        catch
        {
            // Selection bookkeeping is cosmetic.
        }

        StatusRight.Text = Loc.T("status.line", line, column, stats.Words, stats.Characters, stats.Lines, stats.ReadingMinutes);
    }

    private void SetStatus(string key, params object?[] args)
    {
        _lastStatusKey = key;
        _lastStatusArgs = args;
        StatusLeft.Text = Loc.T(key, args);
    }

    private void SetPreviewStatus(string? key, string? detail = null)
    {
        _previewStatusKey = key;
        _previewStatusDetail = detail;

        if (key is null)
        {
            PreviewStatusText.Text = string.Empty;
            return;
        }

        // The detail is the interesting part when something fails, so show it too.
        PreviewStatusText.Text = detail is null
            ? Loc.T(key)
            : Loc.T("preview.statusLabel", Loc.T(key) + " - " + detail);
    }

    private async Task SaveDraftAsync()
    {
        // The self test harness must not touch the user's draft or backups.
        if (App.IsSelfTest)
        {
            return;
        }

        try
        {
            AppPaths.EnsureCreated();
            var text = Editor.Text ?? string.Empty;
            await File.WriteAllTextAsync(AppPaths.DraftFile, text, new UTF8Encoding(false));

            // Timestamped backups for named documents, at most once a minute.
            if (_filePath is not null && _isDirty && DateTime.UtcNow - _lastBackup > TimeSpan.FromMinutes(1))
            {
                _lastBackup = DateTime.UtcNow;
                var name = Path.GetFileNameWithoutExtension(_filePath);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var backup = Path.Combine(AppPaths.BackupDirectory, $"{name}-{stamp}.md");
                await File.WriteAllTextAsync(backup, text, new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    // ==================================================================
    // shutdown
    // ==================================================================

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _previewTimer.Stop();
        _draftTimer.Stop();

        try
        {
            var appWindow = AppWindow;
            if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
            {
                _settings.WindowMaximized = true;
            }
            else
            {
                _settings.WindowMaximized = false;
                _settings.WindowWidth = appWindow.Size.Width;
                _settings.WindowHeight = appWindow.Size.Height;
                _settings.WindowX = appWindow.Position.X;
                _settings.WindowY = appWindow.Position.Y;
            }

            if (PreviewPane.Visibility == Visibility.Visible)
            {
                _settings.EditorColumnWidth = EditorColumn.ActualWidth;
            }

            _settings.PreviewTheme = _previewTheme;
            _settings.EditorFontSize = Editor.FontSize;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        await SaveDraftAsync();
        await SettingsStore.SaveAsync(_settings);
    }
}

