using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.CVVCExpression {

    internal struct ExpressionRange {
        public double Min;
        public double Max;
        public bool Available;
    }

    /// <summary>A read only view of one phoneme, so planning can be unit tested without OpenUtau.</summary>
    internal sealed class PhonemeExpressionSnapshot {
        public int NoteIndex { get; set; } = -1;
        public int PhonemeIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public CvvcPhonemeKind Kind { get; set; }
        /// <summary>Effective current VEL, null when unknown.</summary>
        public float? Vel { get; set; }
        /// <summary>Effective current BRE, null when unknown.</summary>
        public float? Bre { get; set; }
        public object Tag { get; set; }
    }

    internal sealed class PlannedExpressionChange {
        public int SnapshotIndex { get; set; }
        public string PhonemeName { get; set; } = string.Empty;
        public CvvcPhonemeKind Kind { get; set; }
        public string Abbr { get; set; } = string.Empty;
        public float? OldValue { get; set; }
        public float NewValue { get; set; }
    }

    internal sealed class ExpressionPlan {
        public IReadOnlyList<PhonemeExpressionSnapshot> Snapshots { get; set; }
        public List<PlannedExpressionChange> Changes { get; } = new List<PlannedExpressionChange>();
        public int[] CountByKind { get; } = new int[5];
        public List<string> Warnings { get; } = new List<string>();
        public int Skipped { get; set; }
        public bool HasChanges => Changes.Count > 0;

        public int CountOf(CvvcPhonemeKind kind) => CountByKind[(int)kind];
    }

    internal static class ExpressionPlanner {

        public const string VelAbbr = "vel";
        public const string BreAbbr = "bre";

        public static ExpressionPlan Plan(
            IReadOnlyList<PhonemeExpressionSnapshot> phonemes,
            IEnumerable<int> targetNotes,
            ExpressionSettings settings,
            IReadOnlyDictionary<string, ExpressionRange> ranges) {

            var plan = new ExpressionPlan { Snapshots = phonemes };
            if (phonemes == null || phonemes.Count == 0 || settings == null) {
                return plan;
            }
            settings.Sanitize();
            var targets = targetNotes == null ? null : new HashSet<int>(targetNotes);
            for (int i = 0; i < phonemes.Count; i++) {
                var phoneme = phonemes[i];
                if (phoneme.NoteIndex < 0) {
                    continue;
                }
                if (targets != null && !targets.Contains(phoneme.NoteIndex)) {
                    continue;
                }
                plan.CountByKind[(int)phoneme.Kind]++;
                if (phoneme.Kind == CvvcPhonemeKind.Silence) {
                    continue;
                }
                var kindSetting = settings.Get(phoneme.Kind);
                if (kindSetting == null || !kindSetting.Enabled) {
                    continue;
                }
                AddChange(plan, i, phoneme, kindSetting.Vel, VelAbbr, ranges, phoneme.Vel);
                AddChange(plan, i, phoneme, kindSetting.Bre, BreAbbr, ranges, phoneme.Bre);
            }
            return plan;
        }

        static void AddChange(
            ExpressionPlan plan, int index, PhonemeExpressionSnapshot phoneme, double? configured,
            string abbr, IReadOnlyDictionary<string, ExpressionRange> ranges, float? currentValue) {

            if (!configured.HasValue) {
                return;
            }
            double value = configured.Value;
            if (ranges != null && ranges.TryGetValue(abbr, out var range)) {
                if (!range.Available) {
                    if (plan.Warnings.Count < 5) {
                        plan.Warnings.Add($"当前音轨没有 {abbr.ToUpperInvariant()} 参数，已跳过这一项");
                    }
                    return;
                }
                if (value < range.Min) {
                    value = range.Min;
                } else if (value > range.Max) {
                    value = range.Max;
                }
            }
            if (currentValue.HasValue && Math.Abs(currentValue.Value - value) < 0.01) {
                plan.Skipped++;
                return;
            }
            plan.Changes.Add(new PlannedExpressionChange {
                SnapshotIndex = index,
                PhonemeName = phoneme.Name,
                Kind = phoneme.Kind,
                Abbr = abbr,
                OldValue = currentValue,
                NewValue = (float)value,
            });
        }
    }
}
