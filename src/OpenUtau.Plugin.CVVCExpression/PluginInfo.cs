using System.Reflection;

namespace OpenUtau.Plugin.CVVCExpression {

    /// <summary>Version shown in the editor and written to the log.</summary>
    internal static class PluginInfo {
        public const string Version = "1.0.0";

        public static string AssemblyVersion {
            get {
                try {
                    return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Version;
                } catch {
                    return Version;
                }
            }
        }
    }
}
