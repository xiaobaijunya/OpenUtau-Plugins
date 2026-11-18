using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.CVVCExpression {

    /// <summary>Batch editor for VEL / BRE of CV / VC / -CV / V R phonemes.</summary>
    internal sealed class ExpressionWindow : Window {

        static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.FromArgb(72, 128, 128, 128));
        static readonly IBrush CardBackgroundBrush = new SolidColorBrush(Color.FromArgb(10, 128, 128, 128));
        static readonly FontFamily MonoFont = new FontFamily("Consolas,Menlo,DejaVu Sans Mono,monospace");

        sealed class RowControls {
            public CheckBox Enabled;
            public TextBox Vel;
            public TextBox Bre;
        }

        readonly UProject project;
        readonly UVoicePart part;
        readonly List<UNote> selectedNotes;
        readonly DocManager docManager;
        readonly ExpressionSettings settings;
        readonly Dictionary<CvvcPhonemeKind, RowControls> rows = new Dictionary<CvvcPhonemeKind, RowControls>();

        readonly ComboBox scopeBox = new ComboBox { MinWidth = 150 };
        readonly TextBlock scopeHint = new TextBlock { FontSize = 12, Opacity = 0.62 };
        readonly TextBlock previewText = new TextBlock {
            FontFamily = MonoFont,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
        };
        readonly TextBlock statusText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        readonly DispatcherTimer previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        bool suppressControlEvents;

        public ExpressionWindow(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            this.project = project;
            this.part = part;
            this.selectedNotes = selectedNotes ?? new List<UNote>();
            this.docManager = docManager;
            this.settings = PluginSettingsStore.Load();
            this.settings.Sanitize();

            Title = "CVVC 音素参数 · CVVC Expression Editor";
            Width = 940;
            Height = 720;
            MinWidth = 820;
            MinHeight = 600;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            BuildUi();
            UpdateScopeHint();
            previewTimer.Tick += (s, e) => {
                previewTimer.Stop();
                UpdatePreview();
            };
            UpdatePreview();
        }

        #region layout

        void BuildUi() {
            var root = new Grid {
                Margin = new Thickness(16, 12, 16, 12),
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            };

            var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock { Text = "CVVC 音素参数 VEL / BRE", FontSize = 20, FontWeight = FontWeight.Bold });
            header.Children.Add(new TextBlock { Text = DescribeTrack(), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var scopeLine = new StackPanel { Orientation = Orientation.Horizontal };
            scopeLine.Children.Add(new TextBlock { Text = "作用范围", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            scopeBox.ItemsSource = new[] { "仅选中的音符", "全部音符" };
            scopeBox.SelectedIndex = settings.Scope == EditScope.All ? 1 : 0;
            scopeBox.MinHeight = 32;
            scopeBox.FontSize = 13;
            scopeBox.SelectionChanged += (s, e) => {
                if (suppressControlEvents) {
                    return;
                }
                settings.Scope = scopeBox.SelectedIndex == 1 ? EditScope.All : EditScope.Selected;
                UpdateScopeHint();
                SchedulePreview();
            };
            scopeLine.Children.Add(scopeBox);
            var scopeRow = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 8) };
            scopeRow.Children.Add(scopeLine);
            scopeRow.Children.Add(scopeHint);
            Grid.SetRow(scopeRow, 1);
            root.Children.Add(scopeRow);

            var body = new StackPanel { Spacing = 0 };
            body.Children.Add(BuildKindCard());
            body.Children.Add(BuildPreviewCard());
            var scroll = new ScrollViewer {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(2, 0, 8, 0),
            };
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            var footer = BuildFooter();
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
            KeyDown += OnWindowKeyDown;
        }

        string DescribeTrack() {
            try {
                var track = project.tracks[part.trackNo];
                string singer = track.Singer != null && track.Singer.Found ? track.Singer.Name : "(无音源)";
                string phonemizer = string.IsNullOrWhiteSpace(track.PhonemizerTag) ? "-" : track.PhonemizerTag;
                return $"音轨 {part.trackNo + 1} · 音源 {singer} · 音素器 {phonemizer} · 音符 {part.notes.Count} 个 · 已选中 {selectedNotes.Count} 个 · v{PluginInfo.Version}";
            } catch (Exception e) {
                PluginLog.Warn($"Cannot describe track: {e.Message}");
                return $"音符 {part.notes.Count} 个 · 已选中 {selectedNotes.Count} 个";
            }
        }

        Border Card(string title, string hint, Control content) {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 13 });
            if (!string.IsNullOrWhiteSpace(hint)) {
                panel.Children.Add(new TextBlock { Text = hint, FontSize = 12, Opacity = 0.62, TextWrapping = TextWrapping.Wrap });
            }
            panel.Children.Add(content);
            return new Border {
                BorderBrush = CardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Background = CardBackgroundBrush,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = panel,
            };
        }

        Control BuildKindCard() {
            var grid = new Grid {
                ColumnDefinitions = new ColumnDefinitions("80,*,70,150,150"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            };
            AddCell(grid, HeaderText("类型"), 0, 0);
            AddCell(grid, HeaderText("判定规则（已过滤音高后缀）"), 0, 1);
            AddCell(grid, HeaderText("启用"), 0, 2);
            AddCell(grid, HeaderText("VEL"), 0, 3);
            AddCell(grid, HeaderText("BRE"), 0, 4);

            int row = 1;
            foreach (var kind in ExpressionSettings.EditableKinds) {
                var setting = settings.Get(kind) ?? new KindSetting { Kind = kind };
                var controls = BuildRow(kind, setting, row, grid);
                rows[kind] = controls;
                row++;
            }

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(grid);
            content.Children.Add(new TextBlock {
                Text = "VEL / BRE 留空表示不修改。数值会按音轨的表达式范围自动裁剪。",
                FontSize = 12,
                Opacity = 0.62,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });

            return Card("① 分类与数值",
                "音素名先去掉音高 / 颜色后缀（如 a C4、a4、a:2），再按：含 - → -CV；含 R → V R；含空格 → VC；否则 CV。",
                content);
        }

        static TextBlock HeaderText(string text) => new TextBlock {
            Text = text,
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 10, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };

        static void AddCell(Grid grid, Control control, int row, int column) {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        RowControls BuildRow(CvvcPhonemeKind kind, KindSetting setting, int row, Grid grid) {
            var controls = new RowControls();

            var kindText = new TextBlock {
                Text = PhonemeClassifier.Describe(kind),
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
            };
            AddCell(grid, kindText, row, 0);

            var ruleText = new TextBlock {
                Text = PhonemeClassifier.DescribeRule(kind),
                FontSize = 13,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 10, 8),
            };
            AddCell(grid, ruleText, row, 1);

            controls.Enabled = new CheckBox {
                IsChecked = setting.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
            };
            controls.Enabled.Click += (s, e) => {
                setting.Enabled = controls.Enabled.IsChecked == true;
                SchedulePreview();
            };
            AddCell(grid, controls.Enabled, row, 2);

            controls.Vel = BuildValueBox(setting.Vel, "不改");
            controls.Vel.TextChanged += (s, e) => {
                setting.Vel = ParseValue(controls.Vel.Text);
                SchedulePreview();
            };
            controls.Vel.Margin = new Thickness(0, 0, 10, 8);
            AddCell(grid, controls.Vel, row, 3);

            controls.Bre = BuildValueBox(setting.Bre, "不改");
            controls.Bre.TextChanged += (s, e) => {
                setting.Bre = ParseValue(controls.Bre.Text);
                SchedulePreview();
            };
            controls.Bre.Margin = new Thickness(0, 0, 10, 8);
            AddCell(grid, controls.Bre, row, 4);

            return controls;
        }

        static TextBox BuildValueBox(double? value, string watermark) {
            return new TextBox {
                Text = value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty,
                MinHeight = 32,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Watermark = watermark,
            };
        }

        static double? ParseValue(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return null;
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) {
                return Math.Clamp(value, -1000, 1000);
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) {
                return Math.Clamp(value, -1000, 1000);
            }
            return null;
        }

        Control BuildPreviewCard() {
            return Card("② 预览", "应用后会写入音素级表达式，音符本身的 VEL / BRE 不受影响。", new ScrollViewer {
                Content = previewText,
                MaxHeight = 220,
                MinHeight = 80,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
        }

        Control BuildFooter() {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetColumn(statusText, 0);
            grid.Children.Add(statusText);

            var cancel = new Button { Content = "取消", MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (s, e) => Close();
            Grid.SetColumn(cancel, 1);
            grid.Children.Add(cancel);

            var apply = new Button { Content = "应用", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
            apply.Click += (s, e) => Apply();
            Grid.SetColumn(apply, 2);
            grid.Children.Add(apply);
            return grid;
        }

        #endregion

        #region behaviour

        void OnWindowKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                Close();
                e.Handled = true;
            } else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
                Apply();
                e.Handled = true;
            }
        }

        void UpdateScopeHint() {
            if (selectedNotes.Count == 0) {
                scopeHint.Text = "当前没有选中音符，将处理整个声部的全部音符。";
                if (scopeBox.SelectedIndex == 0) {
                    suppressControlEvents = true;
                    scopeBox.SelectedIndex = 1;
                    settings.Scope = EditScope.All;
                    suppressControlEvents = false;
                }
                scopeBox.IsEnabled = false;
            } else {
                scopeHint.Text = $"共 {part.notes.Count} 个音符，已选中 {selectedNotes.Count} 个。";
                scopeBox.IsEnabled = true;
            }
        }

        void SchedulePreview() {
            previewTimer.Stop();
            previewTimer.Start();
        }

        void SetStatus(string message, bool isError = false) {
            statusText.Text = message;
            statusText.Foreground = isError ? Brushes.OrangeRed : null;
        }

        void UpdatePreview() {
            try {
                var plan = CvvcExpressionAdjuster.Plan(project, part, selectedNotes, settings.Clone());
                var builder = new StringBuilder();
                builder.Append("扫描到：");
                foreach (var kind in ExpressionSettings.EditableKinds) {
                    builder.Append($"{PhonemeClassifier.Describe(kind)} {plan.CountOf(kind)} 个 · ");
                }
                builder.Append($"静音 {plan.CountOf(CvvcPhonemeKind.Silence)} 个");
                builder.AppendLine();
                builder.AppendLine($"将要写入 {plan.Changes.Count} 个表达式，无需修改 {plan.Skipped} 个。");
                int shown = 0;
                foreach (var change in plan.Changes) {
                    if (shown >= 80) {
                        builder.AppendLine($"… 还有 {plan.Changes.Count - shown} 项");
                        break;
                    }
                    string oldText = change.OldValue.HasValue ? change.OldValue.Value.ToString("0.##", CultureInfo.InvariantCulture) : "默认";
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-10} {1,-4} {2,-4} {3,6} → {4,-6}",
                        Trim(change.PhonemeName, 10), PhonemeClassifier.Describe(change.Kind),
                        change.Abbr.ToUpperInvariant(), oldText,
                        change.NewValue.ToString("0.##", CultureInfo.InvariantCulture)));
                    shown++;
                }
                foreach (var warning in plan.Warnings) {
                    builder.AppendLine("  ! " + warning);
                }
                if (plan.Changes.Count == 0) {
                    builder.AppendLine("（没有要修改的内容：检查每一类的「启用」勾选，以及 VEL / BRE 是否填了数值）");
                }
                previewText.Text = builder.ToString().TrimEnd();
            } catch (Exception e) {
                PluginLog.Error(e, "Preview failed.");
                previewText.Text = "预览失败：" + e.Message;
            }
        }

        static string Trim(string text, int max) {
            if (string.IsNullOrEmpty(text)) {
                return string.Empty;
            }
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        void Apply() {
            try {
                var snapshot = settings.Clone();
                snapshot.Sanitize();
                var plan = CvvcExpressionAdjuster.Plan(project, part, selectedNotes, snapshot);
                if (!plan.HasChanges) {
                    SetStatus("没有需要修改的表达式。", true);
                    UpdatePreview();
                    return;
                }
                int count = CvvcExpressionAdjuster.Apply(project, part, plan, docManager);
                PluginSettingsStore.Save(snapshot);
                PluginLog.Info($"Apply: changed={count}");
                SetStatus($"已写入 {count} 个表达式。");
                Close();
            } catch (Exception e) {
                PluginLog.Error(e, "Apply failed.");
                SetStatus("应用失败：" + e.Message, true);
            }
        }

        #endregion
    }
}
