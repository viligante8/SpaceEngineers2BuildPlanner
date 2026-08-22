using System;
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
        Log.Write("BuildPlanner initializing...");
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
