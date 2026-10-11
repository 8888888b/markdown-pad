using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MarkdownPad.Services;
using Microsoft.UI.Xaml;

namespace MarkdownPad;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        var arguments = Environment.GetCommandLineArgs();
        Diag.Enabled = arguments.Any(a => string.Equals(a, "--diag", StringComparison.OrdinalIgnoreCase))
            || Environment.GetEnvironmentVariable("MARKDOWNPAD_DIAG") == "1";

        Diag.Log("App ctor");
        UnhandledException += (_, e) =>
        {
            Diag.Log(e.Exception, "Application.UnhandledException");
            e.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Diag.Log("AppDomain unhandled: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Diag.Log(e.Exception, "Unobserved task exception");

        try
        {
            InitializeComponent();
            Diag.Log("App InitializeComponent done");
        }
        catch (Exception ex)
        {
            Diag.Log(ex, "App.InitializeComponent FAILED");
            throw;
        }
    }

    /// <summary>Set from the MARKDOWNPAD_TRACE environment variable for diagnostics.</summary>
    internal static void WriteTrace(string message) => Diag.Log(message);

    public static MainWindow? MainWindow { get; private set; }

    /// <summary>True while running the --selftest harness (no window is shown).</summary>
    public static bool IsSelfTest { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Diag.Log("OnLaunched enter");

        IsSelfTest = Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));

        if (IsSelfTest)
        {
            RunSelfTestAsync();
            return;
        }

        try
        {
            _window = new MainWindow
            {
                StartupFilePath = ResolveStartupFile(),
            };
            MainWindow = _window;
            Diag.Log("MainWindow created");
            _window.Activate();
            Diag.Log("MainWindow activated");
        }
        catch (Exception ex)
        {
            Diag.Log(ex, "OnLaunched");
            throw;
        }
    }

    /// <summary>First command line argument that names an existing file.</summary>
    private static string? ResolveStartupFile()
    {
        try
        {
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                if (argument.StartsWith('-'))
                {
                    continue;
                }

                if (File.Exists(argument))
                {
                    return Path.GetFullPath(argument);
                }
            }
        }
        catch (Exception ex)
        {
            Diag.Log(ex, "ResolveStartupFile");
        }

        return null;
    }

    private async void RunSelfTestAsync()
    {
        try
        {
            var window = new MainWindow();
            MainWindow = window;

            // Guard against a wedged test run so the harness never hangs a build.
            var run = SelfTest.RunAsync(window, window.DebugEditor, AppContext.BaseDirectory);
            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60)));
            var exitCode = finished == run ? await run : 3;
            Environment.Exit(exitCode);
        }
        catch (Exception ex)
        {
            Diag.Log(ex, "SelfTest");
            Environment.Exit(2);
        }
    }
}
