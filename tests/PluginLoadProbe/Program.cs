using System;
using System.Linq;
using System.Reflection;

namespace PluginLoadProbe {

    /// <summary>
    /// Mirrors DocManager.SearchAllPlugins: loads OpenUtau.Core.dll and the plugin dll and checks
    /// that exactly one concrete BatchEdit type with a public parameterless constructor is found.
    /// Usage: PluginLoadProbe &lt;OpenUtau.Core.dll&gt; &lt;plugin.dll&gt;
    /// </summary>
    internal static class Program {
        static int Main(string[] args) {
            if (args.Length < 2) {
                Console.WriteLine("usage: PluginLoadProbe <OpenUtau.Core.dll> <plugin.dll>");
                return 2;
            }
            try {
                var core = Assembly.LoadFrom(args[0]);
                var batchEdit = core.GetType("OpenUtau.Core.Editing.BatchEdit", throwOnError: true);
                Console.WriteLine($"BatchEdit interface: {batchEdit.FullName}");

                var plugin = Assembly.LoadFrom(args[1]);
                int found = 0;
                foreach (var type in plugin.GetExportedTypes()) {
                    if (batchEdit.IsAssignableFrom(type)
                        && !type.IsAbstract
                        && type.GetConstructor(Type.EmptyTypes) != null) {
                        found++;
                        var instance = Activator.CreateInstance(type);
                        var name = (string)type.GetProperty("Name")?.GetValue(instance);
                        Console.WriteLine($"OK  {type.FullName} -> Name = \"{name}\"");
                    }
                }
                Console.WriteLine(found == 1 ? "PASS: exactly one plugin entry point" : $"FAIL: found {found} entry points");
                return found == 1 ? 0 : 1;
            } catch (Exception e) {
                Console.WriteLine("FAIL: " + e);
                return 1;
            }
        }
    }
}
