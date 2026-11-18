using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.CVVCExpression.Tests {

    /// <summary>Dependency free tests for the CVVC phoneme classifier and expression planner.</summary>
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

        static void CheckKind(string name, CvvcPhonemeKind expected) {
            var actual = PhonemeClassifier.Classify(name);
            Check(actual == expected, $"classify \"{name}\" -> {expected} (actual {actual})");
        }

        static Dictionary<string, ExpressionRange> Ranges() {
            return new Dictionary<string, ExpressionRange> {
                { ExpressionPlanner.VelAbbr, new ExpressionRange { Min = 0, Max = 200, Available = true } },
                { ExpressionPlanner.BreAbbr, new ExpressionRange { Min = 0, Max = 100, Available = true } },
            };
        }

        static PhonemeExpressionSnapshot Ph(int noteIndex, string name, float? vel = 100, float? bre = 0) {
            return new PhonemeExpressionSnapshot {
                NoteIndex = noteIndex,
                PhonemeIndex = 0,
                Name = name,
                Kind = PhonemeClassifier.Classify(name),
                Vel = vel,
                Bre = bre,
            };
        }

        static void Main() {
            TestNormalizer();
            TestClassification();
            TestPitchSuffixFiltering();
            TestPlannerBasics();
            TestPlannerScope();
            TestRangeClamp();
            TestUnavailableAbbr();
            TestDisabledKind();

            Console.WriteLine($"{checks - failures}/{checks} checks passed.");
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        static void TestNormalizer() {
            Check(PhonemeNameNormalizer.Normalize("a C4") == "a", "normalize a C4");
            Check(PhonemeNameNormalizer.Normalize("a#4") == "a", "normalize a#4");
            Check(PhonemeNameNormalizer.Normalize("a_C4") == "a", "normalize a_C4");
            Check(PhonemeNameNormalizer.Normalize("a:2") == "a", "normalize color suffix");
            Check(PhonemeNameNormalizer.Normalize("a n C4") == "a n", "normalize a n C4");
            Check(PhonemeNameNormalizer.Normalize("a R C4") == "a R", "normalize a R C4");
            Check(PhonemeNameNormalizer.Normalize("shao") == "shao", "normalize plain name");
        }

        static void TestClassification() {
            CheckKind("a", CvvcPhonemeKind.Cv);
            CheckKind("shao", CvvcPhonemeKind.Cv);
            CheckKind("a n", CvvcPhonemeKind.Vc);
            CheckKind("n a", CvvcPhonemeKind.Vc);
            CheckKind("- a", CvvcPhonemeKind.DashCv);
            CheckKind("-a", CvvcPhonemeKind.DashCv);
            CheckKind("a R", CvvcPhonemeKind.Vr);
            CheckKind("a n R", CvvcPhonemeKind.Vr);
            CheckKind("R", CvvcPhonemeKind.Silence);
            CheckKind("-", CvvcPhonemeKind.Silence);
            CheckKind("sil", CvvcPhonemeKind.Silence);
        }

        static void TestPitchSuffixFiltering() {
            // A pitch suffix with a space must not turn a CV into a VC.
            CheckKind("a C4", CvvcPhonemeKind.Cv);
            CheckKind("a#4", CvvcPhonemeKind.Cv);
            CheckKind("a n C4", CvvcPhonemeKind.Vc);
            CheckKind("a R C4", CvvcPhonemeKind.Vr);
            CheckKind("- a C4", CvvcPhonemeKind.DashCv);
            CheckKind("a:2", CvvcPhonemeKind.Cv);
        }

        static void TestPlannerBasics() {
            var settings = ExpressionSettings.CreateDefault();
            settings.Get(CvvcPhonemeKind.Cv).Vel = 80;
            settings.Get(CvvcPhonemeKind.Vc).Bre = 20;
            var phonemes = new List<PhonemeExpressionSnapshot> {
                Ph(0, "a"), Ph(0, "a n"), Ph(0, "- a"), Ph(0, "a R"), Ph(0, "R"),
            };
            var plan = ExpressionPlanner.Plan(phonemes, new[] { 0 }, settings, Ranges());
            Check(plan.CountByKind[(int)CvvcPhonemeKind.Cv] == 1, "count CV");
            Check(plan.CountByKind[(int)CvvcPhonemeKind.Vc] == 1, "count VC");
            Check(plan.CountByKind[(int)CvvcPhonemeKind.DashCv] == 1, "count -CV");
            Check(plan.CountByKind[(int)CvvcPhonemeKind.Vr] == 1, "count V R");
            Check(plan.CountByKind[(int)CvvcPhonemeKind.Silence] == 1, "count silence");
            Check(plan.Changes.Count == 2, $"planner: 2 changes (actual {plan.Changes.Count})");
            Check(plan.Changes.Any(c => c.Kind == CvvcPhonemeKind.Cv && c.Abbr == "vel" && Math.Abs(c.NewValue - 80) < 0.01), "planner: CV vel=80");
            Check(plan.Changes.Any(c => c.Kind == CvvcPhonemeKind.Vc && c.Abbr == "bre" && Math.Abs(c.NewValue - 20) < 0.01), "planner: VC bre=20");
        }

        static void TestPlannerScope() {
            var settings = ExpressionSettings.CreateDefault();
            settings.Get(CvvcPhonemeKind.Cv).Vel = 80;
            var phonemes = new List<PhonemeExpressionSnapshot> { Ph(0, "a"), Ph(1, "a") };
            var plan = ExpressionPlanner.Plan(phonemes, new[] { 0 }, settings, Ranges());
            Check(plan.Changes.Count == 1 && plan.Changes[0].SnapshotIndex == 0, "planner: scope limits notes");
        }

        static void TestRangeClamp() {
            var settings = ExpressionSettings.CreateDefault();
            settings.Get(CvvcPhonemeKind.Cv).Vel = 500;
            var plan = ExpressionPlanner.Plan(new List<PhonemeExpressionSnapshot> { Ph(0, "a") }, new[] { 0 }, settings, Ranges());
            Check(plan.Changes.Count == 1 && Math.Abs(plan.Changes[0].NewValue - 200) < 0.01, "planner: clamp to max range");
        }

        static void TestUnavailableAbbr() {
            var settings = ExpressionSettings.CreateDefault();
            settings.Get(CvvcPhonemeKind.Cv).Bre = 20;
            var ranges = new Dictionary<string, ExpressionRange> {
                { ExpressionPlanner.VelAbbr, new ExpressionRange { Min = 0, Max = 200, Available = true } },
                { ExpressionPlanner.BreAbbr, new ExpressionRange { Min = 0, Max = 100, Available = false } },
            };
            var plan = ExpressionPlanner.Plan(new List<PhonemeExpressionSnapshot> { Ph(0, "a") }, new[] { 0 }, settings, ranges);
            Check(plan.Changes.Count == 0, "planner: unavailable BRE skipped");
            Check(plan.Warnings.Count == 1, "planner: warns about missing BRE");
        }

        static void TestDisabledKind() {
            var settings = ExpressionSettings.CreateDefault();
            settings.Get(CvvcPhonemeKind.Cv).Vel = 80;
            settings.Get(CvvcPhonemeKind.Cv).Enabled = false;
            var plan = ExpressionPlanner.Plan(new List<PhonemeExpressionSnapshot> { Ph(0, "a") }, new[] { 0 }, settings, Ranges());
            Check(plan.Changes.Count == 0, "planner: disabled kind ignored");
        }
    }
}
