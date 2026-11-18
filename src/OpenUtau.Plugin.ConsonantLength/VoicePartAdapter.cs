using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.ConsonantLength {

    /// <summary>Bridges OpenUtau's UPhoneme/UCommand world and the pure planner.</summary>
    internal static class VoicePartAdapter {

        public static List<PhonemeSnapshot> BuildSnapshots(UVoicePart part, IReadOnlyList<UNote> notes) {
            var noteIndexes = new Dictionary<UNote, int>();
            for (int i = 0; i < notes.Count; i++) {
                noteIndexes[notes[i]] = i;
            }
            var snapshots = new List<PhonemeSnapshot>(part.phonemes.Count);
            for (int i = 0; i < part.phonemes.Count; i++) {
                var phoneme = part.phonemes[i];
                var note = phoneme.Parent;
                snapshots.Add(new PhonemeSnapshot {
                    NoteIndex = note != null && noteIndexes.TryGetValue(note, out int noteIndex) ? noteIndex : -1,
                    Index = phoneme.index,
                    Name = phoneme.phoneme ?? string.Empty,
                    MappedName = phoneme.phonemeMapped ?? string.Empty,
                    Position = phoneme.position,
                    Duration = phoneme.Duration,
                    PositionMs = phoneme.PositionMs,
                    DurationMs = phoneme.DurationMs,
                    Preutter = phoneme.preutter,
                    AutoPreutter = phoneme.autoPreutter,
                    Overlap = phoneme.overlap,
                    AutoOverlap = phoneme.autoOverlap,
                    OldOffset = GetOffset(note, phoneme.index),
                    PreviousPosition = i > 0 ? part.phonemes[i - 1].position : int.MinValue,
                    NotePosition = note != null ? note.position : phoneme.position,
                    HasError = phoneme.Error,
                    Tag = phoneme,
                });
            }
            return snapshots;
        }

        static int GetOffset(UNote note, int index) {
            if (note == null || note.phonemeOverrides == null) {
                return 0;
            }
            var over = note.phonemeOverrides.FirstOrDefault(o => o != null && o.index == index);
            return over != null && over.offset.HasValue ? over.offset.Value : 0;
        }

        public static int Apply(UVoicePart part, PlanResult plan, DocManager docManager) {
            if (plan == null || !plan.HasChanges) {
                return 0;
            }
            int applied = 0;
            docManager.StartUndoGroup("command.batch.note", true);
            try {
                foreach (var change in plan.Changes) {
                    if (change.SnapshotIndex < 0 || change.SnapshotIndex >= plan.Snapshots.Count) {
                        continue;
                    }
                    var phoneme = plan.Snapshots[change.SnapshotIndex].Tag as UPhoneme;
                    var note = phoneme?.Parent;
                    if (phoneme == null || note == null) {
                        continue;
                    }
                    switch (change.Kind) {
                        case ChangeKind.Preutter:
                            docManager.ExecuteCmd(new PhonemePreutterCommand(part, note, phoneme.index, change.NewPreutterDelta));
                            if (change.HasOverlapChange) {
                                docManager.ExecuteCmd(new PhonemeOverlapCommand(part, note, phoneme.index, change.NewOverlapDelta));
                            }
                            applied++;
                            break;
                        case ChangeKind.Position:
                            docManager.ExecuteCmd(new PhonemeOffsetCommand(part, note, phoneme.index, change.NewOffset));
                            applied++;
                            break;
                        case ChangeKind.Overlap:
                            docManager.ExecuteCmd(new PhonemeOverlapCommand(part, note, phoneme.index, change.NewOverlapDelta));
                            applied++;
                            break;
                    }
                }
            } finally {
                docManager.EndUndoGroup();
            }
            PluginLog.Info($"Applied {applied} phoneme timing changes.");
            return applied;
        }
    }
}
