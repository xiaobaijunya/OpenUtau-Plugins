using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace UiLayoutProbe {

    internal static class Program {

        static void Main() {
            AppBuilder.Configure<ProbeApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            Console.WriteLine("=== construct fake project ===");
            UProject project = null;
            UVoicePart part = null;
            try {
                project = new UProject();
                part = new UVoicePart { trackNo = 0 };
                for (int i = 0; i < 3; i++) {
                    var note = UNote.Create();
                    note.position = i * 480;
                    note.duration = 480;
                    note.lyric = new[] { "shao", "a n", "- a" }[i];
                    part.notes.Add(note);
                }
                Console.WriteLine($"ok: tracks={project.tracks.Count}, notes={part.notes.Count}");
            } catch (Exception e) {
                Console.WriteLine("FAILED: " + e);
                return;
            }

            Console.WriteLine("=== plugin 1 window ===");
            var w1 = CreateWindow("OpenUtau.Plugin.ConsonantLength.ConsonantLengthAdjuster", project, part);
            if (w1 != null) {
                var tabs = w1.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
                if (tabs != null) {
                    for (int i = 0; i < tabs.ItemCount; i++) {
                        tabs.SelectedIndex = i;
                        Dispatcher.UIThread.RunJobs();
                        Capture(w1, $"ConsonantLengthWindow-tab{i}");
                    }
                }
            }
            Console.WriteLine("=== plugin 2 window ===");
            var w2 = CreateWindow("OpenUtau.Plugin.CVVCExpression.CvvcExpressionAdjuster", project, part);
            if (w2 != null) {
                DumpWindow(w2);
            }
        }

        static Window CreateWindow(string adjusterTypeName, UProject project, UVoicePart part) {
            try {
                string assemblyFile = adjusterTypeName.StartsWith("OpenUtau.Plugin.CVVCExpression")
                    ? "OpenUtau.Plugin.CVVCExpression.dll"
                    : "OpenUtau.Plugin.ConsonantLength.dll";
                var asm = System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(AppContext.BaseDirectory, assemblyFile));
                var windowType = asm.GetTypes().First(t => t.Name.EndsWith("Window"));
                var docManager = DocManager.Inst;
                var window = (Window)Activator.CreateInstance(windowType, project, part, new List<UNote>(), docManager);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Console.WriteLine($"created {windowType.Name}: size={window.Width}x{window.Height}");
                Capture(window, windowType.Name);
                return window;
            } catch (Exception e) {
                Console.WriteLine("FAILED to create window: " + e);
                return null;
            }
        }

        static void Capture(Window window, string name) {
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            if (frame == null) {
                Console.WriteLine("screenshot: null frame");
                return;
            }
            string outDir = System.IO.Path.Combine(AppContext.BaseDirectory, "shots");
            System.IO.Directory.CreateDirectory(outDir);
            string file = System.IO.Path.Combine(outDir, name + ".png");
            frame.Save(file);
            Console.WriteLine("screenshot: " + file);
        }

        static void DumpWindow(Window window) {
            void Walk(Visual v, int depth) {
                if (depth > 40) return;
                if (v is Control c && (c is TextBox || c is NumericUpDown || c is CheckBox || c is ComboBox || c is Button || c is TextBlock)) {
                    string text = c switch {
                        TextBlock tb => tb.Text,
                        TextBox tbx => tbx.Text,
                        NumericUpDown n => n.Value?.ToString(),
                        ContentControl cc => cc.Content?.ToString(),
                        _ => "",
                    };
                    text = (text ?? "").Replace("\n", " ").Replace("\r", " ");
                    if (text.Length > 30) text = text.Substring(0, 30) + "…";
                    var bounds = c.Bounds;
                    string flag = "";
                    if (c is TextBox && bounds.Width < 60) flag = "  <-- NARROW";
                    if (c is NumericUpDown) {
                        var inner = c.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                        flag = inner != null ? $"  innerTextBox={inner.Bounds.Width:0.#}x{inner.Bounds.Height:0.#}" : "  innerTextBox=none";
                        if (inner != null && inner.Bounds.Width < 45) flag += "  <-- NARROW";
                    }
                    Console.WriteLine($"{new string(' ', depth * 2)}{c.GetType().Name} [{bounds.X:0},{bounds.Y:0} {bounds.Width:0.#}x{bounds.Height:0.#}] \"{text}\"{flag}");
                }
                foreach (var child in v.GetVisualChildren()) {
                    Walk(child, depth + 1);
                }
            }
            Walk(window, 0);
        }

        internal sealed class ProbeApp : Application {
            public override void Initialize() {
                Styles.Add(new FluentTheme());
            }
        }
    }
}