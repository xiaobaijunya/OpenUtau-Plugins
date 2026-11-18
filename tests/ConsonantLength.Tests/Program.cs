using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.ConsonantLength.Tests {

    /// <summary>
    /// Dependency free test runner for the planning logic.
    /// Run with: dotnet run --project tests/ConsonantLength.Tests
    /// </summary>
    internal static class Program {

        static int failures;
        static int checks;

        static void Check(bool condition, string name) {
            checks++;
            if (!condition) {
                failures++;
                Console.WriteLine($"FAIL  {name}");
            }
        }

        static void CheckClose(double expected, double actual, string name, double tolerance = 0.01) {
            Check(Math.Abs(expected - actual) <= tolerance, $"{name} (expected {expected:0.###}, actual {actual:0.###})");
        }

        // 120 bpm, 480 ticks per beat -> one tick is 500/480 ms.
        static int MsToTicks(int refTick, double ms) => (int)Math.Round(ms * 480.0 / 500.0);

        static PluginSettings Settings(AdjustMode mode, params (string Prefix, double Ratio)[] rules) {
            return new PluginSettings {
                Mode = mode,
                MasterRatio = 1.0,
                MinLengthMs = 10,
                MaxLengthMs = 1000,
                ScaleOverlap = true,
                Rules = rules.Select(r => new ConsonantRule(r.Prefix, r.Ratio)).ToList(),
            };
        }

        static void Main() {
            TestMatcher();
            TestPresets();
            TestCv();
            TestCvExistingDelta();
            TestCvOverlapOff();
            TestCvClamp();
            TestCvvc();
            TestCvvcVcOnPreviousNote();
            TestCvvcSkipsPlainCv();
            TestAi();
            TestAiMultiplePhonemes();
            TestScope();
            TestMasterRatio();
            TestDisabledRule();
            TestSilence();
            TestOverlap();
            TestOverlapAbsoluteAndFraction();
            TestOverlapClamp();
            TestAlignPreviousPosition();
            TestAlignPreviousPreLineClamped();
            TestAlignMoveP0Only();

            Console.WriteLine($"{checks - failures}/{checks} checks passed.");
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        static void TestMatcher() {
            var matcher = PhonemeMatcher.Compile(ConsonantPresets.CreateDefaultRules());
            Check(matcher.Match("shao")?.MatchedPrefix == "sh", "shao -> sh");
            Check(matcher.Match("sao")?.MatchedPrefix == "s", "sao -> s");
            Check(matcher.Match("zhang")?.MatchedPrefix == "zh", "zhang -> zh");
            Check(matcher.Match("zao")?.MatchedPrefix == "z", "zao -> z");
            Check(matcher.Match("chang")?.MatchedPrefix == "ch", "chang -> ch");
            Check(matcher.Match("a n")?.MatchedPrefix == "n", "VC a n -> n");
            Check(matcher.Match("n a")?.MatchedPrefix == "n", "CV n a -> n");
            Check(matcher.Match("しゃ")?.MatchedPrefix == "しゃ", "kana しゃ -> しゃ");
            Check(matcher.Match("か")?.MatchedPrefix == "か", "kana か -> か");
            Check(matcher.Match("kya")?.MatchedPrefix == "ky", "kya -> ky (longest prefix)");
            Check(matcher.Match("ao") == null, "ao has no consonant");
            Check(matcher.Match("e") == null, "e has no consonant");
        }

        static void TestPresets() {
            var matcher = PhonemeMatcher.Compile(ConsonantPresets.CreateDefaultRules());
            var pinyin = new[] { "ba", "pa", "ma", "fa", "da", "ta", "na", "la", "ga", "ka", "ha",
                "ji", "qi", "xi", "zhi", "chi", "shi", "ri", "zi", "ci", "si", "ya", "wa", "nv" };
            foreach (var name in pinyin) {
                Check(matcher.Match(name) != null, $"preset covers pinyin {name}");
            }
            var japanese = new[] { "ka", "sa", "ta", "na", "ha", "ma", "ya", "ra", "wa", "ga", "za", "da", "ba", "pa",
                "sha", "cha", "tsu", "kya", "か", "さ", "た", "な", "は", "ま", "や", "ら", "わ", "が", "ざ", "だ", "ば", "ぱ" };
            foreach (var name in japanese) {
                Check(matcher.Match(name) != null, $"preset covers japanese {name}");
            }
        }

        static void TestCv() {
            var settings = Settings(AdjustMode.CV, ("sh", 0.7));
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot {
                    NoteIndex = 0, Index = 0, Name = "shao", Position = 0, Duration = 480,
                    PositionMs = 0, DurationMs = 500, Preutter = 100, AutoPreutter = 100,
                    Overlap = 20, AutoOverlap = 20, NotePosition = 0,
                },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "cv: one change");
            if (plan.Changes.Count == 1) {
                var c = plan.Changes[0];
                Check(c.Kind == ChangeKind.Preutter, "cv: preutter kind");
                CheckClose(70, c.AfterMs, "cv: preutter 100*0.7");
                CheckClose(-30, c.NewPreutterDelta, "cv: preutter delta");
                CheckClose(-6, c.NewOverlapDelta, "cv: overlap scales 20->14");
                Check(c.HasOverlapChange, "cv: overlap change flagged");
            }
        }

        static void TestCvExistingDelta() {
            var settings = Settings(AdjustMode.CV, ("sh", 0.5));
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot {
                    NoteIndex = 0, Index = 0, Name = "shao", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 130, AutoPreutter = 100,
                    Overlap = 20, AutoOverlap = 20, NotePosition = 0,
                },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "cv existing delta: one change");
            if (plan.Changes.Count == 1) {
                CheckClose(65, plan.Changes[0].AfterMs, "cv existing delta: 130*0.5");
                CheckClose(-35, plan.Changes[0].NewPreutterDelta, "cv existing delta relative to auto");
            }
        }

        static void TestCvOverlapOff() {
            var settings = Settings(AdjustMode.CV, ("sh", 0.5));
            settings.ScaleOverlap = false;
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot {
                    NoteIndex = 0, Index = 0, Name = "shao", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 100, AutoPreutter = 100, Overlap = 20, AutoOverlap = 20, NotePosition = 0,
                },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1 && !plan.Changes[0].HasOverlapChange, "cv overlap off: no overlap command");
        }

        static void TestCvClamp() {
            var settings = Settings(AdjustMode.CV, ("sh", 0.1));
            settings.MinLengthMs = 50;
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot {
                    NoteIndex = 0, Index = 0, Name = "shao", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 200, AutoPreutter = 200, Overlap = 20, AutoOverlap = 20, NotePosition = 0,
                },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "cv clamp: one change");
            if (plan.Changes.Count == 1) {
                CheckClose(50, plan.Changes[0].AfterMs, "cv clamp: min length applied");
                Check(plan.Changes[0].Clamped, "cv clamp: flagged");
            }
        }

        static void TestCvvc() {
            var settings = Settings(AdjustMode.CVVC, ("n", 0.5));
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "a", Position = 0, Duration = 480,
                    PositionMs = 0, DurationMs = 500, Preutter = 50, AutoPreutter = 50, NotePosition = 0 },
                new PhonemeSnapshot { NoteIndex = 1, Index = 0, Name = "a n", Position = 360, Duration = 120,
                    PositionMs = 375, DurationMs = 125, Preutter = 20, AutoPreutter = 20, NotePosition = 480, PreviousPosition = 0 },
                new PhonemeSnapshot { NoteIndex = 1, Index = 1, Name = "a", Position = 480, Duration = 480,
                    PositionMs = 500, DurationMs = 500, Preutter = 50, AutoPreutter = 50, NotePosition = 480 },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "cvvc: one change");
            if (plan.Changes.Count == 1) {
                var c = plan.Changes[0];
                Check(c.Kind == ChangeKind.Position, "cvvc: position kind");
                Check(c.SnapshotIndex == 1, "cvvc: previous phoneme changed");
                Check(c.NewOffset == 60, "cvvc: offset moves the pre line right by 60 ticks");
                CheckClose(62.5, c.AfterMs, "cvvc: slot halved");
            }
        }

        static void TestCvvcVcOnPreviousNote() {
            // Some phonemizers attach the VC phoneme to the previous note instead of the next one.
            var settings = Settings(AdjustMode.CVVC, ("n", 0.5));
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "a", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 50, AutoPreutter = 50, NotePosition = 0 },
                new PhonemeSnapshot { NoteIndex = 0, Index = 1, Name = "a n", Position = 360, Duration = 120,
                    DurationMs = 125, Preutter = 20, AutoPreutter = 20, NotePosition = 0, PreviousPosition = 0 },
                new PhonemeSnapshot { NoteIndex = 1, Index = 0, Name = "a", Position = 480, Duration = 480,
                    DurationMs = 500, Preutter = 50, AutoPreutter = 50, NotePosition = 480 },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1 && plan.Changes[0].SnapshotIndex == 1, "cvvc: VC attached to previous note is adjusted");
        }

        static void TestCvvcSkipsPlainCv() {
            // Previous phoneme is a plain CV of the previous note, not a VC carrier.
            var settings = Settings(AdjustMode.CVVC, ("m", 0.5));
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "ma", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 100, AutoPreutter = 100, NotePosition = 0 },
                new PhonemeSnapshot { NoteIndex = 1, Index = 0, Name = "a", Position = 480, Duration = 480,
                    DurationMs = 500, Preutter = 50, AutoPreutter = 50, NotePosition = 480 },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 0, "cvvc: plain CV of previous note is not touched");
        }

        static void TestAi() {
            var settings = Settings(AdjustMode.AI, ("sh", 0.5));
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "sh", Position = 0, Duration = 240,
                    DurationMs = 250, NotePosition = 0 },
                new PhonemeSnapshot { NoteIndex = 0, Index = 1, Name = "ao", Position = 240, Duration = 240,
                    DurationMs = 250, NotePosition = 0, PreviousPosition = 0 },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "ai: one change");
            if (plan.Changes.Count == 1) {
                var c = plan.Changes[0];
                Check(c.Kind == ChangeKind.Position, "ai: position kind");
                Check(c.SnapshotIndex == 0, "ai: consonant phoneme adjusted");
                Check(c.NewOffset == 120, "ai: start moves right 120 ticks");
                CheckClose(125, c.AfterMs, "ai: 250ms halved");
            }
        }

        static void TestAiMultiplePhonemes() {
            var settings = Settings(AdjustMode.AI, ("ch", 0.5), ("ao", 2.0));
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "ch", Position = 0, Duration = 120,
                    DurationMs = 125, NotePosition = 0 },
                new PhonemeSnapshot { NoteIndex = 0, Index = 1, Name = "ao", Position = 120, Duration = 360,
                    DurationMs = 375, NotePosition = 0, PreviousPosition = 0 },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0 }, settings, MsToTicks);
            Check(plan.Changes.Count == 2, "ai: both phonemes adjusted when both match");
            var consonant = plan.Changes.FirstOrDefault(c => c.SnapshotIndex == 0);
            var vowel = plan.Changes.FirstOrDefault(c => c.SnapshotIndex == 1);
            Check(consonant != null && consonant.AfterMs < 125, "ai: ch shortened");
            Check(vowel == null || vowel.AfterMs > 375, "ai: ao lengthened (if not clamped)");
        }

        static void TestScope() {
            var settings = Settings(AdjustMode.CV, ("sh", 0.5));
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "shao", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 100, AutoPreutter = 100, NotePosition = 0 },
                new PhonemeSnapshot { NoteIndex = 1, Index = 0, Name = "shan", Position = 480, Duration = 480,
                    DurationMs = 500, Preutter = 100, AutoPreutter = 100, NotePosition = 480 },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1 && plan.Changes[0].SnapshotIndex == 1, "scope: only targeted note");
        }

        static void TestMasterRatio() {
            var settings = Settings(AdjustMode.CV, ("sh", 0.8));
            settings.MasterRatio = 0.5;
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "shao", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 100, AutoPreutter = 100, NotePosition = 0 },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "master: one change");
            if (plan.Changes.Count == 1) {
                CheckClose(40, plan.Changes[0].AfterMs, "master: 0.8 * 0.5 applied");
            }
        }

        static void TestDisabledRule() {
            var rules = new List<ConsonantRule> { new ConsonantRule("sh", 0.7) { Enabled = false } };
            var settings = new PluginSettings { Mode = AdjustMode.CV, Rules = rules };
            var snaps = new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "shao", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 100, AutoPreutter = 100, NotePosition = 0 },
            };
            var plan = ConsonantPlanner.Plan(snaps, new[] { 0 }, settings, MsToTicks);
            Check(plan.Changes.Count == 0, "disabled rule: no change");
        }

        static List<PhonemeSnapshot> CvvcSnapshots() {
            return new List<PhonemeSnapshot> {
                new PhonemeSnapshot { NoteIndex = 0, Index = 0, Name = "a", Position = 0, Duration = 480,
                    DurationMs = 500, Preutter = 50, AutoPreutter = 50, Overlap = 10, AutoOverlap = 10, NotePosition = 0 },
                new PhonemeSnapshot { NoteIndex = 1, Index = 0, Name = "a n", Position = 360, Duration = 120,
                    PositionMs = 375, DurationMs = 125, Preutter = 25, AutoPreutter = 20, Overlap = 5, AutoOverlap = 5,
                    NotePosition = 480, PreviousPosition = 0 },
                new PhonemeSnapshot { NoteIndex = 1, Index = 1, Name = "a", Position = 480, Duration = 480,
                    PositionMs = 500, DurationMs = 500, Preutter = 50, AutoPreutter = 50, NotePosition = 480 },
            };
        }

        static void TestOverlap() {
            var settings = Settings(AdjustMode.CV, ("n", 0.7));
            settings.P1Target = P1Target.PreviousVc;
            settings.P1Unit = P1Unit.Ratio;
            settings.P1Ratio = 2.0;
            var plan = ConsonantPlanner.PlanOverlap(CvvcSnapshots(), new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "p1 ratio: one change");
            if (plan.Changes.Count == 1) {
                var c = plan.Changes[0];
                Check(c.Kind == ChangeKind.Overlap, "p1 ratio: overlap kind");
                Check(c.SnapshotIndex == 1, "p1 ratio: VC phoneme edited");
                CheckClose(10, c.AfterMs, "p1 ratio: 5ms * 2");
                CheckClose(5, c.NewOverlapDelta, "p1 ratio: delta vs autoOverlap");
                Check(!c.HasOverlapChange == false, "p1 ratio: overlap flag");
            }
        }

        static void TestOverlapAbsoluteAndFraction() {
            var settings = Settings(AdjustMode.CV, ("n", 0.7));
            settings.P1Target = P1Target.PreviousVc;
            settings.P1Unit = P1Unit.Milliseconds;
            settings.P1Milliseconds = 18;
            var plan = ConsonantPlanner.PlanOverlap(CvvcSnapshots(), new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1 && Math.Abs(plan.Changes[0].AfterMs - 18) < 0.01, "p1 absolute: 18ms");

            settings.P1Unit = P1Unit.FractionOfPreutter;
            settings.P1Fraction = 0.4;
            plan = ConsonantPlanner.PlanOverlap(CvvcSnapshots(), new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1 && Math.Abs(plan.Changes[0].AfterMs - 10) < 0.01, "p1 fraction: 25ms * 0.4");
        }

        static void TestOverlapClamp() {
            var settings = Settings(AdjustMode.CV, ("n", 0.7));
            settings.P1Target = P1Target.PreviousVc;
            settings.P1Unit = P1Unit.Milliseconds;
            settings.P1Milliseconds = 200;
            var plan = ConsonantPlanner.PlanOverlap(CvvcSnapshots(), new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "p1 clamp: one change");
            if (plan.Changes.Count == 1) {
                CheckClose(25, plan.Changes[0].AfterMs, "p1 clamp: limited by preutter");
                Check(plan.Changes[0].Clamped, "p1 clamp: flagged");
            }
        }

        static void TestAlignPreviousPosition() {
            var settings = Settings(AdjustMode.CVVC, ("n", 0.7));
            settings.AlignTarget = AlignTarget.PreviousVc;
            settings.AlignAnchor = AlignAnchor.PreviousPosition;
            settings.AlignMode = AlignMode.MovePhoneme;
            var plan = ConsonantPlanner.PlanAlign(CvvcSnapshots(), new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "align: one change");
            if (plan.Changes.Count == 1) {
                var c = plan.Changes[0];
                Check(c.Kind == ChangeKind.Position, "align: position kind");
                Check(c.SnapshotIndex == 1, "align: VC phoneme moved");
                // anchor 0 + preutter 25ms (24 ticks) = 24; offset = 24 - 360
                Check(c.NewOffset == -336, "align: p0 lands on previous position line");
            }
        }

        static void TestAlignPreviousPreLineClamped() {
            var settings = Settings(AdjustMode.CVVC, ("n", 0.7));
            settings.AlignTarget = AlignTarget.PreviousVc;
            settings.AlignAnchor = AlignAnchor.PreviousPreLine;
            settings.AlignMode = AlignMode.MovePhoneme;
            var plan = ConsonantPlanner.PlanAlign(CvvcSnapshots(), new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "align pre line: one change");
            if (plan.Changes.Count == 1) {
                var c = plan.Changes[0];
                Check(c.Clamped, "align pre line: clamped by previous phoneme");
                Check(c.NewOffset == -350, "align pre line: clamps to prev.position + 10");
            }
        }

        static void TestAlignMoveP0Only() {
            var settings = Settings(AdjustMode.CVVC, ("n", 0.7));
            settings.AlignTarget = AlignTarget.PreviousVc;
            settings.AlignAnchor = AlignAnchor.PreviousPosition;
            settings.AlignMode = AlignMode.MoveP0Only;
            settings.MaxLengthMs = 500;
            var plan = ConsonantPlanner.PlanAlign(CvvcSnapshots(), new[] { 0, 1 }, settings, MsToTicks);
            Check(plan.Changes.Count == 1, "align p0 only: one change");
            if (plan.Changes.Count == 1) {
                var c = plan.Changes[0];
                Check(c.Kind == ChangeKind.Preutter, "align p0 only: preutter kind");
                CheckClose(375, c.AfterMs, "align p0 only: preutter = position - anchor");
                CheckClose(355, c.NewPreutterDelta, "align p0 only: delta vs autoPreutter");
            }
        }

        static void TestSilence() {
            var matcher = PhonemeMatcher.Compile(ConsonantPresets.CreateDefaultRules());
            Check(matcher.Match("R") == null, "rest R is not matched");
            Check(matcher.Match("-") == null, "silence - is not matched");
            Check(matcher.Match("sil") == null, "sil is not matched");
            Check(matcher.Match("a R") == null, "rest token inside a VC alias is not matched");
        }
    }
}