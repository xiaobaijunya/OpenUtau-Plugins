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

    /// <summary>Configuration window of the consonant length adjuster (three pages + rules).</summary>
    internal sealed class ConsonantLengthWindow : Window {

        static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.FromArgb(72, 128, 128, 128));
        static readonly IBrush CardBackgroundBrush = new SolidColorBrush(Color.FromArgb(10, 128, 128, 128));
        static readonly FontFamily MonoFont = new FontFamily("Consolas,Menlo,DejaVu Sans Mono,monospace");

        const int TabDuration = 0;
        const int TabOverlap = 1;
        const int TabAlign = 2;
        const int TabRules = 3;

        readonly UProject project;
        readonly UVoicePart part;
        readonly List<UNote> selectedNotes;
        readonly DocManager docManager;
        readonly PluginSettings settings;

        // shared
        readonly TabControl tabs = new TabControl();
        readonly ComboBox scopeBox = new ComboBox { MinWidth = 150 };
        readonly TextBlock scopeHint = new TextBlock { FontSize = 12, Opacity = 0.62, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock statusText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        readonly Button applyButton = new Button { Content = "应用", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        readonly DispatcherTimer previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        // duration page
        readonly RadioButton cvRadio = new RadioButton { Content = "CV 模式", GroupName = "cl-mode" };
        readonly RadioButton cvvcRadio = new RadioButton { Content = "CVVC 模式", GroupName = "cl-mode" };
        readonly RadioButton aiRadio = new RadioButton { Content = "AI 模式", GroupName = "cl-mode" };
        readonly TextBlock modeDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 12 };
        readonly NumericUpDown masterBox = new NumericUpDown();
        readonly NumericUpDown minBox = new NumericUpDown();
        readonly NumericUpDown maxBox = new NumericUpDown();
        readonly CheckBox overlapCheck = new CheckBox { Content = "同时缩放 overlap（p1 点）" };
        readonly TextBlock durationPreview = PreviewText();

        // p1 page
        readonly ComboBox p1TargetBox = new ComboBox { MinWidth = 230 };
        readonly ComboBox p1UnitBox = new ComboBox { MinWidth = 170 };
        readonly NumericUpDown p1ValueBox = new NumericUpDown { Width = 150, MinHeight = 32, FontSize = 13 };
        readonly TextBlock p1Hint = new TextBlock { FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock overlapPreview = PreviewText();

        // align page
        readonly ComboBox alignTargetBox = new ComboBox { MinWidth = 260 };
        readonly ComboBox alignAnchorBox = new ComboBox { MinWidth = 260 };
        readonly ComboBox alignModeBox = new ComboBox { MinWidth = 260 };
        readonly TextBlock alignHint = new TextBlock { FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock alignPreview = PreviewText();

        // rules page
        readonly ComboBox presetBox = new ComboBox { MinWidth = 240 };
        readonly WrapPanel rulesPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock rulesHint = new TextBlock { FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };

        ConsonantRule focusAfterRebuild;
        bool suppressControlEvents;

        public ConsonantLengthWindow(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            this.project = project;
            this.part = part;
            this.selectedNotes = selectedNotes ?? new List<UNote>();
            this.docManager = docManager;
            this.settings = PluginSettingsStore.Load();
            this.settings.Sanitize();

            Title = "辅音时长调整 · Consonant Length Adjuster";
            Width = 960;
            Height = 780;
            MinWidth = 820;
            MinHeight = 620;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            BuildUi();
            UpdateModeDescription();
            UpdateRulesPanel();
            UpdateScopeHint();
            UpdateP1Hint();
            UpdateAlignHint();
            previewTimer.Tick += (s, e) => {
                previewTimer.Stop();
                UpdatePreview();
            };
            UpdatePreview();
        }

        static TextBlock PreviewText() => new TextBlock {
            FontFamily = MonoFont,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
        };

        #region layout

        void BuildUi() {
            var root = new Grid {
                Margin = new Thickness(16, 12, 16, 12),
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            };

            var header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            tabs.Items.Add(new TabItem { Header = "辅音时长", Content = BuildDurationTab() });
            tabs.Items.Add(new TabItem { Header = "p1 · overlap", Content = BuildOverlapTab() });
            tabs.Items.Add(new TabItem { Header = "CVVC · p0 对齐", Content = BuildAlignTab() });
            tabs.Items.Add(new TabItem { Header = "规则", Content = BuildRulesTab() });
            tabs.SelectedIndex = TabDuration;
            tabs.SelectionChanged += (s, e) => {
                UpdateApplyState();
                UpdatePreview();
            };
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            var footer = BuildFooter();
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            KeyDown += OnWindowKeyDown;
        }

        Control BuildHeader() {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
            var title = new StackPanel { Spacing = 2 };
            title.Children.Add(new TextBlock { Text = "辅音时长调整", FontSize = 20, FontWeight = FontWeight.Bold });
            title.Children.Add(new TextBlock {
                Text = DescribeTrack(),
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(title, 0);
            grid.Children.Add(title);

            var scope = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            scope.Children.Add(new TextBlock { Text = "作用范围", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            scopeBox.ItemsSource = new[] { "仅选中的音符", "全部音符" };
            scopeBox.SelectedIndex = settings.Scope == NoteScope.All ? 1 : 0;
            scopeBox.SelectionChanged += (s, e) => {
                if (suppressControlEvents) {
                    return;
                }
                settings.Scope = scopeBox.SelectedIndex == 1 ? NoteScope.All : NoteScope.Selected;
                UpdateScopeHint();
                SchedulePreview();
            };
            scope.Children.Add(scopeBox);
            Grid.SetColumn(scope, 1);
            grid.Children.Add(scope);

            var wrapper = new StackPanel { Spacing = 2 };
            wrapper.Children.Add(grid);
            wrapper.Children.Add(scopeHint);
            return wrapper;
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

        Control Page(params Control[] children) {
            var panel = new StackPanel { Spacing = 0, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var child in children) {
                panel.Children.Add(child);
            }
            return new ScrollViewer {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(2, 0, 8, 0),
            };
        }

        Control BuildDurationTab() {
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

            var modeContent = new StackPanel { Spacing = 6 };
            modeContent.Children.Add(modes);
            modeContent.Children.Add(modeDescription);

            var masterRow = new StackPanel { Orientation = Orientation.Horizontal };
            masterRow.Children.Add(new TextBlock { Text = "整体倍率", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            masterBox.Minimum = 0.1m;
            masterBox.Maximum = 3.0m;
            masterBox.Increment = 0.05m;
            masterBox.FormatString = "0.00";
            masterBox.Value = (decimal)settings.MasterRatio;
            masterBox.Width = 150;
            masterBox.MinHeight = 32;
            masterBox.FontSize = 13;
            masterBox.ValueChanged += (s, e) => {
                if (suppressControlEvents || !masterBox.Value.HasValue) {
                    return;
                }
                settings.MasterRatio = (double)masterBox.Value.Value;
                SchedulePreview();
            };
            masterRow.Children.Add(masterBox);

            var clampRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            clampRow.Children.Add(new TextBlock { Text = "最短(ms)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            minBox.Minimum = 0m;
            minBox.Maximum = 500m;
            minBox.Increment = 5m;
            minBox.FormatString = "0.#";
            minBox.Value = (decimal)settings.MinLengthMs;
            minBox.Width = 140;
            minBox.MinHeight = 32;
            minBox.FontSize = 13;
            minBox.ValueChanged += (s, e) => {
                if (suppressControlEvents || !minBox.Value.HasValue) {
                    return;
                }
                settings.MinLengthMs = (double)minBox.Value.Value;
                SchedulePreview();
            };
            clampRow.Children.Add(minBox);
            clampRow.Children.Add(new TextBlock { Text = "最长(ms)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) });
            maxBox.Minimum = 10m;
            maxBox.Maximum = 2000m;
            maxBox.Increment = 10m;
            maxBox.FormatString = "0.#";
            maxBox.Value = (decimal)settings.MaxLengthMs;
            maxBox.Width = 140;
            maxBox.MinHeight = 32;
            maxBox.FontSize = 13;
            maxBox.ValueChanged += (s, e) => {
                if (suppressControlEvents || !maxBox.Value.HasValue) {
                    return;
                }
                settings.MaxLengthMs = (double)maxBox.Value.Value;
                SchedulePreview();
            };
            clampRow.Children.Add(maxBox);

            overlapCheck.IsChecked = settings.ScaleOverlap;
            overlapCheck.VerticalAlignment = VerticalAlignment.Center;
            overlapCheck.Margin = new Thickness(20, 0, 0, 0);
            overlapCheck.Click += (s, e) => {
                settings.ScaleOverlap = overlapCheck.IsChecked == true;
                SchedulePreview();
            };
            clampRow.Children.Add(overlapCheck);

            var paramContent = new StackPanel { Spacing = 0 };
            paramContent.Children.Add(masterRow);
            paramContent.Children.Add(clampRow);

            var previewCard = Card("③ 预览", "改动会以「规则倍率 × 整体倍率」计算，先预览再应用。", new ScrollViewer {
                Content = durationPreview,
                MaxHeight = 260,
                MinHeight = 80,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            });

            return Page(
                Card("① 调整模式", null, modeContent),
                Card("② 参数", "整体倍率会与每条规则的倍率相乘；最短/最长用于防止过度调整。", paramContent),
                previewCard);
        }

        Control BuildOverlapTab() {
            p1TargetBox.ItemsSource = new[] {
                "音符起始辅音（CV 音源）",
                "前一个 VC 音素（CVVC 音源）",
                "全部匹配音素（AI 音源）",
            };
            p1TargetBox.SelectedIndex = (int)settings.P1Target;
            p1TargetBox.SelectionChanged += (s, e) => {
                if (suppressControlEvents) {
                    return;
                }
                settings.P1Target = (P1Target)Math.Clamp(p1TargetBox.SelectedIndex, 0, 2);
                UpdateP1Hint();
                SchedulePreview();
            };

            p1UnitBox.ItemsSource = new[] { "倍率 ×（p1 = 当前 p1 × 倍率）", "绝对值（直接设为 ms）", "占 p0 的比例（p1 = p0 × 比例）" };
            p1UnitBox.SelectedIndex = (int)settings.P1Unit;
            p1UnitBox.SelectionChanged += (s, e) => {
                if (suppressControlEvents) {
                    return;
                }
                settings.P1Unit = (P1Unit)Math.Clamp(p1UnitBox.SelectedIndex, 0, 2);
                ConfigureP1ValueBox();
                UpdateP1Hint();
                SchedulePreview();
            };

            ConfigureP1ValueBox();
            p1ValueBox.ValueChanged += (s, e) => {
                if (suppressControlEvents || !p1ValueBox.Value.HasValue) {
                    return;
                }
                double value = (double)p1ValueBox.Value.Value;
                switch (settings.P1Unit) {
                    case P1Unit.Ratio:
                        settings.P1Ratio = value;
                        break;
                    case P1Unit.Milliseconds:
                        settings.P1Milliseconds = value;
                        break;
                    default:
                        settings.P1Fraction = value;
                        break;
                }
                UpdateP1Hint();
                SchedulePreview();
            };

            var targetRow = new StackPanel { Orientation = Orientation.Horizontal };
            targetRow.Children.Add(new TextBlock { Text = "作用对象", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            targetRow.Children.Add(p1TargetBox);

            var unitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            unitRow.Children.Add(new TextBlock { Text = "设置方式", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            unitRow.Children.Add(p1UnitBox);
            unitRow.Children.Add(new TextBlock { Text = "数值", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
            unitRow.Children.Add(p1ValueBox);

            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(targetRow);
            content.Children.Add(unitRow);
            content.Children.Add(p1Hint);

            return Page(
                Card("① 作用对象与取值",
                    "p1 是 envelope 的第二个点（overlap）。这里只改 p1，不改 p0；按「规则」页的前缀匹配辅音，结果限制在 0 ~ p0 之间。",
                    content),
                Card("② 预览", null, new ScrollViewer {
                    Content = overlapPreview,
                    MaxHeight = 260,
                    MinHeight = 90,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                }));
        }

        void ConfigureP1ValueBox() {
            suppressControlEvents = true;
            switch (settings.P1Unit) {
                case P1Unit.Ratio:
                    p1ValueBox.Minimum = 0.1m;
                    p1ValueBox.Maximum = 3.0m;
                    p1ValueBox.Increment = 0.05m;
                    p1ValueBox.FormatString = "0.00";
                    p1ValueBox.Value = (decimal)Math.Clamp(settings.P1Ratio, 0.1, 3.0);
                    break;
                case P1Unit.Milliseconds:
                    p1ValueBox.Minimum = -200m;
                    p1ValueBox.Maximum = 400m;
                    p1ValueBox.Increment = 5m;
                    p1ValueBox.FormatString = "0.#";
                    p1ValueBox.Value = (decimal)Math.Clamp(settings.P1Milliseconds, -200, 400);
                    break;
                default:
                    p1ValueBox.Minimum = -1m;
                    p1ValueBox.Maximum = 1m;
                    p1ValueBox.Increment = 0.05m;
                    p1ValueBox.FormatString = "0.00";
                    p1ValueBox.Value = (decimal)Math.Clamp(settings.P1Fraction, -1, 1);
                    break;
            }
            suppressControlEvents = false;
        }

        void UpdateP1Hint() {
            string target = p1TargetBox.SelectedIndex switch {
                1 => "前一个 VC 音素（形如 a n，紧邻当前音符的元音音素）",
                2 => "当前音符上所有匹配规则的音素",
                _ => "音符起始辅音（音符起点处的音素）",
            };
            string unit = settings.P1Unit switch {
                P1Unit.Ratio => $"p1 × {settings.P1Ratio:0.00}",
                P1Unit.Milliseconds => $"p1 = {settings.P1Milliseconds:0.#} ms",
                _ => $"p1 = p0 × {settings.P1Fraction:0.00}",
            };
            p1Hint.Text = $"当前：{target}，{unit}。结果会被限制在 0 ~ p0 之间。";
        }

        Control BuildAlignTab() {
            alignTargetBox.ItemsSource = new[] {
                "前一个 VC 音素（a n，CVVC 的辅音）",
                "当前音符的起始辅音（CV）",
            };
            alignTargetBox.SelectedIndex = (int)settings.AlignTarget;
            alignTargetBox.SelectionChanged += (s, e) => {
                if (suppressControlEvents) {
                    return;
                }
                settings.AlignTarget = (AlignTarget)Math.Clamp(alignTargetBox.SelectedIndex, 0, 1);
                UpdateAlignHint();
                SchedulePreview();
            };

            alignAnchorBox.ItemsSource = new[] {
                "前一个音素的 pre 线（prev.position − prev.preutter）",
                "前一个音素的位置线（prev.position）",
                "前一个音素的终点（prev.End）",
            };
            alignAnchorBox.SelectedIndex = (int)settings.AlignAnchor;
            alignAnchorBox.SelectionChanged += (s, e) => {
                if (suppressControlEvents) {
                    return;
                }
                settings.AlignAnchor = (AlignAnchor)Math.Clamp(alignAnchorBox.SelectedIndex, 0, 2);
                UpdateAlignHint();
                SchedulePreview();
            };

            alignModeBox.ItemsSource = new[] {
                "移动整个音素（保持辅音长度，p0 落到锚点）",
                "只移动 p0（改变 preutter，音素位置不变）",
            };
            alignModeBox.SelectedIndex = (int)settings.AlignMode;
            alignModeBox.SelectionChanged += (s, e) => {
                if (suppressControlEvents) {
                    return;
                }
                settings.AlignMode = (AlignMode)Math.Clamp(alignModeBox.SelectedIndex, 0, 1);
                UpdateAlignHint();
                SchedulePreview();
            };

            var targetRow = new StackPanel { Orientation = Orientation.Horizontal };
            targetRow.Children.Add(new TextBlock { Text = "目标音素", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            targetRow.Children.Add(alignTargetBox);

            var anchorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            anchorRow.Children.Add(new TextBlock { Text = "对齐到", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            anchorRow.Children.Add(alignAnchorBox);

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            modeRow.Children.Add(new TextBlock { Text = "移动方式", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            modeRow.Children.Add(alignModeBox);

            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(targetRow);
            content.Children.Add(anchorRow);
            content.Children.Add(modeRow);
            content.Children.Add(alignHint);

            return Page(
                Card("① CVVC 辅音对齐",
                    "只处理 CVVC 的 VC 音素（形如 a n，或附着在音符上的前一个音素）。用于修正在 OpenUtau 里 CVVC 辅音位置不对的情况。",
                    content),
                Card("② 预览", "「p0」= 音素起点 − preutter。对齐后 p0 会落在所选的位置上。", new ScrollViewer {
                    Content = alignPreview,
                    MaxHeight = 300,
                    MinHeight = 90,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                }));
        }

        void UpdateAlignHint() {
            string target = settings.AlignTarget == AlignTarget.NoteOnset
                ? "当前音符的起始辅音（CV）"
                : "前一个 VC 音素（a n）";
            string anchor = alignAnchorBox.SelectedIndex switch {
                1 => "前一个音素的位置线（prev.position）",
                2 => "前一个音素的终点（prev.End）",
                _ => "前一个音素的 pre 线（prev.position − prev.preutter）",
            };
            string mode = settings.AlignMode == AlignMode.MoveP0Only
                ? "只移动 p0，改变 preutter（音素位置不变）"
                : "移动整个音素，preutter 不变";
            alignHint.Text = $"当前：把「{target}」的 p0 移动到{anchor}，{mode}。";
        }

        Control BuildRulesTab() {
            presetBox.ItemsSource = ConsonantPresets.All.Select(p => new PresetItem(p)).ToList();
            presetBox.SelectedIndex = 0;
            presetBox.MinHeight = 32;
            presetBox.FontSize = 13;

            var mergeButton = new Button { Content = "合并载入", MinHeight = 32, Margin = new Thickness(6, 0, 0, 0) };
            mergeButton.Click += (s, e) => LoadPreset(replace: false);
            var replaceButton = new Button { Content = "替换规则", MinHeight = 32, Margin = new Thickness(6, 0, 0, 0) };
            replaceButton.Click += (s, e) => LoadPreset(replace: true);
            var addButton = new Button { Content = "＋ 添加规则", MinHeight = 32 };
            addButton.Click += (s, e) => {
                var rule = new ConsonantRule(string.Empty, 0.7, ConsonantPresets.CustomGroup);
                settings.Rules.Add(rule);
                focusAfterRebuild = rule;
                UpdateRulesPanel();
                SchedulePreview();
            };
            var clearButton = new Button { Content = "清空", MinHeight = 32, Margin = new Thickness(6, 0, 0, 0) };
            clearButton.Click += (s, e) => {
                settings.Rules.Clear();
                UpdateRulesPanel();
                SchedulePreview();
            };
            var defaultButton = new Button { Content = "恢复默认", MinHeight = 32, Margin = new Thickness(6, 0, 0, 0) };
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

            var scroll = new ScrollViewer {
                Content = rulesPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(0, 0, 8, 0),
            };

            // A real grid: the rule list stretches to the tab height instead of scrolling inside a card.
            var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(2, 10, 2, 0) };
            var title = new TextBlock { Text = "音素匹配规则", FontWeight = FontWeight.Bold, FontSize = 13 };
            var hint = new TextBlock {
                Text = "按音素名开头匹配，最长前缀优先：sh 可匹配 shao / shi / shu；一条规则可写多个前缀，用逗号分隔，如 k,か,き,く,け,こ。语音时长页与 p1 页共用这里的规则。",
                FontSize = 12,
                Opacity = 0.62,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8),
            };
            Grid.SetRow(title, 0);
            Grid.SetRow(hint, 1);
            var actionRow = new StackPanel { Orientation = Orientation.Horizontal };
            actionRow.Children.Add(addButton);
            actionRow.Children.Add(clearButton);
            actionRow.Children.Add(defaultButton);

            var toolbar = new StackPanel { Spacing = 8 };
            toolbar.Children.Add(presetRow);
            toolbar.Children.Add(actionRow);

            Grid.SetRow(toolbar, 2);
            Grid.SetRow(scroll, 3);
            Grid.SetRow(rulesHint, 4);

            panel.Children.Add(title);
            panel.Children.Add(hint);
            panel.Children.Add(toolbar);
            panel.Children.Add(scroll);
            panel.Children.Add(rulesHint);
            rulesHint.Margin = new Thickness(0, 6, 0, 0);

            return new Border {
                BorderBrush = CardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Background = CardBackgroundBrush,
                Padding = new Thickness(12, 10, 12, 10),
                Child = panel,
            };
        }

        Control BuildFooter() {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetColumn(statusText, 0);
            grid.Children.Add(statusText);

            var cancel = new Button { Content = "取消", MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (s, e) => Close();
            Grid.SetColumn(cancel, 1);
            grid.Children.Add(cancel);

            applyButton.Click += (s, e) => Apply();
            Grid.SetColumn(applyButton, 2);
            grid.Children.Add(applyButton);
            return grid;
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

        void UpdateApplyState() {
            bool editable = tabs.SelectedIndex == TabDuration || tabs.SelectedIndex == TabOverlap || tabs.SelectedIndex == TabAlign;
            applyButton.IsEnabled = editable;
            applyButton.Content = tabs.SelectedIndex switch {
                TabDuration => "应用辅音时长",
                TabOverlap => "应用 p1",
                TabAlign => "应用对齐",
                _ => "应用",
            };
        }

        void LoadPreset(bool replace) {
            if (!(presetBox.SelectedItem is PresetItem item)) {
                return;
            }
            var source = ConsonantPresets.Find(item.Preset.Id);
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
                var row = BuildRuleRow(rule);
                rulesPanel.Children.Add(row.Item1);
                if (rule == focusAfterRebuild) {
                    focusTarget = row.Item2;
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
            UpdateRulesHint();
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
                Width = 190,
                MinHeight = 32,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Watermark = "前缀，如 sh",
            };
            prefixBox.TextChanged += (s, e) => {
                rule.Prefix = prefixBox.Text ?? string.Empty;
                SchedulePreview();
            };

            var ratioBox = new TextBox {
                Text = rule.Ratio.ToString("0.00", CultureInfo.InvariantCulture),
                Width = 70,
                MinHeight = 32,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            ratioBox.TextChanged += (s, e) => {
                if (TryParseDouble(ratioBox.Text, out double value)) {
                    rule.Ratio = Math.Clamp(value, 0.1, 3.0);
                    SchedulePreview();
                }
            };

            var delete = new Button {
                Content = "✕",
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                FontSize = 12,
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

        static bool TryParseDouble(string text, out double value) {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) {
                return true;
            }
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
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
                switch (tabs.SelectedIndex) {
                    case TabDuration:
                        durationPreview.Text = FormatPlan(
                            ConsonantLengthAdjuster.Plan(project, part, selectedNotes, snapshot),
                            p => p.Kind == ChangeKind.Preutter ? "preutter" : "length",
                            p => p.BeforeMs,
                            p => p.AfterMs);
                        break;
                    case TabOverlap:
                        overlapPreview.Text = FormatPlan(
                            ConsonantLengthAdjuster.PlanOverlap(project, part, selectedNotes, snapshot),
                            p => "p1",
                            p => p.BeforeMs,
                            p => p.AfterMs);
                        break;
                    case TabAlign:
                        alignPreview.Text = FormatPlan(
                            ConsonantLengthAdjuster.PlanAlign(project, part, selectedNotes, snapshot),
                            p => p.Kind == ChangeKind.Position ? "start" : "preutter",
                            p => p.BeforeMs,
                            p => p.AfterMs);
                        break;
                }
                UpdateApplyState();
            } catch (Exception e) {
                PluginLog.Error(e, "Preview failed.");
                durationPreview.Text = "预览失败：" + e.Message;
            }
        }

        static string FormatPlan(PlanResult plan, Func<PlannedChange, string> kind, Func<PlannedChange, double> before, Func<PlannedChange, double> after) {
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
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10} {1,-9} {2,7:0.0} → {3,7:0.0} ms   ×{4:0.00}  [{5}]",
                    Trim(change.PhonemeName, 10), kind(change), before(change), after(change), change.Ratio, change.MatchedPrefix));
                shown++;
            }
            foreach (var warning in plan.Warnings) {
                builder.AppendLine("  ! " + warning);
            }
            string text = builder.ToString().TrimEnd();
            if (plan.Changes.Count == 0) {
                text += Environment.NewLine + "（没有可调整的音素：请检查页面选择、规则前缀，或音素是否已有 preutter 数据）";
            }
            return text;
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
                PlanResult plan;
                switch (tabs.SelectedIndex) {
                    case TabDuration:
                        plan = ConsonantLengthAdjuster.Plan(project, part, selectedNotes, snapshot);
                        break;
                    case TabOverlap:
                        plan = ConsonantLengthAdjuster.PlanOverlap(project, part, selectedNotes, snapshot);
                        break;
                    case TabAlign:
                        plan = ConsonantLengthAdjuster.PlanAlign(project, part, selectedNotes, snapshot);
                        break;
                    default:
                        SetStatus("「规则」页只用于设置匹配规则，请切换到其他页面应用。", true);
                        return;
                }
                if (!plan.HasChanges) {
                    SetStatus("没有需要调整的音素。", true);
                    UpdatePreview();
                    return;
                }
                int count = ConsonantLengthAdjuster.Apply(part, plan, docManager);
                PluginSettingsStore.Save(snapshot);
                PluginLog.Info($"Apply tab={tabs.SelectedIndex} mode={snapshot.Mode} changed={count}");
                SetStatus($"已调整 {count} 个音素。");
                Close();
            } catch (Exception e) {
                PluginLog.Error(e, "Apply failed.");
                SetStatus("应用失败：" + e.Message, true);
            }
        }

        #endregion
    }
}