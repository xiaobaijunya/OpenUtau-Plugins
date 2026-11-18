using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Plugin.CVVCExpression {

    internal enum EditScope {
        Selected = 0,
        All = 1,
    }

    /// <summary>Per phoneme kind configuration: which expressions to write.</summary>
    internal sealed class KindSetting {
        public CvvcPhonemeKind Kind { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>null = do not touch VEL.</summary>
        public double? Vel { get; set; }
        /// <summary>null = do not touch BRE.</summary>
        public double? Bre { get; set; }

        public KindSetting Clone() {
            return new KindSetting { Kind = Kind, Enabled = Enabled, Vel = Vel, Bre = Bre };
        }
    }

    internal sealed class ExpressionSettings {
        public EditScope Scope { get; set; } = EditScope.Selected;
        public List<KindSetting> Kinds { get; set; } = CreateDefaultKinds();

        public static readonly CvvcPhonemeKind[] EditableKinds = {
            CvvcPhonemeKind.Cv,
            CvvcPhonemeKind.Vc,
            CvvcPhonemeKind.DashCv,
            CvvcPhonemeKind.Vr,
        };

        public static List<KindSetting> CreateDefaultKinds() {
            return EditableKinds.Select(k => new KindSetting { Kind = k, Enabled = true }).ToList();
        }

        public static ExpressionSettings CreateDefault() {
            return new ExpressionSettings();
        }

        public ExpressionSettings Clone() {
            return new ExpressionSettings {
                Scope = Scope,
                Kinds = Kinds.Select(k => k.Clone()).ToList(),
            };
        }

        public KindSetting Get(CvvcPhonemeKind kind) {
            return Kinds.FirstOrDefault(k => k.Kind == kind);
        }

        public void Sanitize() {
            Scope = (EditScope)Math.Clamp((int)Scope, 0, 1);
            Kinds ??= CreateDefaultKinds();
            var result = new List<KindSetting>();
            foreach (var kind in EditableKinds) {
                var existing = Kinds.FirstOrDefault(k => k != null && k.Kind == kind) ?? new KindSetting { Kind = kind };
                existing.Kind = kind;
                existing.Vel = SanitizeValue(existing.Vel);
                existing.Bre = SanitizeValue(existing.Bre);
                result.Add(existing);
            }
            Kinds = result;
        }

        static double? SanitizeValue(double? value) {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) {
                return null;
            }
            return Math.Clamp(value.Value, -1000, 1000);
        }
    }
}
