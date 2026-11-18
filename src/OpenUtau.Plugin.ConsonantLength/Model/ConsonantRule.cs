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
        public List<ConsonantRule> Rules { get; set; } = new List<ConsonantRule>();

        public PluginSettings Clone() {
            return new PluginSettings {
                Mode = Mode,
                Scope = Scope,
                MasterRatio = MasterRatio,
                MinLengthMs = MinLengthMs,
                MaxLengthMs = MaxLengthMs,
                ScaleOverlap = ScaleOverlap,
                Rules = Rules.Select(r => r.Clone()).ToList(),
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
