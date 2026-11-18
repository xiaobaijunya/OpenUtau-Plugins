using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.CVVCExpression {

    /// <summary>
    /// OpenUtau external batch edit: sets VEL / BRE per CVVC phoneme kind
    /// (CV, VC, -CV, V R), ignoring pitch suffixes such as "a C4".
    /// </summary>
    public class CvvcExpressionAdjuster : BatchEdit {

        public const string DisplayName = "CVVC 音素参数 VEL/BRE (CVVC Expression)";

        public string Name => DisplayName;

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            try {
                selectedNotes ??= new List<UNote>();
                PluginLog.Info($"Open editor: notes={part.notes.Count}, selected={selectedNotes.Count}, phonemes={part.phonemes.Count}");
                var window = new ExpressionWindow(project, part, selectedNotes, docManager);
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
                PluginLog.Error(e, "Failed to open the CVVC expression editor.");
                docManager.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        internal static List<UNote> GetPartNotes(UVoicePart part) {
            return part.notes.OrderBy(n => n.position).ThenBy(n => n.duration).ToList();
        }

        internal static List<UNote> GetTargetNotes(UVoicePart part, IReadOnlyList<UNote> selectedNotes, ExpressionSettings settings) {
            var partNotes = GetPartNotes(part);
            if (settings.Scope == EditScope.Selected && selectedNotes != null && selectedNotes.Count > 0) {
                var selected = new HashSet<UNote>(selectedNotes);
                return partNotes.Where(n => selected.Contains(n)).ToList();
            }
            return partNotes;
        }

        internal static ExpressionPlan Plan(UProject project, UVoicePart part, IReadOnlyList<UNote> selectedNotes, ExpressionSettings settings) {
            var partNotes = GetPartNotes(part);
            var snapshots = ExpressionAdapter.BuildSnapshots(project, part, partNotes);
            var noteIndexes = new Dictionary<UNote, int>();
            for (int i = 0; i < partNotes.Count; i++) {
                noteIndexes[partNotes[i]] = i;
            }
            var targets = GetTargetNotes(part, selectedNotes, settings)
                .Where(n => noteIndexes.ContainsKey(n))
                .Select(n => noteIndexes[n])
                .ToList();
            var ranges = ExpressionAdapter.BuildRanges(project, part);
            return ExpressionPlanner.Plan(snapshots, targets, settings, ranges);
        }

        internal static int Apply(UProject project, UVoicePart part, ExpressionPlan plan, DocManager docManager) {
            return ExpressionAdapter.Apply(project, part, plan, docManager);
        }
    }
}
