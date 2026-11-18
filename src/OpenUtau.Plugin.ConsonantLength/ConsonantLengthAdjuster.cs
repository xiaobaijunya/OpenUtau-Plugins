using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.ConsonantLength {

    /// <summary>
    /// OpenUtau external batch edit ("外部插件" in the piano roll right click menu).
    /// Opens a small configuration window, then applies the consonant timing changes.
    /// </summary>
    public class ConsonantLengthAdjuster : BatchEdit {

        public const string DisplayName = "辅音时长调整 (Consonant Length)";

        public string Name => DisplayName;

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            try {
                selectedNotes ??= new List<UNote>();
                PluginLog.Info($"Open editor: notes={part.notes.Count}, selected={selectedNotes.Count}, phonemes={part.phonemes.Count}");
                var window = new ConsonantLengthWindow(project, part, selectedNotes, docManager);
                var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner != null && owner.IsVisible) {
                    window.ShowDialog(owner).ContinueWith(t => {
                        if (t.Exception != null) {
                            PluginLog.Error(t.Exception, "Editor window failed.");
                        }
                    }, TaskScheduler.Default);
                } else {
                    window.Show();
                }
            } catch (Exception e) {
                PluginLog.Error(e, "Failed to open the consonant length editor.");
                docManager.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        /// <summary>Notes of the part, ordered by position.</summary>
        internal static List<UNote> GetPartNotes(UVoicePart part) {
            return part.notes.OrderBy(n => n.position).ThenBy(n => n.duration).ToList();
        }

        /// <summary>Notes the current settings apply to.</summary>
        internal static List<UNote> GetTargetNotes(UVoicePart part, IReadOnlyList<UNote> selectedNotes, PluginSettings settings) {
            var partNotes = GetPartNotes(part);
            if (settings.Scope == NoteScope.Selected && selectedNotes != null && selectedNotes.Count > 0) {
                var selected = new HashSet<UNote>(selectedNotes);
                return partNotes.Where(n => selected.Contains(n)).ToList();
            }
            return partNotes;
        }

        /// <summary>Builds the plan without touching the document.</summary>
        internal static PlanResult Plan(UProject project, UVoicePart part, IReadOnlyList<UNote> selectedNotes, PluginSettings settings) {
            var partNotes = GetPartNotes(part);
            var snapshots = VoicePartAdapter.BuildSnapshots(part, partNotes);
            var targets = GetTargetNotes(part, selectedNotes, settings);
            var noteIndexes = new Dictionary<UNote, int>();
            for (int i = 0; i < partNotes.Count; i++) {
                noteIndexes[partNotes[i]] = i;
            }
            var targetIndexes = targets
                .Where(n => noteIndexes.ContainsKey(n))
                .Select(n => noteIndexes[n])
                .ToList();
            int partPosition = part.position;
            var timeAxis = project.timeAxis;
            return ConsonantPlanner.Plan(
                snapshots,
                targetIndexes,
                settings,
                (refTick, ms) => timeAxis.MsToTickAt(ms, partPosition + refTick));
        }

        /// <summary>Applies a plan to the document inside one undo group. Returns the number of edited phonemes.</summary>
        internal static int Apply(UVoicePart part, PlanResult plan, DocManager docManager) {
            return VoicePartAdapter.Apply(part, plan, docManager);
        }
    }
}
