using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.ConsonantLength {

    /// <summary>
    /// Built in rule bundles.
    /// Chinese (pinyin initials) and Japanese (romaji + kana) consonants, each split into
    /// a voiceless group and a voiced group.
    /// </summary>
    internal static class ConsonantPresets {

        public const string VoicelessGroup = "voiceless";
        public const string VoicedGroup = "voiced";
        public const string CustomGroup = "custom";

        /// <summary>Mandarin voiceless initials: b p d t g k f h j q x zh ch sh z c s.</summary>
        public static readonly RulePreset ChineseVoiceless = Preset(
            "zh-voiceless", "汉语 · 清辅音 (Chinese voiceless)", "b p d t g k f h j q x zh ch sh z c s", VoicelessGroup,
            ("b", 0.60), ("p", 0.60), ("d", 0.60), ("t", 0.60),
            ("g", 0.55), ("k", 0.60),
            ("f", 0.75), ("h", 0.75), ("x", 0.75), ("s", 0.75),
            ("sh", 0.75), ("zh", 0.70), ("ch", 0.70), ("z", 0.70), ("c", 0.70),
            ("j", 0.65), ("q", 0.65));

        /// <summary>Mandarin voiced / sonorant initials: m n ng l r y w.</summary>
        public static readonly RulePreset ChineseVoiced = Preset(
            "zh-voiced", "汉语 · 浊辅音 (Chinese voiced / sonorant)", "m n ng l r y w", VoicedGroup,
            ("m", 0.80), ("n", 0.80), ("ng", 0.80),
            ("l", 0.80), ("r", 0.80),
            ("y", 0.90), ("w", 0.90));

        /// <summary>Japanese voiceless consonants, romaji aliases and kana aliases.</summary>
        public static readonly RulePreset JapaneseVoiceless = Preset(
            "ja-voiceless", "日语 · 清辅音 (Japanese voiceless)", "k s t h p ch ts sh f / か行 さ行 た行 は行 ぱ行 っ", VoicelessGroup,
            ("k,か,き,く,け,こ", 0.60),
            ("ky,きゃ,きゅ,きょ", 0.60),
            ("s,さ,す,せ,そ", 0.70),
            ("sh,し,しゃ,しゅ,しょ", 0.70),
            ("t,た,て,と", 0.60),
            ("ch,ち,ちゃ,ちゅ,ちょ", 0.65),
            ("ts,つ", 0.65),
            ("h,は,ひ,へ,ほ", 0.70),
            ("f,ふ,ふぁ,ふぃ,ふぇ,ふぉ", 0.70),
            ("p,ぱ,ぴ,ぷ,ぺ,ぽ", 0.55),
            ("py,ぴゃ,ぴゅ,ぴょ", 0.55),
            ("hy,ひゃ,ひゅ,ひょ", 0.70),
            ("っ", 0.50));

        /// <summary>Japanese voiced consonants, romaji aliases and kana aliases.</summary>
        public static readonly RulePreset JapaneseVoiced = Preset(
            "ja-voiced", "日语 · 浊辅音 (Japanese voiced)", "g z j d b / が行 ざ行 だ行 ば行 ま行 な行 ら行 や行 わ行", VoicedGroup,
            ("g,が,ぎ,ぐ,げ,ご", 0.55),
            ("gy,ぎゃ,ぎゅ,ぎょ", 0.55),
            ("z,ざ,ず,ぜ,ぞ", 0.60),
            ("j,じ,じゃ,じゅ,じょ", 0.65),
            ("d,だ,で,ど", 0.55),
            ("b,ば,び,ぶ,べ,ぼ", 0.50),
            ("by,びゃ,びゅ,びょ", 0.50),
            ("m,ま,み,む,め,も", 0.80),
            ("n,な,に,ぬ,ね,の,ん", 0.80),
            ("ny,にゃ,にゅ,にょ", 0.80),
            ("r,ら,り,る,れ,ろ", 0.80),
            ("y,や,ゆ,よ", 0.90),
            ("w,わ,を", 0.90),
            ("v,ゔ", 0.60));

        public static readonly IReadOnlyList<RulePreset> All = new List<RulePreset> {
            ChineseVoiceless,
            ChineseVoiced,
            JapaneseVoiceless,
            JapaneseVoiced,
        };

        static RulePreset Preset(string id, string name, string description, string group,
            params (string Prefix, double Ratio)[] rules) {
            return new RulePreset(id, name, description,
                rules.Select(r => new ConsonantRule(r.Prefix, r.Ratio, group)));
        }

        public static RulePreset Find(string id) {
            return All.FirstOrDefault(p => p.Id == id);
        }

        /// <summary>Add every rule of <paramref name="source"/> that is not already present.</summary>
        public static void Merge(List<ConsonantRule> target, IEnumerable<ConsonantRule> source) {
            foreach (var rule in source) {
                if (!target.Any(r => string.Equals(r.Prefix, rule.Prefix, System.StringComparison.OrdinalIgnoreCase))) {
                    target.Add(rule.Clone());
                }
            }
        }

        public static List<ConsonantRule> CreateDefaultRules() {
            var rules = new List<ConsonantRule>();
            foreach (var preset in All) {
                Merge(rules, preset.Rules);
            }
            return rules;
        }
    }
}
