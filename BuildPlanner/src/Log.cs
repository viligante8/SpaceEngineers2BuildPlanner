using System;
using System.IO;

namespace BuildPlanner;

/// <summary>
/// File logger for the plugin. Writes next to the game's own logs so a diagnosis only ever needs
/// one folder. Logging must never throw — a broken log must not take the game down with it.
/// </summary>
internal static class Log
{
    // Deliberately NOT under SpaceEngineers2\Temp\Logs: the game prunes that directory on
    // startup and took this log with it, destroying the evidence from a test run. Its own folder
    // keeps the history across launches.
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpaceEngineers2", "BuildPlanner", "BuildPlanner.log");

    private static readonly object Gate = new object();

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
        }
        catch
        {
            // Never let logging break the game.
        }
    }

    internal static void Error(string message, Exception ex) => Write($"ERROR: {message}: {ex}");

    /// <summary>
    /// Verbose tracing: entity dumps, component lists, inventory contents, per-source transfer steps.
    ///
    /// Off by default. These lines were essential while the client/server split was being diagnosed
    /// (notes/client-server-split.md) and are kept rather than deleted, because they are the only
    /// diagnostic tool this project has — there is no debugger attached to a shipped game. What they
    /// must not do is bury the outcome lines during normal play: a right-click wrote a dozen lines.
    ///
    /// Outcomes, warnings and errors are NOT routed through here. Every branch still reports itself
    /// unconditionally (CLAUDE.md, "A silent code path is a broken code path") — this only silences
    /// the supporting detail beneath those reports.
    ///
    /// Enable by creating the file:
    ///     %APPDATA%\SpaceEngineers2\BuildPlanner\debug
    /// A file flag rather than a launch argument, so it can be toggled without editing Steam options
    /// and without a rebuild. Read once per run.
    /// </summary>
    internal static void Debug(string message)
    {
        if (DebugEnabled) Write(message);
    }

    /// <summary>
    /// Whether the verbose flag file is present. Evaluated once: a filesystem probe on every log call
    /// would run inside the input handler on the game thread.
    /// </summary>
    private static readonly bool DebugEnabled = ProbeDebugFlag();

    private static bool ProbeDebugFlag()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            return dir != null && File.Exists(Path.Combine(dir, "debug"));
        }
        catch
        {
            return false;
        }
    }
}
