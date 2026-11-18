using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.ConsonantLength {

    internal enum ChangeKind {
        /// <summary>Move p0/p1: preutter (and overlap) of a phoneme, in milliseconds.</summary>
        Preutter = 0,
        /// <summary>Move the left edge of a phoneme slot by changing its offset, in ticks.</summary>
        Position = 1,
        /// <summary>Move p1 only: overlap of a phoneme, in milliseconds.</summary>
        Overlap = 2,
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
        /// <summary>New absolute overlap delta (ms) for ChangeKind.Preutter / Overlap.</summary>
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

    internal enum TargetSelector {
        /// <summary>The phoneme that carries the note onset (CV).</summary>
        NoteOnset = 0,
        /// <summary>The VC phoneme right before the note's vowel phoneme (CVVC).</summary>
        PreviousVc = 1,
        /// <summary>Every phoneme of the note (AI).</summary>
        AllMatched = 2,
    }

    /// <summary>
    /// Computes, from read only snapshots, which phoneme timing values have to change.
    /// Nothing here touches the document; the caller turns the result into OpenUtau commands.
    /// </summary>
    internal static class ConsonantPlanner {

        /// <summary>The duration page: CV / CVVC / AI.</summary>
        public static PlanResult Plan(
            IReadOnlyList<PhonemeSnapshot> phonemes,
            IEnumerable<int> targetNotes,
            PluginSettings settings,
            Func<int, double, int> msToTicks) {

            var result = Begin(phonemes, settings, msToTicks);
            if (result == null) {
                return new PlanResult { Snapshots = phonemes };
            }
            var byNote = GroupByNote(phonemes);
            var noteIndexes = ResolveNotes(byNote, targetNotes, result);
            var matcher = PhonemeMatcher.Compile(settings.Rules);
            var done = new HashSet<int>();

            foreach (int noteIndex in noteIndexes) {
                TargetSelector selector = settings.Mode == AdjustMode.CV ? TargetSelector.NoteOnset
                    : settings.Mode == AdjustMode.CVVC ? TargetSelector.PreviousVc
                    : TargetSelector.AllMatched;
                foreach (int index in SelectTargets(phonemes, byNote, noteIndex, selector)) {
                    if (!done.Add(index)) {
                        continue;
                    }
                    if (selector == TargetSelector.NoteOnset) {
                        PlanPreutter(phonemes, index, settings, matcher, result);
                    } else {
                        PlanSlot(phonemes, index, settings, matcher, result, msToTicks);
                    }
                }
            }
            return result;
        }

        /// <summary>The p1 page: change only the overlap of the selected phonemes.</summary>
        public static PlanResult PlanOverlap(
            IReadOnlyList<PhonemeSnapshot> phonemes,
            IEnumerable<int> targetNotes,
            PluginSettings settings,
            Func<int, double, int> msToTicks) {

            var result = Begin(phonemes, settings, msToTicks);
            if (result == null) {
                return new PlanResult { Snapshots = phonemes };
            }
            var byNote = GroupByNote(phonemes);
            var noteIndexes = ResolveNotes(byNote, targetNotes, result);
            var matcher = PhonemeMatcher.Compile(settings.Rules);
            var done = new HashSet<int>();
            TargetSelector selector = settings.P1Target == P1Target.NoteOnset ? TargetSelector.NoteOnset
                : settings.P1Target == P1Target.PreviousVc ? TargetSelector.PreviousVc
                : TargetSelector.AllMatched;

            foreach (int noteIndex in noteIndexes) {
                foreach (int index in SelectTargets(phonemes, byNote, noteIndex, selector)) {
                    if (done.Add(index)) {
                        PlanOverlapForIndex(phonemes, index, settings, matcher, result);
                    }
                }
            }
            return result;
        }

        /// <summary>The alignment page: put the VC consonant's p0 on the previous phoneme's pre line (or another anchor).</summary>
        public static PlanResult PlanAlign(
            IReadOnlyList<PhonemeSnapshot> phonemes,
            IEnumerable<int> targetNotes,
            PluginSettings settings,
            Func<int, double, int> msToTicks) {

            var result = Begin(phonemes, settings, msToTicks);
            if (result == null) {
                return new PlanResult { Snapshots = phonemes };
            }
            var byNote = GroupByNote(phonemes);
            var noteIndexes = ResolveNotes(byNote, targetNotes, result);
            var done = new HashSet<int>();

            var selector = settings.AlignTarget == AlignTarget.NoteOnset ? TargetSelector.NoteOnset : TargetSelector.PreviousVc;
            foreach (int noteIndex in noteIndexes) {
                foreach (int index in SelectTargets(phonemes, byNote, noteIndex, selector)) {
                    if (done.Add(index)) {
                        PlanAlignForIndex(phonemes, index, settings, result, msToTicks);
                    }
                }
            }
            return result;
        }

        #region planning internals

        static PlanResult Begin(IReadOnlyList<PhonemeSnapshot> phonemes, PluginSettings settings, Func<int, double, int> msToTicks) {
            if (phonemes == null || phonemes.Count == 0 || settings == null) {
                return null;
            }
            settings.Sanitize();
            return new PlanResult { Snapshots = phonemes };
        }

        static Dictionary<int, List<int>> GroupByNote(IReadOnlyList<PhonemeSnapshot> phonemes) {
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
            return byNote;
        }

        static List<int> ResolveNotes(Dictionary<int, List<int>> byNote, IEnumerable<int> targetNotes, PlanResult result) {
            var noteIndexes = targetNotes == null
                ? byNote.Keys.OrderBy(k => k).ToList()
                : targetNotes.Where(k => byNote.ContainsKey(k)).Distinct().OrderBy(k => k).ToList();
            foreach (int noteIndex in noteIndexes) {
                if (byNote[noteIndex].Count == 0) {
                    result.NotesWithoutPhonemes++;
                }
            }
            return noteIndexes;
        }

        static List<int> SelectTargets(
            IReadOnlyList<PhonemeSnapshot> phonemes,
            Dictionary<int, List<int>> byNote,
            int noteIndex,
            TargetSelector selector) {

            if (!byNote.TryGetValue(noteIndex, out var indexes) || indexes.Count == 0) {
                return EmptyList;
            }
            switch (selector) {
                case TargetSelector.NoteOnset: {
                    int mainIndex = FindMainPhoneme(phonemes, indexes, noteIndex);
                    return mainIndex >= 0 ? new List<int> { mainIndex } : EmptyList;
                }
                case TargetSelector.PreviousVc: {
                    int mainIndex = FindMainPhoneme(phonemes, indexes, noteIndex);
                    if (mainIndex <= 0) {
                        return EmptyList;
                    }
                    int prevIndex = mainIndex - 1;
                    var prev = phonemes[prevIndex];
                    var main = phonemes[mainIndex];
                    // Only a VC style phoneme (attached to the note, or written as "a n") carries the consonant.
                    // A plain CV phoneme of the previous note must not be touched.
                    bool sameNote = prev.NoteIndex == noteIndex;
                    if (prev.NoteIndex < 0 || prev.HasError || (!sameNote && !LooksLikeVc(prev.Name))) {
                        return EmptyList;
                    }
                    if (prev.End != main.Position || prev.Duration <= 10) {
                        return EmptyList;
                    }
                    return new List<int> { prevIndex };
                }
                default:
                    return indexes;
            }
        }

        static readonly List<int> EmptyList = new List<int>();

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

        /// <summary>p1 page: set the overlap (p1) of a phoneme.</summary>
        static void PlanOverlapForIndex(
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
            if (ph.Preutter < 0.1 && settings.P1Unit == P1Unit.FractionOfPreutter) {
                result.Unchanged++;
                return;
            }
            double newOverlap;
            double ratio = 1.0;
            switch (settings.P1Unit) {
                case P1Unit.Ratio:
                    ratio = Math.Clamp(settings.P1Ratio, 0.1, 3.0);
                    newOverlap = ph.Overlap * ratio;
                    break;
                case P1Unit.Milliseconds:
                    newOverlap = settings.P1Milliseconds;
                    break;
                default:
                    newOverlap = ph.Preutter * settings.P1Fraction;
                    break;
            }
            bool clamped = false;
            if (newOverlap > ph.Preutter) {
                newOverlap = ph.Preutter;
                clamped = true;
            }
            if (newOverlap < -Math.Max(ph.Preutter, 50)) {
                newOverlap = -Math.Max(ph.Preutter, 50);
                clamped = true;
            }
            if (Math.Abs(newOverlap - ph.Overlap) < 0.1) {
                result.Unchanged++;
                return;
            }
            result.Changes.Add(new PlannedChange {
                SnapshotIndex = index,
                Kind = ChangeKind.Overlap,
                PhonemeName = ph.Name,
                MatchedPrefix = match.MatchedPrefix,
                Ratio = ratio,
                BeforeMs = ph.Overlap,
                AfterMs = newOverlap,
                NewOverlapDelta = (float)(newOverlap - ph.AutoOverlap),
                HasOverlapChange = true,
                Clamped = clamped,
            });
            if (clamped && result.Warnings.Count < 5) {
                result.Warnings.Add($"\"{ph.Name}\" 的 p1 被限制在 0~p0（{ph.Preutter:0.0}ms）之间");
            }
        }

        /// <summary>Alignment page: move the VC phoneme so that its p0 lands on the anchor.</summary>
        static void PlanAlignForIndex(
            IReadOnlyList<PhonemeSnapshot> phonemes, int index, PluginSettings settings,
            PlanResult result, Func<int, double, int> msToTicks) {

            if (index <= 0) {
                return;
            }
            var vc = phonemes[index];
            var prev = phonemes[index - 1];
            if (vc.HasError || prev.HasError || vc.Duration <= 10) {
                return;
            }
            int anchorTicks;
            string anchorName;
            switch (settings.AlignAnchor) {
                case AlignAnchor.PreviousPosition:
                    anchorTicks = prev.Position;
                    anchorName = "前一个音素的位置线";
                    break;
                case AlignAnchor.PreviousEnd:
                    anchorTicks = prev.End;
                    anchorName = "前一个音素的终点";
                    break;
                default:
                    anchorTicks = prev.Position - msToTicks(prev.Position, prev.Preutter);
                    anchorName = "前一个音素的 pre 线";
                    break;
            }
            double msPerTick = vc.Duration > 0 ? vc.DurationMs / vc.Duration : 0;
            bool clamped = false;

            if (settings.AlignMode == AlignMode.MoveP0Only) {
                // Keep the phoneme where it is, change preutter so that p0 = anchor.
                int preutterTicks = vc.Position - anchorTicks;
                double newPreutter = preutterTicks * msPerTick;
                if (newPreutter > settings.MaxLengthMs) {
                    newPreutter = settings.MaxLengthMs;
                    clamped = true;
                }
                if (newPreutter < 0) {
                    newPreutter = 0;
                    clamped = true;
                }
                if (Math.Abs(newPreutter - vc.Preutter) < 0.2) {
                    result.Unchanged++;
                    return;
                }
                result.Changes.Add(new PlannedChange {
                    SnapshotIndex = index,
                    Kind = ChangeKind.Preutter,
                    PhonemeName = vc.Name,
                    MatchedPrefix = anchorName,
                    Ratio = 1.0,
                    BeforeMs = vc.Preutter,
                    AfterMs = newPreutter,
                    NewPreutterDelta = (float)(newPreutter - vc.AutoPreutter),
                    HasOverlapChange = true,
                    NewOverlapDelta = (float)(Math.Min(vc.Overlap, newPreutter) - vc.AutoOverlap),
                    Clamped = clamped,
                });
                return;
            }

            int preutterTicksOfVc = msToTicks(vc.Position, vc.Preutter);
            int newPosition = anchorTicks + preutterTicksOfVc;
            int lowerBound = prev.Position + 10;
            if (newPosition < lowerBound) {
                newPosition = lowerBound;
                clamped = true;
            }
            newPosition = Math.Min(newPosition, vc.End - 10);
            if (newPosition == vc.Position) {
                result.Unchanged++;
                return;
            }
            int newOffset = vc.OldOffset + (newPosition - vc.Position);
            result.Changes.Add(new PlannedChange {
                SnapshotIndex = index,
                Kind = ChangeKind.Position,
                PhonemeName = vc.Name,
                MatchedPrefix = anchorName,
                Ratio = 1.0,
                BeforeMs = vc.PositionMs,
                AfterMs = vc.PositionMs + (newPosition - vc.Position) * msPerTick,
                NewOffset = newOffset,
                Clamped = clamped,
            });
            if (clamped && result.Warnings.Count < 5) {
                result.Warnings.Add($"\"{vc.Name}\" 被前一个音素限制住了，无法完全对齐到{anchorName}");
            }
        }

        static double EffectiveRatio(MatchResult match, PluginSettings settings) {
            double ratio = match.RuleRatio * settings.MasterRatio;
            return Math.Clamp(ratio, 0.1, 3.0);
        }

        #endregion
    }
}
