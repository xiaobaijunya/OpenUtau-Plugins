using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.ConsonantLength {

    internal enum ChangeKind {
        /// <summary>Move p0/p1: preutter (and overlap) of a phoneme, in milliseconds.</summary>
        Preutter = 0,
        /// <summary>Move the left edge of a phoneme slot by changing its offset, in ticks.</summary>
        Position = 1,
    }

    /// <summary>
    /// A read only view of one UPhoneme, so the planning can be unit tested without OpenUtau.
    /// </summary>
    internal sealed class PhonemeSnapshot {
        public int NoteIndex { get; set; } = -1;
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string MappedName { get; set; } = string.Empty;
        public int Position { get; set; }
        public int Duration { get; set; }
        public double PositionMs { get; set; }
        public double DurationMs { get; set; }
        public double Preutter { get; set; }
        public double AutoPreutter { get; set; }
        public double Overlap { get; set; }
        public double AutoOverlap { get; set; }
        /// <summary>Current offset override, in ticks.</summary>
        public int OldOffset { get; set; }
        /// <summary>Position of the phoneme before this one in the part, int.MinValue when there is none.</summary>
        public int PreviousPosition { get; set; } = int.MinValue;
        public int NotePosition { get; set; }
        public bool HasError { get; set; }
        /// <summary>Caller data (the UPhoneme), never touched by the planner.</summary>
        public object Tag { get; set; }

        public int End => Position + Duration;
    }

    internal sealed class PlannedChange {
        public int SnapshotIndex { get; set; }
        public ChangeKind Kind { get; set; }
        public string PhonemeName { get; set; } = string.Empty;
        public string MatchedPrefix { get; set; } = string.Empty;
        public double Ratio { get; set; } = 1.0;
        public double BeforeMs { get; set; }
        public double AfterMs { get; set; }
        /// <summary>New absolute preutter delta (ms) for ChangeKind.Preutter.</summary>
        public float NewPreutterDelta { get; set; }
        /// <summary>New absolute overlap delta (ms) for ChangeKind.Preutter.</summary>
        public float NewOverlapDelta { get; set; }
        public bool HasOverlapChange { get; set; }
        /// <summary>New absolute offset in ticks for ChangeKind.Position.</summary>
        public int NewOffset { get; set; }
        public bool Clamped { get; set; }
    }

    internal sealed class PlanResult {
        public IReadOnlyList<PhonemeSnapshot> Snapshots { get; set; }
        public List<PlannedChange> Changes { get; } = new List<PlannedChange>();
        public List<string> Warnings { get; } = new List<string>();
        public int Matched { get; set; }
        public int Unmatched { get; set; }
        public int Unchanged { get; set; }
        public int NotesWithoutPhonemes { get; set; }
        public bool HasChanges => Changes.Count > 0;
    }

    /// <summary>
    /// Computes, from read only snapshots, which phoneme timing values have to change.
    /// Nothing here touches the document; the caller turns the result into OpenUtau commands.
    /// </summary>
    internal static class ConsonantPlanner {

        public static PlanResult Plan(
            IReadOnlyList<PhonemeSnapshot> phonemes,
            IEnumerable<int> targetNotes,
            PluginSettings settings,
            Func<int, double, int> msToTicks) {

            var result = new PlanResult { Snapshots = phonemes };
            if (phonemes == null || phonemes.Count == 0 || settings == null) {
                return result;
            }
            msToTicks ??= (refTick, ms) => (int)Math.Round(ms);
            settings.Sanitize();

            var matcher = PhonemeMatcher.Compile(settings.Rules);
            var byNote = new Dictionary<int, List<int>>();
            for (int i = 0; i < phonemes.Count; i++) {
                int noteIndex = phonemes[i].NoteIndex;
                if (noteIndex < 0) {
                    continue;
                }
                if (!byNote.TryGetValue(noteIndex, out var list)) {
                    list = new List<int>();
                    byNote[noteIndex] = list;
                }
                list.Add(i);
            }
            foreach (var list in byNote.Values) {
                list.Sort((a, b) => {
                    int byPos = phonemes[a].Position.CompareTo(phonemes[b].Position);
                    return byPos != 0 ? byPos : phonemes[a].Index.CompareTo(phonemes[b].Index);
                });
            }

            var done = new HashSet<int>();
            var noteIndexes = targetNotes == null
                ? byNote.Keys.OrderBy(k => k).ToList()
                : targetNotes.Where(k => byNote.ContainsKey(k)).Distinct().OrderBy(k => k).ToList();

            foreach (int noteIndex in noteIndexes) {
                var indexes = byNote[noteIndex];
                if (indexes.Count == 0) {
                    result.NotesWithoutPhonemes++;
                    continue;
                }
                switch (settings.Mode) {
                    case AdjustMode.CV: {
                        int mainIndex = FindMainPhoneme(phonemes, indexes, noteIndex);
                        if (mainIndex >= 0 && done.Add(mainIndex)) {
                            PlanPreutter(phonemes, mainIndex, settings, matcher, result);
                        }
                        break;
                    }
                    case AdjustMode.CVVC: {
                        int mainIndex = FindMainPhoneme(phonemes, indexes, noteIndex);
                        if (mainIndex <= 0) {
                            break;
                        }
                        int prevIndex = mainIndex - 1;
                        var prev = phonemes[prevIndex];
                        var main = phonemes[mainIndex];
                        bool sameNote = prev.NoteIndex == noteIndex;
                        if (prev.NoteIndex < 0 || prev.HasError || (!sameNote && !LooksLikeVc(prev.Name))) {
                            // Only a VC style phoneme (attached to the note, or written as "a n") is a consonant carrier.
                            // A plain CV phoneme of the previous note must not be touched.
                            break;
                        }
                        if (prev.End != main.Position || prev.Duration <= 10) {
                            break;
                        }
                        if (done.Add(prevIndex)) {
                            PlanSlot(phonemes, prevIndex, settings, matcher, result, msToTicks);
                        }
                        break;
                    }
                    case AdjustMode.AI: {
                        foreach (int index in indexes) {
                            if (done.Add(index)) {
                                PlanSlot(phonemes, index, settings, matcher, result, msToTicks);
                            }
                        }
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>The phoneme that carries the note onset: the last one starting at or before the note.</summary>
        static int FindMainPhoneme(IReadOnlyList<PhonemeSnapshot> phonemes, List<int> indexes, int noteIndex) {
            int notePosition = phonemes[indexes[0]].NotePosition;
            int best = -1;
            foreach (int i in indexes) {
                var ph = phonemes[i];
                if (ph.Position > notePosition) {
                    continue;
                }
                if (best < 0
                    || ph.Position > phonemes[best].Position
                    || ph.Position == phonemes[best].Position && ph.Index > phonemes[best].Index) {
                    best = i;
                }
            }
            return best >= 0 ? best : indexes[0];
        }

        static bool LooksLikeVc(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return false;
            }
            return name.IndexOf(' ') >= 0 || name.IndexOf('-') >= 0 || name.IndexOf('_') >= 0;
        }

        /// <summary>CV mode: scale preutter and overlap, i.e. move the p0 and p1 points.</summary>
        static void PlanPreutter(
            IReadOnlyList<PhonemeSnapshot> phonemes, int index, PluginSettings settings,
            PhonemeMatcher matcher, PlanResult result) {

            var ph = phonemes[index];
            if (ph.HasError) {
                return;
            }
            var match = matcher.Match(ph.Name, ph.MappedName);
            if (match == null) {
                result.Unmatched++;
                return;
            }
            result.Matched++;
            double ratio = EffectiveRatio(match, settings);
            if (Math.Abs(ratio - 1.0) < 0.001) {
                result.Unchanged++;
                return;
            }
            if (ph.Preutter < 0.5) {
                // No consonant before the vowel in this phoneme (e.g. an AI bank without oto preutter).
                result.Unchanged++;
                return;
            }
            double oldPreutter = ph.Preutter;
            double newPreutter = Math.Clamp(oldPreutter * ratio, settings.MinLengthMs, settings.MaxLengthMs);
            bool clamped = Math.Abs(newPreutter - oldPreutter * ratio) > 0.01;
            if (Math.Abs(newPreutter - oldPreutter) < 0.2) {
                result.Unchanged++;
                return;
            }
            double k = newPreutter / oldPreutter;
            double newOverlap = settings.ScaleOverlap ? ph.Overlap * k : ph.Overlap;
            if (newOverlap > newPreutter) {
                newOverlap = newPreutter;
            }

            result.Changes.Add(new PlannedChange {
                SnapshotIndex = index,
                Kind = ChangeKind.Preutter,
                PhonemeName = ph.Name,
                MatchedPrefix = match.MatchedPrefix,
                Ratio = ratio,
                BeforeMs = oldPreutter,
                AfterMs = newPreutter,
                NewPreutterDelta = (float)(newPreutter - ph.AutoPreutter),
                NewOverlapDelta = (float)(newOverlap - ph.AutoOverlap),
                HasOverlapChange = settings.ScaleOverlap,
                Clamped = clamped,
            });
            if (clamped && result.Warnings.Count < 5) {
                result.Warnings.Add($"\"{ph.Name}\" 的辅音长度被限制在 {settings.MinLengthMs:0.#}~{settings.MaxLengthMs:0.#}ms 内");
            }
        }

        /// <summary>CVVC / AI mode: scale the slot (duration) of a phoneme by moving its left edge.</summary>
        static void PlanSlot(
            IReadOnlyList<PhonemeSnapshot> phonemes, int index, PluginSettings settings,
            PhonemeMatcher matcher, PlanResult result, Func<int, double, int> msToTicks) {

            var ph = phonemes[index];
            if (ph.HasError || ph.Duration <= 10) {
                return;
            }
            var match = matcher.Match(ph.Name, ph.MappedName);
            if (match == null) {
                result.Unmatched++;
                return;
            }
            result.Matched++;
            double ratio = EffectiveRatio(match, settings);
            if (Math.Abs(ratio - 1.0) < 0.001) {
                result.Unchanged++;
                return;
            }
            double oldDuration = ph.DurationMs;
            if (oldDuration <= 1) {
                result.Unchanged++;
                return;
            }
            double newDuration = Math.Clamp(oldDuration * ratio, settings.MinLengthMs, settings.MaxLengthMs);
            bool clamped = Math.Abs(newDuration - oldDuration * ratio) > 0.01;
            if (Math.Abs(newDuration - oldDuration) < 0.5) {
                result.Unchanged++;
                return;
            }
            int deltaTicks = msToTicks(ph.Position, newDuration - oldDuration);
            if (deltaTicks == 0) {
                deltaTicks = newDuration > oldDuration ? 1 : -1;
            }
            int newPosition = ph.Position - deltaTicks;
            if (ph.PreviousPosition != int.MinValue) {
                int lowerBound = ph.PreviousPosition + 10;
                if (newPosition < lowerBound) {
                    newPosition = lowerBound;
                    clamped = true;
                }
            }
            newPosition = Math.Min(newPosition, ph.End - 10);
            if (newPosition == ph.Position) {
                result.Unchanged++;
                return;
            }
            int newOffset = ph.OldOffset + (newPosition - ph.Position);
            double msPerTick = ph.Duration > 0 ? ph.DurationMs / ph.Duration : 0;
            double afterMs = ph.DurationMs + (ph.Position - newPosition) * msPerTick;

            result.Changes.Add(new PlannedChange {
                SnapshotIndex = index,
                Kind = ChangeKind.Position,
                PhonemeName = ph.Name,
                MatchedPrefix = match.MatchedPrefix,
                Ratio = ratio,
                BeforeMs = ph.DurationMs,
                AfterMs = afterMs,
                NewOffset = newOffset,
                Clamped = clamped,
            });
            if (clamped && result.Warnings.Count < 5) {
                result.Warnings.Add($"\"{ph.Name}\" 的长度受最短/最长或前一个音素限制");
            }
        }

        static double EffectiveRatio(MatchResult match, PluginSettings settings) {
            double ratio = match.RuleRatio * settings.MasterRatio;
            return Math.Clamp(ratio, 0.1, 3.0);
        }
    }
}
