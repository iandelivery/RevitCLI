using RevitCliBridge.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitCliBridge
{
    public static class BridgePluginLoader
    {
        public static void LoadPlugins(string pluginDir)
        {
            if (!Directory.Exists(pluginDir))
                return;

            foreach (var dll in Directory.GetFiles(pluginDir, "*.dll"))
            {
                LoadPluginDll(dll);
            }
        }

        private static void LoadPluginDll(string dllPath)
        {
            try
            {
                var assembly = Assembly.LoadFrom(dllPath);

                var handlerTypes = assembly.GetTypes()
                    .Where(t => typeof(IBridgeCommand).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                foreach (var handlerType in handlerTypes)
                {
                    var cmd = (IBridgeCommand)Activator.CreateInstance(handlerType);
                    CommandRouter.Register(cmd.CommandName, cmd);
                }
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Bridge plugin load failed: {Path.GetFileName(dllPath)} - {ex.Message}");
            }
        }
    }
}
