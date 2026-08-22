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
}
