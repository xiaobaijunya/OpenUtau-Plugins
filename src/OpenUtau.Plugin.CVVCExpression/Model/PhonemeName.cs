using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OpenUtau.Plugin.CVVCExpression {

    /// <summary>How a CVVC phoneme is used: CV / VC / -CV / V R.</summary>
    internal enum CvvcPhonemeKind {
        Cv = 0,
        Vc = 1,
        DashCv = 2,
        Vr = 3,
        Silence = 4,
    }

    /// <summary>Strips pitch and voice color suffixes before classifying a phoneme name.</summary>
    internal static class PhonemeNameNormalizer {

        // "a:2" / "a：柔和" -> "a"
        static readonly Regex ColorSuffix = new Regex(@"\s*[:：]\s*\S*$", RegexOptions.Compiled);

        // "a C4" / "a#4" / "a 4" / "a_C4" / "a-4" / "a4" -> "a"
        // Some voice banks put a space inside the pitch suffix, so the space alone must not decide the type.
        static readonly Regex PitchSuffix = new Regex(
            @"(?:[\s_\-]+(?:[A-Ga-g][#b]?\-?\d{1,2}|[#b]?\d{1,2})|[A-Ga-g][#b]?\-?\d{1,2}|[#b]\d{1,2}|\d{1,2})$",
            RegexOptions.Compiled);

        public static string Normalize(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return string.Empty;
            }
            string result = ColorSuffix.Replace(name.Trim(), string.Empty).Trim();
            if (result.Length <= 1) {
                return result;
            }
            // Strip the suffix from everything after the first character, so "a#4" keeps its "a"
            // while "a C4" and "a n C4" lose the pitch token.
            string head = result.Substring(0, 1);
            string tail = PitchSuffix.Replace(result.Substring(1), string.Empty);
            string stripped = (head + tail).Trim();
            return stripped.Length == 0 ? result : stripped;
        }
    }

    /// <summary>Classifies a phoneme name into CV / VC / -CV / V R.</summary>
    internal static class PhonemeClassifier {

        static readonly HashSet<string> SilenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "R", "-", "sil", "pau", "br", "cl", "sp", "ap", "rest", "・", "…",
        };

        /// <summary>
        /// - contains "-"            -> -CV
        /// - contains "R"            -> V R
        /// - contains a space        -> VC
        /// - anything else           -> CV
        /// Pitch / color suffixes are stripped first, so "a C4" is still CV.
        /// </summary>
        public static CvvcPhonemeKind Classify(string rawName) {
            if (string.IsNullOrWhiteSpace(rawName)) {
                return CvvcPhonemeKind.Silence;
            }
            string name = PhonemeNameNormalizer.Normalize(rawName);
            if (string.IsNullOrEmpty(name) || SilenceNames.Contains(name)) {
                return CvvcPhonemeKind.Silence;
            }
            if (name.IndexOf('-') >= 0) {
                return CvvcPhonemeKind.DashCv;
            }
            if (name.IndexOf('R') >= 0) {
                return CvvcPhonemeKind.Vr;
            }
            if (name.IndexOf(' ') >= 0) {
                return CvvcPhonemeKind.Vc;
            }
            return CvvcPhonemeKind.Cv;
        }

        public static string Describe(CvvcPhonemeKind kind) {
            switch (kind) {
                case CvvcPhonemeKind.Vc: return "VC";
                case CvvcPhonemeKind.DashCv: return "-CV";
                case CvvcPhonemeKind.Vr: return "V R";
                case CvvcPhonemeKind.Cv: return "CV";
                default: return "静音/休止";
            }
        }

        public static string DescribeRule(CvvcPhonemeKind kind) {
            switch (kind) {
                case CvvcPhonemeKind.Vc: return "含空格，例如 a n";
                case CvvcPhonemeKind.DashCv: return "含 -，例如 - a";
                case CvvcPhonemeKind.Vr: return "含 R，例如 a R";
                case CvvcPhonemeKind.Cv: return "不含空格、- 和 R，例如 a / shao";
                default: return "R / - / sil / pau 等";
            }
        }
    }
}
