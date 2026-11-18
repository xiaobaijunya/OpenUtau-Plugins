using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUtau.Core;

namespace OpenUtau.Plugin.CVVCExpression {

    internal static class PluginSettingsStore {

        public const string PluginFolderName = "CVVCExpression";
        public const string SettingsFileName = "settings.json";
        public const string LogFileName = "plugin.log";

        static string directory;

        public static string Directory {
            get {
                if (directory != null) {
                    return directory;
                }
                string path = null;
                try {
                    path = Path.Combine(PathManager.Inst.PluginsPath, PluginFolderName);
                } catch (Exception e) {
                    PluginLog.Warn($"Cannot resolve OpenUtau plugin path: {e.Message}");
                }
                if (string.IsNullOrWhiteSpace(path)) {
                    path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "OpenUtau", "Plugins", PluginFolderName);
                }
                directory = path;
                return directory;
            }
        }

        public static string SettingsFilePath => Path.Combine(Directory, SettingsFileName);

        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
        };

        public static ExpressionSettings Load() {
            try {
                string file = SettingsFilePath;
                if (!File.Exists(file)) {
                    return ExpressionSettings.CreateDefault();
                }
                string json = File.ReadAllText(file, Encoding.UTF8);
                var settings = JsonSerializer.Deserialize<ExpressionSettings>(json, JsonOptions);
                if (settings == null) {
                    return ExpressionSettings.CreateDefault();
                }
                settings.Sanitize();
                return settings;
            } catch (Exception e) {
                PluginLog.Error($"Failed to load settings, using defaults: {e}");
                return ExpressionSettings.CreateDefault();
            }
        }

        public static bool Save(ExpressionSettings settings) {
            if (settings == null) {
                return false;
            }
            try {
                System.IO.Directory.CreateDirectory(Directory);
                settings.Sanitize();
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json, new UTF8Encoding(false));
                return true;
            } catch (Exception e) {
                PluginLog.Error($"Failed to save settings: {e}");
                return false;
            }
        }
    }

    internal static class PluginLog {

        static readonly object Gate = new object();

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);
        public static void Error(Exception e, string message) => Write("ERROR", $"{message}{Environment.NewLine}{e}");

        static void Write(string level, string message) {
            try {
                lock (Gate) {
                    System.IO.Directory.CreateDirectory(PluginSettingsStore.Directory);
                    string file = Path.Combine(PluginSettingsStore.Directory, PluginSettingsStore.LogFileName);
                    var info = new FileInfo(file);
                    bool truncate = info.Exists && info.Length > 512 * 1024;
                    var builder = new StringBuilder();
                    if (truncate) {
                        builder.AppendLine("---- log truncated ----");
                    }
                    builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
                    if (truncate) {
                        File.WriteAllText(file, builder.ToString(), Encoding.UTF8);
                    } else {
                        File.AppendAllText(file, builder.ToString(), Encoding.UTF8);
                    }
                }
            } catch {
                // Logging must never break editing.
            }
        }
    }
}
