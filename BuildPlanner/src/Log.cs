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

    /// <summary>
    /// Roll the log once it passes this, keeping one previous file.
    ///
    /// The log is appended to across every launch and was never trimmed, so it grew without limit
    /// for the life of the install - a real measurement after 49 launches was 1.8 MB, and nothing
    /// in the code would ever have stopped it. Two files of this size is the worst case on disk.
    /// </summary>
    private const long MaxBytes = 4L * 1024 * 1024;

    /// <summary>Bytes written so far, so the size check does not stat the file on every line.</summary>
    private static long _written;

    /// <summary>The log file path, so diagnostics can find sibling files (queries.txt, quiet).</summary>
    internal static string PathForDiagnostics => LogPath;

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir != null) Directory.CreateDirectory(dir);
                var line = $"[{DateTime.Now:HH:mm:ss}] {message}" + Environment.NewLine;
                RollIfTooBig(line.Length);
                File.AppendAllText(LogPath, line);
                _written += line.Length;
            }
        }
        catch
        {
            // Never let logging break the game.
        }
    }

    /// <summary>
    /// Move the log aside when it gets too big, keeping exactly one previous file.
    /// </summary>
    /// <remarks>
    /// Called with the gate held. The on-disk size is read once per run and then tracked in memory:
    /// probing the file length on every line would double the syscalls of the very thing this is
    /// meant to bound, and it runs on the game thread.
    /// </remarks>
    private static void RollIfTooBig(int about)
    {
        if (_written == 0)
        {
            // First write of the run: adopt whatever a previous run left behind.
            try
            {
                var existing = new FileInfo(LogPath);
                _written = existing.Exists ? existing.Length : 0;
            }
            catch
            {
                _written = 0;
            }
        }

        if (_written + about <= MaxBytes) return;

        try
        {
            var previous = LogPath + ".1";
            if (File.Exists(previous)) File.Delete(previous);
            if (File.Exists(LogPath)) File.Move(LogPath, previous);
            _written = 0;

            File.AppendAllText(
                LogPath,
                "[" + DateTime.Now.ToString("HH:mm:ss") +
                "] --- rolled over; the previous log is BuildPlanner.log.1 ---" + Environment.NewLine);
        }
        catch
        {
            // If the roll fails the log simply keeps growing. Losing the cap is survivable;
            // throwing here is not.
        }
    }

    internal static void Error(string message, Exception ex) => Write($"ERROR: {message}: {ex}");

    /// <summary>
    /// Verbose tracing: entity dumps, component lists, inventory contents, per-source transfer steps.
    ///
    /// These lines were essential while the client/server split was being diagnosed
    /// (notes/client-server-split.md) and are kept rather than deleted, because they are the only
    /// diagnostic tool this project has — there is no debugger attached to a shipped game. What they
    /// must not do is bury the outcome lines during normal play: a right-click wrote a dozen lines.
    ///
    /// Outcomes, warnings and errors are NOT routed through here. Every branch still reports itself
    /// unconditionally (CLAUDE.md, "A silent code path is a broken code path") — this only silences
    /// the supporting detail beneath those reports.
    ///
    /// **On by default.** A game restart plus world load costs about five minutes, so a run that
    /// fails to record something needed is far more expensive than a large log file. Log first, trim
    /// later. Disable by creating the file:
    ///     %APPDATA%\SpaceEngineers2\BuildPlanner\quiet
    /// Read once per run.
    /// </summary>
    internal static void Debug(string message)
    {
        if (DebugEnabled) Write(message);
    }

    /// <summary>
    /// The highest-frequency tracing there is: state that flips every time the player looks at or
    /// away from a block.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="Debug"/> after measuring a real log. Four line types - capturing
    /// and releasing the welder's UI component, and claiming and releasing right-click - were
    /// **61% of 25,000 lines** across 49 launches. They are genuine state transitions rather than
    /// per-frame spam, but the transitions happen continuously while building, which is exactly
    /// when nobody is reading the log.
    ///
    /// They stay in the code because the input-context work could not have been diagnosed without
    /// them. They are opt-in because carrying them by default costs every player disk space for
    /// something only useful while debugging input:
    ///     %APPDATA%\SpaceEngineers2\BuildPlanner	race-input
    ///
    /// Same flag as the engine's own input tracing - both answer the same question, so one switch
    /// turns on the whole picture.
    /// </remarks>
    internal static void Trace(string message)
    {
        if (TraceEnabled) Write(message);
    }

    private static readonly bool TraceEnabled = HasFlag("trace-input");

    /// <summary>
    /// Whether verbose tracing is on. Exposed so callers can gate work that is only worth doing
    /// while diagnosing - not just the logging of it.
    /// </summary>
    internal static bool IsVerbose => DebugEnabled;

    /// <summary>
    /// Whether a named opt-in flag file sits next to the log.
    /// </summary>
    /// <remarks>
    /// Opt-IN, unlike <c>quiet</c>. Used for switches that change something the mod does not own -
    /// the engine's own input trace writes into the GAME's log, for every session, so it must not
    /// be on by default the way this plugin's own tracing is.
    /// </remarks>
    internal static bool HasFlag(string name)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            return dir != null && File.Exists(Path.Combine(dir, name));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the verbose flag file is absent. Evaluated once: a filesystem probe on every log call
    /// would run inside the input handler on the game thread.
    /// </summary>
    private static readonly bool DebugEnabled = ProbeDebugFlag();

    private static bool ProbeDebugFlag()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            // Verbose unless explicitly silenced. Failing to probe leaves it ON, deliberately.
            return dir == null || !File.Exists(Path.Combine(dir, "quiet"));
        }
        catch
        {
            return true;
        }
    }
}
