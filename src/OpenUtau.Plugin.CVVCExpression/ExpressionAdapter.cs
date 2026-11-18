using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.CVVCExpression {

    /// <summary>Bridges OpenUtau's phonemes / commands and the pure expression planner.</summary>
    internal static class ExpressionAdapter {

        public static List<PhonemeExpressionSnapshot> BuildSnapshots(UProject project, UVoicePart part, IReadOnlyList<UNote> notes) {
            var track = project.tracks[part.trackNo];
            var noteIndexes = new Dictionary<UNote, int>();
            for (int i = 0; i < notes.Count; i++) {
                noteIndexes[notes[i]] = i;
            }
            var snapshots = new List<PhonemeExpressionSnapshot>(part.phonemes.Count);
            foreach (var phoneme in part.phonemes) {
                var note = phoneme.Parent;
                int noteIndex = note != null && noteIndexes.TryGetValue(note, out int index) ? index : -1;
                snapshots.Add(new PhonemeExpressionSnapshot {
                    NoteIndex = noteIndex,
                    PhonemeIndex = phoneme.index,
                    Name = phoneme.phoneme ?? string.Empty,
                    Kind = PhonemeClassifier.Classify(phoneme.phoneme),
                    Vel = ReadExpression(project, track, phoneme, ExpressionPlanner.VelAbbr),
                    Bre = ReadExpression(project, track, phoneme, ExpressionPlanner.BreAbbr),
                    Tag = phoneme,
                });
            }
            return snapshots;
        }

        static float? ReadExpression(UProject project, UTrack track, UPhoneme phoneme, string abbr) {
            try {
                if (!track.TryGetExpDescriptor(project, abbr, out var descriptor) || descriptor == null) {
                    return null;
                }
                // Item1 is the effective value (phoneme override -> phonemizer value -> descriptor default).
                return phoneme.GetExpression(project, track, abbr).Item1;
            } catch (Exception e) {
                PluginLog.Warn($"Cannot read {abbr} of {phoneme.phoneme}: {e.Message}");
                return null;
            }
        }

        public static Dictionary<string, ExpressionRange> BuildRanges(UProject project, UVoicePart part) {
            var ranges = new Dictionary<string, ExpressionRange>();
            try {
                var track = project.tracks[part.trackNo];
                foreach (var abbr in new[] { ExpressionPlanner.VelAbbr, ExpressionPlanner.BreAbbr }) {
                    if (track.TryGetExpDescriptor(project, abbr, out var descriptor) && descriptor != null) {
                        ranges[abbr] = new ExpressionRange { Min = descriptor.min, Max = descriptor.max, Available = true };
                    } else {
                        ranges[abbr] = new ExpressionRange { Min = 0, Max = 100, Available = false };
                    }
                }
            } catch (Exception e) {
                PluginLog.Warn($"Cannot read expression descriptors: {e.Message}");
            }
            return ranges;
        }

        public static int Apply(UProject project, UVoicePart part, ExpressionPlan plan, DocManager docManager) {
            if (plan == null || !plan.HasChanges) {
                return 0;
            }
            var track = project.tracks[part.trackNo];
            int applied = 0;
            docManager.StartUndoGroup("command.batch.note", true);
            try {
                foreach (var change in plan.Changes) {
                    if (change.SnapshotIndex < 0 || change.SnapshotIndex >= plan.Snapshots.Count) {
                        continue;
                    }
                    var phoneme = plan.Snapshots[change.SnapshotIndex].Tag as UPhoneme;
                    if (phoneme == null) {
                        continue;
                    }
                    docManager.ExecuteCmd(new SetPhonemeExpressionCommand(project, track, part, phoneme, change.Abbr, change.NewValue));
                    applied++;
                }
            } finally {
                docManager.EndUndoGroup();
            }
            PluginLog.Info($"Applied {applied} phoneme expression changes.");
            return applied;
        }
    }
}
