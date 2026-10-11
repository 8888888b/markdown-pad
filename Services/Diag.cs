using System;
using System.IO;
using System.Text;

namespace MarkdownPad.Services;

/// <summary>
/// File based trace used to diagnose startup and preview problems.
/// Disabled by default; enable it with either
/// <c>set MARKDOWNPAD_DIAG=1</c> or <c>MarkdownPad.exe --diag</c>.
/// The log is written next to the executable and, when MARKDOWNPAD_TRACE is
/// set, to that path as well.
/// </summary>
public static class Diag
{
    private static readonly object Gate = new();
    private static string? _sidecar;

    public static bool Enabled { get; set; }

    public static string LogFile => _sidecar ??= BuildSidecarPath();

    public static void Log(string message)
    {
        if (!Enabled)
        {
            return;
        }

        var line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine;
        Write(LogFile, line);

        var overridePath = Environment.GetEnvironmentVariable("MARKDOWNPAD_TRACE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            Write(overridePath!, line);
        }
    }

    public static void Log(Exception exception, string context)
    {
        Log(context + ": " + exception.GetType().Name + ": " + exception.Message);
        Log(exception.ToString());
    }

    private static string BuildSidecarPath()
    {
        try
        {
            return Path.Combine(AppContext.BaseDirectory, "MarkdownPad-trace.log");
        }
        catch
        {
            return "MarkdownPad-trace.log";
        }
    }

    private static void Write(string path, string line)
    {
        try
        {
            lock (Gate)
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(line);
                writer.Flush();
                stream.Flush(true);
            }
        }
        catch
        {
            // Tracing must never take the app down.
        }
    }
}
