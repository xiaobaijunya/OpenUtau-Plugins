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

namespace OpenUtau.Plugin.ConsonantLength {

    /// <summary>Configuration window of the consonant length adjuster.</summary>
    internal sealed class ConsonantLengthWindow : Window {

        static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.FromArgb(72, 128, 128, 128));
        static readonly IBrush CardBackgroundBrush = new SolidColorBrush(Color.FromArgb(10, 128, 128, 128));

        readonly UProject project;
        readonly UVoicePart part;
        readonly List<UNote> selectedNotes;
        readonly DocManager docManager;
        readonly PluginSettings settings;

        readonly RadioButton cvRadio = new RadioButton { Content = "CV 模式", GroupName = "cl-mode" };
        readonly RadioButton cvvcRadio = new RadioButton { Content = "CVVC 模式", GroupName = "cl-mode" };
        readonly RadioButton aiRadio = new RadioButton { Content = "AI 模式", GroupName = "cl-mode" };
        readonly TextBlock modeDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 12 };

        readonly ComboBox scopeBox = new ComboBox { MinWidth = 150 };
        readonly NumericUpDown masterBox = new NumericUpDown();
        readonly NumericUpDown minBox = new NumericUpDown();
        readonly NumericUpDown maxBox = new NumericUpDown();
        readonly CheckBox overlapCheck = new CheckBox { Content = "同时缩放 overlap（p1 点）" };
        readonly TextBlock scopeHint = new TextBlock { FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };

        readonly ComboBox presetBox = new ComboBox { MinWidth = 230 };
        readonly WrapPanel rulesPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock rulesHint = new TextBlock { FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };

        readonly TextBlock previewText = new TextBlock {
            FontFamily = new FontFamily("Consolas,Menlo,DejaVu Sans Mono,monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
        };
        readonly TextBlock statusText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

        readonly DispatcherTimer previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        ConsonantRule focusAfterRebuild;

        public ConsonantLengthWindow(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            this.project = project;
            this.part = part;
            this.selectedNotes = selectedNotes ?? new List<UNote>();
            this.docManager = docManager;
            this.settings = PluginSettingsStore.Load();
            this.settings.Sanitize();

            Title = "辅音时长调整 · Consonant Length Adjuster";
            Width = 880;
            Height = 730;
            MinWidth = 720;
            MinHeight = 560;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            BuildUi();
            UpdateModeDescription();
            UpdateRulesPanel();
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
                Margin = new Thickness(18, 14, 18, 14),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            };

            var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock {
                Text = "辅音时长调整",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
            });
            header.Children.Add(new TextBlock {
                Text = DescribeTrack(),
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var modeCard = BuildModeCard();
            Grid.SetRow(modeCard, 1);
            root.Children.Add(modeCard);

            var paramCard = BuildParamCard();
            Grid.SetRow(paramCard, 2);
            root.Children.Add(paramCard);

            var ruleCard = BuildRuleCard();
            Grid.SetRow(ruleCard, 3);
            root.Children.Add(ruleCard);

            var previewCard = BuildPreviewCard();
            Grid.SetRow(previewCard, 4);
            root.Children.Add(previewCard);

            var buttons = BuildButtonRow();
            Grid.SetRow(buttons, 5);
            root.Children.Add(buttons);

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

        Control BuildModeCard() {
            cvRadio.IsChecked = settings.Mode == AdjustMode.CV;
            cvvcRadio.IsChecked = settings.Mode == AdjustMode.CVVC;
            aiRadio.IsChecked = settings.Mode == AdjustMode.AI;
            cvRadio.Click += (s, e) => { settings.Mode = AdjustMode.CV; UpdateModeDescription(); SchedulePreview(); };
            cvvcRadio.Click += (s, e) => { settings.Mode = AdjustMode.CVVC; UpdateModeDescription(); SchedulePreview(); };
            aiRadio.Click += (s, e) => { settings.Mode = AdjustMode.AI; UpdateModeDescription(); SchedulePreview(); };

            var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
            modes.Children.Add(cvRadio);
            modes.Children.Add(cvvcRadio);
            modes.Children.Add(aiRadio);

            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(modes);
            content.Children.Add(modeDescription);
            return Card("① 调整模式", null, content);
        }

        Control BuildParamCard() {
            var scopeLabel = new TextBlock { Text = "作用范围", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            scopeBox.ItemsSource = new[] { "仅选中的音符", "全部音符" };
            scopeBox.SelectedIndex = settings.Scope == NoteScope.All ? 1 : 0;
            scopeBox.SelectionChanged += (s, e) => {
                settings.Scope = scopeBox.SelectedIndex == 1 ? NoteScope.All : NoteScope.Selected;
                UpdateScopeHint();
                SchedulePreview();
            };
            scopeBox.Margin = new Thickness(0, 0, 18, 0);

            masterBox.Minimum = 0.1m;
            masterBox.Maximum = 3.0m;
            masterBox.Increment = 0.05m;
            masterBox.FormatString = "0.00";
            masterBox.Value = (decimal)settings.MasterRatio;
            masterBox.Width = 110;
            masterBox.ValueChanged += (s, e) => {
                if (masterBox.Value.HasValue) {
                    settings.MasterRatio = (double)masterBox.Value.Value;
                    SchedulePreview();
                }
            };
            masterBox.Margin = new Thickness(0, 0, 18, 0);

            minBox.Minimum = 0m;
            minBox.Maximum = 500m;
            minBox.Increment = 5m;
            minBox.FormatString = "0.#";
            minBox.Value = (decimal)settings.MinLengthMs;
            minBox.Width = 92;
            minBox.ValueChanged += (s, e) => {
                if (minBox.Value.HasValue) {
                    settings.MinLengthMs = (double)minBox.Value.Value;
                    SchedulePreview();
                }
            };
            minBox.Margin = new Thickness(0, 0, 18, 0);

            maxBox.Minimum = 10m;
            maxBox.Maximum = 2000m;
            maxBox.Increment = 10m;
            maxBox.FormatString = "0.#";
            maxBox.Value = (decimal)settings.MaxLengthMs;
            maxBox.Width = 92;
            maxBox.ValueChanged += (s, e) => {
                if (maxBox.Value.HasValue) {
                    settings.MaxLengthMs = (double)maxBox.Value.Value;
                    SchedulePreview();
                }
            };
            maxBox.Margin = new Thickness(0, 0, 18, 0);

            overlapCheck.IsChecked = settings.ScaleOverlap;
            overlapCheck.VerticalAlignment = VerticalAlignment.Center;
            overlapCheck.Click += (s, e) => {
                settings.ScaleOverlap = overlapCheck.IsChecked == true;
                SchedulePreview();
            };

            var masterLabel = new TextBlock { Text = "整体倍率", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var minLabel = new TextBlock { Text = "最短(ms)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var maxLabel = new TextBlock { Text = "最长(ms)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            row1.Children.Add(minLabel);
            row1.Children.Add(minBox);
            row1.Children.Add(maxLabel);
            row1.Children.Add(maxBox);
            row1.Children.Add(overlapCheck);

            var content = new StackPanel { Spacing = 8 };
            var firstRow = new StackPanel { Orientation = Orientation.Horizontal };
            firstRow.Children.Add(scopeLabel);
            firstRow.Children.Add(scopeBox);
            firstRow.Children.Add(masterLabel);
            firstRow.Children.Add(masterBox);
            content.Children.Add(firstRow);
            content.Children.Add(row1);
            content.Children.Add(scopeHint);
            return Card("② 参数", "整体倍率会与下面每条规则的倍率相乘；最短/最长用于防止过度调整。", content);
        }

        Control BuildRuleCard() {
            presetBox.ItemsSource = ConsonantPresets.All
                .Select(p => new PresetItem(p))
                .ToList();
            presetBox.SelectedIndex = 0;

            var mergeButton = new Button { Content = "合并载入", Margin = new Thickness(6, 0, 0, 0) };
            mergeButton.Click += (s, e) => LoadPreset(replace: false);
            var replaceButton = new Button { Content = "替换规则", Margin = new Thickness(6, 0, 0, 0) };
            replaceButton.Click += (s, e) => LoadPreset(replace: true);
            var addButton = new Button { Content = "＋ 添加规则", Margin = new Thickness(12, 0, 0, 0) };
            addButton.Click += (s, e) => {
                var rule = new ConsonantRule(string.Empty, 0.7, ConsonantPresets.CustomGroup);
                settings.Rules.Add(rule);
                focusAfterRebuild = rule;
                UpdateRulesPanel();
                SchedulePreview();
            };
            var clearButton = new Button { Content = "清空", Margin = new Thickness(6, 0, 0, 0) };
            clearButton.Click += (s, e) => {
                settings.Rules.Clear();
                UpdateRulesPanel();
                SchedulePreview();
            };
            var defaultButton = new Button { Content = "恢复默认", Margin = new Thickness(6, 0, 0, 0) };
            defaultButton.Click += (s, e) => {
                foreach (var preset in ConsonantPresets.All) {
                    ConsonantPresets.Merge(settings.Rules, preset.Rules);
                }
                UpdateRulesPanel();
                SchedulePreview();
            };

            var presetRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            presetRow.Children.Add(presetBox);
            presetRow.Children.Add(mergeButton);
            presetRow.Children.Add(replaceButton);

            var buttonRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            buttonRow.Children.Add(addButton);
            buttonRow.Children.Add(clearButton);
            buttonRow.Children.Add(defaultButton);

            var scroll = new ScrollViewer {
                Content = rulesPanel,
                MaxHeight = 210,
                MinHeight = 90,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 2, 0, 0),
            };

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(presetRow);
            content.Children.Add(buttonRow);
            content.Children.Add(scroll);
            content.Children.Add(rulesHint);

            return Card("③ 音素匹配规则",
                "按音素名开头匹配，最长前缀优先：sh 可匹配 shao / shi / shu；一条规则可写多个前缀，用逗号分隔，如 k,か,き,く,け,こ。",
                content);
        }

        Control BuildPreviewCard() {
            var scroll = new ScrollViewer {
                Content = previewText,
                MaxHeight = 120,
                MinHeight = 70,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(scroll);
            return Card("④ 预览", "改动会以「倍率 × 全局倍率」计算，先预览再应用。", content);
        }

        Control BuildButtonRow() {
            var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetColumn(statusText, 0);
            buttons.Children.Add(statusText);

            var cancel = new Button { Content = "取消", MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (s, e) => Close();
            Grid.SetColumn(cancel, 1);
            buttons.Children.Add(cancel);

            var apply = new Button { Content = "应用", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
            apply.Click += (s, e) => Apply();
            Grid.SetColumn(apply, 2);
            buttons.Children.Add(apply);

            return buttons;
        }

        #endregion

        #region behaviour

        sealed class PresetItem {
            public RulePreset Preset { get; }
            public PresetItem(RulePreset preset) {
                Preset = preset;
            }
            public override string ToString() => Preset.Name;
        }

        void OnWindowKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                Close();
                e.Handled = true;
            } else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
                Apply();
                e.Handled = true;
            }
        }

        void LoadPreset(bool replace) {
            if (!(presetBox.SelectedItem is PresetItem item)) {
                return;
            }
            string preset = item.Preset.Id;
            var source = ConsonantPresets.Find(preset);
            if (source == null) {
                return;
            }
            if (replace) {
                settings.Rules = source.Rules.Select(r => r.Clone()).ToList();
            } else {
                ConsonantPresets.Merge(settings.Rules, source.Rules);
            }
            UpdateRulesPanel();
            SchedulePreview();
            SetStatus(replace ? $"已替换为预设：{source.Name}" : $"已合并预设：{source.Name}");
        }

        void UpdateModeDescription() {
            switch (settings.Mode) {
                case AdjustMode.CV:
                    modeDescription.Text =
                        "CV：只移动当前音素的 p0 / p1 两个点（preutter / overlap）。" +
                        "辅音位于 pre 线以前、即 p2（0 点）之前的那段时间，缩放 preutter 就相当于把辅音拉长或缩短。";
                    break;
                case AdjustMode.CVVC:
                    modeDescription.Text =
                        "CVVC：移动前一个音素（VC，例如 a n）的 pre 线 / 起点，让该辅音占用更多或更少的时间。" +
                        "只处理与当前音符第一个音素紧邻、且形如「a n」或附着在当前音符上的音素。";
                    break;
                default:
                    modeDescription.Text =
                        "AI：音素列通常已经是 sh / ao / ch / e 这样拆分好的形式，只需按名字匹配到辅音音素，" +
                        "直接移动它的起点来改变它的长度（终点不变）。";
                    break;
            }
        }

        void UpdateScopeHint() {
            if (selectedNotes.Count == 0) {
                scopeHint.Text = "当前没有选中音符，将处理整个声部的全部音符。";
                if (scopeBox.SelectedIndex == 0) {
                    scopeBox.SelectedIndex = 1;
                }
                scopeBox.IsEnabled = false;
            } else {
                scopeHint.Text = $"共 {part.notes.Count} 个音符，已选中 {selectedNotes.Count} 个。";
                scopeBox.IsEnabled = true;
            }
        }

        void UpdateRulesPanel() {
            rulesPanel.Children.Clear();
            TextBox focusTarget = null;
            foreach (var rule in settings.Rules) {
                var textBox = BuildRuleRow(rule);
                rulesPanel.Children.Add(textBox.Item1);
                if (rule == focusAfterRebuild) {
                    focusTarget = textBox.Item2;
                }
            }
            focusAfterRebuild = null;
            if (settings.Rules.Count == 0) {
                rulesPanel.Children.Add(new TextBlock {
                    Text = "（还没有规则，点「恢复默认」载入汉语/日语预设）",
                    Opacity = 0.6,
                    Margin = new Thickness(2, 4, 0, 0),
                });
            }
            rulesHint.Text = $"共 {settings.Rules.Count} 条规则，启用 {settings.Rules.Count(r => r.Enabled)} 条。";
            if (focusTarget != null) {
                Dispatcher.UIThread.Post(() => {
                    focusTarget.Focus();
                    focusTarget.SelectAll();
                }, DispatcherPriority.Background);
            }
        }

        Tuple<Control, TextBox> BuildRuleRow(ConsonantRule rule) {
            var check = new CheckBox {
                IsChecked = rule.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
                Padding = new Thickness(0),
            };
            check.Click += (s, e) => {
                rule.Enabled = check.IsChecked == true;
                UpdateRulesHint();
                SchedulePreview();
            };

            var prefixBox = new TextBox {
                Text = rule.Prefix ?? string.Empty,
                Width = 150,
                MinHeight = 28,
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                Watermark = "前缀，如 sh",
            };
            prefixBox.TextChanged += (s, e) => {
                rule.Prefix = prefixBox.Text ?? string.Empty;
                SchedulePreview();
            };

            var ratioBox = new TextBox {
                Text = rule.Ratio.ToString("0.00", CultureInfo.InvariantCulture),
                Width = 52,
                MinHeight = 28,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            ratioBox.TextChanged += (s, e) => {
                if (TryParseRatio(ratioBox.Text, out double value)) {
                    rule.Ratio = value;
                    SchedulePreview();
                }
            };

            var delete = new Button {
                Content = "✕",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                FontSize = 11,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(delete, "删除这条规则");
            delete.Click += (s, e) => {
                settings.Rules.Remove(rule);
                UpdateRulesPanel();
                SchedulePreview();
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(check);
            row.Children.Add(prefixBox);
            row.Children.Add(ratioBox);
            row.Children.Add(delete);

            var border = new Border {
                BorderBrush = CardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 4, 2),
                Margin = new Thickness(0, 0, 6, 6),
                Child = row,
            };
            return Tuple.Create<Control, TextBox>(border, prefixBox);
        }

        void UpdateRulesHint() {
            rulesHint.Text = $"共 {settings.Rules.Count} 条规则，启用 {settings.Rules.Count(r => r.Enabled)} 条。";
        }

        static bool TryParseRatio(string text, out double value) {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) {
                value = Math.Clamp(value, 0.1, 3.0);
                return true;
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) {
                value = Math.Clamp(value, 0.1, 3.0);
                return true;
            }
            return false;
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
                var snapshot = settings.Clone();
                snapshot.Sanitize();
                var plan = ConsonantLengthAdjuster.Plan(project, part, selectedNotes, snapshot);
                var builder = new StringBuilder();
                builder.Append($"匹配 {plan.Matched} 个音素 · 将要调整 {plan.Changes.Count} 个 · 未匹配（元音/静音等）{plan.Unmatched} 个 · 无需调整 {plan.Unchanged} 个");
                if (plan.NotesWithoutPhonemes > 0) {
                    builder.Append($" · {plan.NotesWithoutPhonemes} 个音符还没有音素");
                }
                builder.AppendLine();
                int shown = 0;
                foreach (var change in plan.Changes) {
                    if (shown >= 80) {
                        builder.AppendLine($"… 还有 {plan.Changes.Count - shown} 项");
                        break;
                    }
                    string kind = change.Kind == ChangeKind.Preutter ? "preutter" : "length";
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-10} {1,-9} {2,7:0.0} → {3,7:0.0} ms   ×{4:0.00}  [{5}]",
                        Trim(change.PhonemeName, 10), kind, change.BeforeMs, change.AfterMs, change.Ratio, change.MatchedPrefix));
                    shown++;
                }
                foreach (var warning in plan.Warnings) {
                    builder.AppendLine("  ! " + warning);
                }
                previewText.Text = builder.ToString().TrimEnd();
                if (plan.Changes.Count == 0) {
                    previewText.Text += Environment.NewLine + "（没有可调整的音素：请检查模式、规则前缀，或音素是否已有 preutter 数据）";
                }
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
                var plan = ConsonantLengthAdjuster.Plan(project, part, selectedNotes, snapshot);
                if (!plan.HasChanges) {
                    SetStatus("没有需要调整的音素。", true);
                    UpdatePreview();
                    return;
                }
                int count = ConsonantLengthAdjuster.Apply(part, plan, docManager);
                PluginSettingsStore.Save(snapshot);
                SetStatus($"已调整 {count} 个音素。");
                PluginLog.Info($"Apply: mode={snapshot.Mode}, scope={snapshot.Scope}, changed={count}");
                Close();
            } catch (Exception e) {
                PluginLog.Error(e, "Apply failed.");
                SetStatus("应用失败：" + e.Message, true);
            }
        }

        #endregion
    }
}
