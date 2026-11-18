using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.ConsonantLength {

    /// <summary>
    /// Which part of the phoneme chain is adjusted.
    /// </summary>
    internal enum AdjustMode {
        /// <summary>CV: scale preutter/overlap (p0/p1) of the phoneme that starts at the note.</summary>
        CV = 0,
        /// <summary>CVVC: scale the slot of the phoneme right before the note's vowel (the VC phoneme).</summary>
        CVVC = 1,
        /// <summary>AI: scale the slot of every phoneme whose name matches a rule (sh, ao, ch, ...).</summary>
        AI = 2,
    }

    internal enum NoteScope {
        Selected = 0,
        All = 1,
    }

    /// <summary>Which phoneme the p1 (overlap) page edits.</summary>
    internal enum P1Target {
        /// <summary>The consonant phoneme at the note onset (CV banks).</summary>
        NoteOnset = 0,
        /// <summary>The VC phoneme before the note vowel (CVVC banks).</summary>
        PreviousVc = 1,
        /// <summary>Every matched phoneme of the note (AI banks).</summary>
        AllMatched = 2,
    }

    /// <summary>How the new p1 value is computed.</summary>
    internal enum P1Unit {
        /// <summary>Multiply the current overlap.</summary>
        Ratio = 0,
        /// <summary>Set the overlap to an absolute value in milliseconds.</summary>
        Milliseconds = 1,
        /// <summary>Set the overlap as a fraction of the preutter (p0) length.</summary>
        FractionOfPreutter = 2,
    }

    /// <summary>Where the consonant p0 is aligned to on the "CVVC p0" page.</summary>
    internal enum AlignAnchor {
        /// <summary>p0 of the previous phoneme: prev.position - prev.preutter.</summary>
        PreviousPreLine = 0,
        /// <summary>The previous phoneme's position line.</summary>
        PreviousPosition = 1,
        /// <summary>The end of the previous phoneme.</summary>
        PreviousEnd = 2,
    }

    /// <summary>Which phoneme is moved on the alignment page.</summary>
    internal enum AlignTarget {
        /// <summary>The VC phoneme in front of the note vowel (a n).</summary>
        PreviousVc = 0,
        /// <summary>The consonant phoneme that starts at the note (CV).</summary>
        NoteOnset = 1,
    }

    /// <summary>Whether the whole phoneme or only its p0 point is moved.</summary>
    internal enum AlignMode {
        /// <summary>Shift the phoneme (keeps its consonant length, p0 lands on the anchor).</summary>
        MovePhoneme = 0,
        /// <summary>Only move p0 by changing preutter (the phoneme position stays).</summary>
        MoveP0Only = 1,
    }

    /// <summary>
    /// One matching rule: "if a phoneme name starts with one of the prefixes, multiply its length by Ratio".
    /// Prefix may contain several alternatives separated by ',' or '|', e.g. "k,か,き,く,け,こ".
    /// </summary>
    internal class ConsonantRule {
        public string Prefix { get; set; } = string.Empty;
        public double Ratio { get; set; } = 0.7;
        public bool Enabled { get; set; } = true;
        /// <summary>Informational group: "voiceless", "voiced" or "custom".</summary>
        public string Group { get; set; } = "custom";

        public ConsonantRule() { }

        public ConsonantRule(string prefix, double ratio, string group = "custom") {
            Prefix = prefix;
            Ratio = ratio;
            Group = group;
        }

        public ConsonantRule Clone() {
            return new ConsonantRule(Prefix, Ratio, Group) { Enabled = Enabled };
        }

        public string[] GetPrefixes() {
            if (string.IsNullOrWhiteSpace(Prefix)) {
                return Array.Empty<string>();
            }
            return Prefix
                .Split(new[] { ',', '|', '，', '｜', '/', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
        }

        public override string ToString() => $"{Prefix}={Ratio:0.00}";
    }

    /// <summary>A named, loadable bundle of rules.</summary>
    internal class RulePreset {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ConsonantRule> Rules { get; set; } = new List<ConsonantRule>();

        public RulePreset() { }
        public RulePreset(string id, string name, string description, IEnumerable<ConsonantRule> rules) {
            Id = id;
            Name = name;
            Description = description;
            Rules = rules.ToList();
        }
    }

    /// <summary>Everything the plugin remembers between runs.</summary>
    internal class PluginSettings {
        public AdjustMode Mode { get; set; } = AdjustMode.CV;
        public NoteScope Scope { get; set; } = NoteScope.Selected;
        /// <summary>Multiplied with every rule ratio. 1.0 = keep rule ratios.</summary>
        public double MasterRatio { get; set; } = 1.0;
        /// <summary>Lower clamp of preutter (CV) / phoneme slot (CVVC, AI), in milliseconds.</summary>
        public double MinLengthMs { get; set; } = 20;
        /// <summary>Upper clamp of preutter (CV) / phoneme slot (CVVC, AI), in milliseconds.</summary>
        public double MaxLengthMs { get; set; } = 400;
        /// <summary>Scale overlap (p1) together with preutter (p0) in CV mode.</summary>
        public bool ScaleOverlap { get; set; } = true;
        /// <summary>
        /// When true the ratio is applied to the singer's default values (oto autoPreutter / autoOverlap)
        /// instead of the values currently shown in the piano roll, so repeated runs do not stack up.
        /// </summary>
        public bool BaseOnAuto { get; set; } = true;
        public List<ConsonantRule> Rules { get; set; } = new List<ConsonantRule>();

        // --- p1 / overlap page ---
        public P1Target P1Target { get; set; } = P1Target.NoteOnset;
        public P1Unit P1Unit { get; set; } = P1Unit.Ratio;
        public double P1Ratio { get; set; } = 1.0;
        public double P1Milliseconds { get; set; } = 30;
        /// <summary>Fraction of preutter used when P1Unit is FractionOfPreutter (0..1).</summary>
        public double P1Fraction { get; set; } = 0.3;

        // --- CVVC p0 alignment page ---
        public AlignTarget AlignTarget { get; set; } = AlignTarget.PreviousVc;
        public AlignAnchor AlignAnchor { get; set; } = AlignAnchor.PreviousPreLine;
        public AlignMode AlignMode { get; set; } = AlignMode.MovePhoneme;

        public PluginSettings Clone() {
            return new PluginSettings {
                Mode = Mode,
                Scope = Scope,
                MasterRatio = MasterRatio,
                MinLengthMs = MinLengthMs,
                MaxLengthMs = MaxLengthMs,
                ScaleOverlap = ScaleOverlap,
                BaseOnAuto = BaseOnAuto,
                Rules = Rules.Select(r => r.Clone()).ToList(),
                P1Target = P1Target,
                P1Unit = P1Unit,
                P1Ratio = P1Ratio,
                P1Milliseconds = P1Milliseconds,
                P1Fraction = P1Fraction,
                AlignTarget = AlignTarget,
                AlignAnchor = AlignAnchor,
                AlignMode = AlignMode,
            };
        }

        public static PluginSettings CreateDefault() {
            var settings = new PluginSettings();
            foreach (var preset in ConsonantPresets.All) {
                ConsonantPresets.Merge(settings.Rules, preset.Rules);
            }
            return settings;
        }

        /// <summary>Clamp nonsensical values that could come from a hand edited json.</summary>
        public void Sanitize() {
            Mode = (AdjustMode)Math.Clamp((int)Mode, 0, 2);
            Scope = (NoteScope)Math.Clamp((int)Scope, 0, 1);
            P1Target = (P1Target)Math.Clamp((int)P1Target, 0, 2);
            P1Unit = (P1Unit)Math.Clamp((int)P1Unit, 0, 2);
            AlignTarget = (AlignTarget)Math.Clamp((int)AlignTarget, 0, 1);
            AlignAnchor = (AlignAnchor)Math.Clamp((int)AlignAnchor, 0, 2);
            AlignMode = (AlignMode)Math.Clamp((int)AlignMode, 0, 1);
            if (double.IsNaN(MasterRatio) || MasterRatio <= 0) {
                MasterRatio = 1.0;
            }
            MasterRatio = Math.Clamp(MasterRatio, 0.1, 3.0);
            if (double.IsNaN(MinLengthMs) || MinLengthMs < 0) {
                MinLengthMs = 0;
            }
            MinLengthMs = Math.Clamp(MinLengthMs, 0, 500);
            if (double.IsNaN(MaxLengthMs) || MaxLengthMs <= 0) {
                MaxLengthMs = 400;
            }
            MaxLengthMs = Math.Clamp(MaxLengthMs, 10, 2000);
            if (MaxLengthMs < MinLengthMs) {
                MaxLengthMs = MinLengthMs;
            }
            if (double.IsNaN(P1Ratio) || P1Ratio <= 0) {
                P1Ratio = 1.0;
            }
            P1Ratio = Math.Clamp(P1Ratio, 0.1, 3.0);
            if (double.IsNaN(P1Milliseconds)) {
                P1Milliseconds = 30;
            }
            P1Milliseconds = Math.Clamp(P1Milliseconds, -200, 400);
            if (double.IsNaN(P1Fraction)) {
                P1Fraction = 0.3;
            }
            P1Fraction = Math.Clamp(P1Fraction, -1.0, 1.0);
            Rules ??= new List<ConsonantRule>();
            foreach (var rule in Rules) {
                if (rule == null) {
                    continue;
                }
                if (double.IsNaN(rule.Ratio) || rule.Ratio <= 0) {
                    rule.Ratio = 1.0;
                }
                rule.Ratio = Math.Clamp(rule.Ratio, 0.1, 3.0);
                rule.Prefix ??= string.Empty;
            }
            Rules.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.Prefix));
        }
    }
}
