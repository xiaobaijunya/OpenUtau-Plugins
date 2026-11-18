using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.ConsonantLength {

    internal sealed class MatchResult {
        public ConsonantRule Rule { get; set; }
        public string MatchedPrefix { get; set; } = string.Empty;
        public string Candidate { get; set; } = string.Empty;
        public double RuleRatio => Rule?.Ratio ?? 1.0;
    }

    /// <summary>
    /// Decides whether a phoneme name starts with a consonant listed in the rules.
    /// The longest prefix wins, so both "s" and "sh" can be declared and "shao" picks "sh".
    /// </summary>
    internal sealed class PhonemeMatcher {

        /// <summary>Phoneme names that are silence / rests and must never be matched by a rule.</summary>
        static readonly HashSet<string> SilenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "R", "-", "sil", "pau", "br", "cl", "sp", "ap", "rest", "・", "…", "，", "、",
        };

        readonly List<KeyValuePair<string, ConsonantRule>> prefixes;

        PhonemeMatcher(List<KeyValuePair<string, ConsonantRule>> prefixes) {
            this.prefixes = prefixes;
        }

        public static PhonemeMatcher Compile(IEnumerable<ConsonantRule> rules) {
            var list = new List<KeyValuePair<string, ConsonantRule>>();
            if (rules != null) {
                foreach (var rule in rules) {
                    if (rule == null || !rule.Enabled) {
                        continue;
                    }
                    foreach (var prefix in rule.GetPrefixes()) {
                        list.Add(new KeyValuePair<string, ConsonantRule>(prefix.ToLowerInvariant(), rule));
                    }
                }
            }
            // Longest prefix first, so "sh" beats "s" for "shao".
            list.Sort((a, b) => {
                int byLength = b.Key.Length.CompareTo(a.Key.Length);
                return byLength != 0 ? byLength : string.CompareOrdinal(a.Key, b.Key);
            });
            return new PhonemeMatcher(list);
        }

        public MatchResult Match(string phonemeName, string mappedName = null) {
            var result = MatchSingle(phonemeName);
            if (result == null && !string.IsNullOrWhiteSpace(mappedName) && !string.Equals(mappedName, phonemeName, StringComparison.Ordinal)) {
                result = MatchSingle(mappedName);
            }
            return result;
        }

        MatchResult MatchSingle(string rawName) {
            if (string.IsNullOrWhiteSpace(rawName) || prefixes.Count == 0) {
                return null;
            }
            foreach (var candidate in GetCandidates(rawName)) {
                if (candidate.Length == 0 || SilenceNames.Contains(candidate)) {
                    continue;
                }
                string lower = candidate.ToLowerInvariant();
                foreach (var pair in prefixes) {
                    if (lower.StartsWith(pair.Key, StringComparison.Ordinal)) {
                        return new MatchResult {
                            Rule = pair.Value,
                            MatchedPrefix = pair.Key,
                            Candidate = candidate,
                        };
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Candidate strings extracted from a phoneme name, in priority order.
        /// "shao" -> ["shao"]; "a n" (VC alias) -> ["n", "a"] so the consonant token wins.
        /// </summary>
        static IEnumerable<string> GetCandidates(string rawName) {
            string name = rawName.Trim();
            int suffix = name.IndexOf(':');
            if (suffix > 0) {
                name = name.Substring(0, suffix).Trim();
            }
            suffix = name.IndexOf('：');
            if (suffix > 0) {
                name = name.Substring(0, suffix).Trim();
            }
            if (name.Length == 0) {
                yield break;
            }
            if (name.IndexOf(' ') < 0 && name.IndexOf('-') < 0 && name.IndexOf('_') < 0 && name.IndexOf('+') < 0) {
                yield return name;
                yield break;
            }
            var tokens = name
                .Split(new[] { ' ', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 0)
                .ToArray();
            for (int i = tokens.Length - 1; i >= 0; i--) {
                yield return tokens[i];
            }
            // "a n" with a rule written as the full alias.
            yield return name;
        }
    }
}
