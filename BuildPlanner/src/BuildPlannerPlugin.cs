using System;
using System.Reflection;
using Keen.VRage.Core.Plugins;

namespace BuildPlanner;

/// <summary>
/// Plugin entry point.
///
/// Load with:
///     SpaceEngineers2.exe -plugins:&lt;path&gt;\BuildPlanner.dll
///
/// PluginHost instantiates this via Activator.CreateInstance(pluginType, this), falling back to the
/// parameterless constructor, so both are provided.
/// </summary>
public class BuildPlannerPlugin : IPlugin
{
    private static BuildPlannerInstaller? _installer;

    public BuildPlannerPlugin()
    {
        // The version is the first thing any bug report needs: this plugin binds to method
        // signatures and private field names, so "which build is that log from" decides whether a
        // failure is a known one already fixed or something new. Stamped by packaging/package.ps1
        // from the release tag; a local build reports whatever the csproj default is.
        Log.Write($"BuildPlanner {Version()} initializing...");
    }

    private static string Version()
    {
        try
        {
            var assembly = typeof(BuildPlannerPlugin).Assembly;

            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            // The SDK appends "+<commit sha>" when the build knows one; the sha is noise here.
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0];

            return assembly.GetName().Version?.ToString() ?? "unknown version";
        }
        catch (Exception ex)
        {
            // Never let a cosmetic lookup stop the plugin loading.
            Log.Error("reading the plugin version failed", ex);
            return "unknown version";
        }
    }

    public BuildPlannerPlugin(PluginHost host) : this()
    {
        try
        {
            _installer = new BuildPlannerInstaller();
            _installer.Install(host);
            Log.Write("BuildPlanner ready.");
        }
        catch (Exception ex)
        {
            Log.Error("initialization failed", ex);
        }
    }
}
