using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace QuotaBar
{
    public class SettingsWindow : Window
    {
        private readonly QuotaApp _app;
        private readonly StackPanel _rowsPanel = new StackPanel();
        private readonly List<ProviderRow> _rows = new List<ProviderRow>();
        private bool _suppress;

        private readonly ComboBox _interval = new ComboBox();
        private readonly CheckBox _topmost = new CheckBox();
        private readonly CheckBox _pinned = new CheckBox();
        private readonly CheckBox _autoStart = new CheckBox();
        private readonly ComboBox _newKind = new ComboBox();

        public SettingsWindow(QuotaApp app)
        {
            _app = app;

            Title = "QuotaBar 设置";
            Width = 660;
            Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF9));

            DockPanel root = new DockPanel();
            root.LastChildFill = true;

            FrameworkElement header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            FrameworkElement footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.Padding = new Thickness(14, 6, 14, 14);
            scroll.Content = _rowsPanel;
            root.Children.Add(scroll);

            Content = root;
            LoadFromState();
        }

        // ---------- 顶部：全局设置 ----------

        private FrameworkElement BuildHeader()
        {
            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(14, 14, 14, 6);

            TextBlock title = new TextBlock();
            title.Text = "AI 额度 · 设置";
            title.FontSize = 15;
            title.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(title);

            TextBlock subtitle = new TextBlock();
            subtitle.Text = "支持 OpenRouter、DeepSeek、Kimi、硅基流动、Claude Code 订阅、OpenAI 组织用量，也可以自定义接口。";
            subtitle.FontSize = 11;
            subtitle.Margin = new Thickness(0, 3, 0, 10);
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x6E));
            panel.Children.Add(subtitle);

            Border box = new Border();
            box.Background = Brushes.White;
            box.CornerRadius = new CornerRadius(10);
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE8));
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(12);

            StackPanel inner = new StackPanel();

            Grid line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock label = FieldLabel("自动刷新");
            Grid.SetColumn(label, 0);
            line.Children.Add(label);
            _interval.Width = 150;
            _interval.HorizontalAlignment = HorizontalAlignment.Left;
            _interval.Items.Add("1 分钟");
            _interval.Items.Add("2 分钟");
            _interval.Items.Add("5 分钟");
            _interval.Items.Add("10 分钟");
            _interval.Items.Add("30 分钟");
            _interval.Items.Add("60 分钟");
            Grid.SetColumn(_interval, 1);
            line.Children.Add(_interval);
            inner.Children.Add(line);

            TextBlock intervalHint = new TextBlock();
            intervalHint.Text = "1 分钟适合盯 Claude Code 的 5 小时窗口；但余额类接口有频率限制，" +
                                "长期挂 1 分钟可能被限流，平时建议用 5–10 分钟。";
            intervalHint.FontSize = 11;
            intervalHint.Margin = new Thickness(96, 4, 0, 0);
            intervalHint.TextWrapping = TextWrapping.Wrap;
            intervalHint.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90));
            inner.Children.Add(intervalHint);

            _topmost.Content = "总在最前（不会被其它窗口盖住）";
            _topmost.Margin = new Thickness(96, 8, 0, 0);
            _topmost.FontSize = 12;
            inner.Children.Add(_topmost);

            _pinned.Content = "贴到桌面（实验：挂到桌面图层，会被窗口盖住）";
            _pinned.Margin = new Thickness(96, 6, 0, 0);
            _pinned.FontSize = 12;
            inner.Children.Add(_pinned);

            _autoStart.Content = "开机自动启动";
            _autoStart.Margin = new Thickness(96, 6, 0, 0);
            _autoStart.FontSize = 12;
            inner.Children.Add(_autoStart);

            StackPanel pathLine = new StackPanel();
            pathLine.Orientation = Orientation.Horizontal;
            pathLine.Margin = new Thickness(96, 10, 0, 0);

            TextBlock path = new TextBlock();
            path.Text = "配置： " + Store.ConfigPath;
            path.FontSize = 10;
            path.VerticalAlignment = VerticalAlignment.Center;
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90));
            pathLine.Children.Add(path);

            Button openFolder = new Button();
            openFolder.Content = "打开文件夹";
            openFolder.Margin = new Thickness(8, 0, 0, 0);
            openFolder.Padding = new Thickness(8, 2, 8, 2);
            openFolder.FontSize = 11;
            openFolder.Click += delegate { OpenDataFolder(); };
            pathLine.Children.Add(openFolder);
            inner.Children.Add(pathLine);

            box.Child = inner;
            panel.Children.Add(box);

            TextBlock listTitle = new TextBlock();
            listTitle.Text = "供应商";
            listTitle.FontSize = 13;
            listTitle.FontWeight = FontWeights.SemiBold;
            listTitle.Margin = new Thickness(2, 16, 0, 0);
            panel.Children.Add(listTitle);

            return panel;
        }

        // ---------- 底部：添加 / 保存 ----------

        private FrameworkElement BuildFooter()
        {
            Border bar = new Border();
            bar.Background = Brushes.White;
            bar.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE8));
            bar.BorderThickness = new Thickness(0, 1, 0, 0);
            bar.Padding = new Thickness(14, 10, 14, 10);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;

            foreach (ProviderKind kind in ProviderCatalog.All)
            {
                _newKind.Items.Add(ProviderCatalog.DisplayName(kind));
            }
            _newKind.SelectedIndex = 0;
            _newKind.Width = 180;
            _newKind.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(_newKind);

            Button add = new Button();
            add.Content = "添加供应商";
            add.Margin = new Thickness(8, 0, 0, 0);
            add.Padding = new Thickness(12, 4, 12, 4);
            add.Click += delegate
            {
                CommitAll();
                ProviderKind kind = ProviderCatalog.All[_newKind.SelectedIndex < 0 ? 0 : _newKind.SelectedIndex];
                ProviderConfig config = new ProviderConfig();
                config.Kind = kind;
                config.Name = ProviderCatalog.DisplayName(kind);
                _app.State.Providers.Add(config);
                Rebuild();
            };
            left.Children.Add(add);

            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;

            Button cancel = new Button();
            cancel.Content = "关闭";
            cancel.Padding = new Thickness(16, 4, 16, 4);
            cancel.Click += delegate { Close(); };
            right.Children.Add(cancel);

            Button save = new Button();
            save.Content = "保存并刷新";
            save.Margin = new Thickness(8, 0, 0, 0);
            save.Padding = new Thickness(16, 4, 16, 4);
            save.IsDefault = true;
            save.Click += delegate { SaveAndApply(); };
            right.Children.Add(save);

            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            bar.Child = grid;
            return bar;
        }

        // ---------- 行 ----------

        private static TextBlock FieldLabel(string text)
        {
            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontSize = 11;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x6E));
            return label;
        }

        private static Grid LabeledRow(string text, UIElement control, double top)
        {
            Grid grid = new Grid();
            grid.Margin = new Thickness(0, top, 0, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock label = FieldLabel(text);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        private void LoadFromState()
        {
            _suppress = true;
            _interval.SelectedIndex = IntervalIndex(_app.State.RefreshMinutes);
            _topmost.IsChecked = _app.State.Topmost;
            _pinned.IsChecked = _app.State.PinnedToDesktop;
            _autoStart.IsChecked = IsAutoStartEnabled();
            _suppress = false;
            Rebuild();
        }

        private static int IntervalIndex(int minutes)
        {
            if (minutes <= 1) return 0;
            if (minutes <= 2) return 1;
            if (minutes <= 5) return 2;
            if (minutes <= 10) return 3;
            if (minutes <= 30) return 4;
            return 5;
        }

        private static int IntervalMinutes(int index)
        {
            switch (index)
            {
                case 0: return 1;
                case 1: return 2;
                case 2: return 5;
                case 4: return 30;
                case 5: return 60;
                default: return 10;
            }
        }

        private void Rebuild()
        {
            _suppress = true;
            _rowsPanel.Children.Clear();
            _rows.Clear();

            if (_app.State.Providers.Count == 0)
            {
                TextBlock empty = new TextBlock();
                empty.Text = "还没有供应商。用下面的「添加供应商」加一个吧。";
                empty.FontSize = 12;
                empty.Margin = new Thickness(4, 20, 0, 0);
                empty.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90));
                _rowsPanel.Children.Add(empty);
            }

            foreach (ProviderConfig config in _app.State.Providers)
            {
                ProviderRow row = new ProviderRow(this, config);
                _rows.Add(row);
                _rowsPanel.Children.Add(row.Build());
            }
            _suppress = false;
        }

        internal bool Suppressed { get { return _suppress; } }

        private void CommitAll()
        {
            foreach (ProviderRow row in _rows) row.Commit();
        }

        private void SaveAndApply()
        {
            CommitAll();
            _app.State.RefreshMinutes = IntervalMinutes(_interval.SelectedIndex);
            _app.State.Topmost = _topmost.IsChecked == true;
            _app.State.PinnedToDesktop = _pinned.IsChecked == true;
            SetAutoStart(_autoStart.IsChecked == true);
            _app.SaveStateOnly();
            _app.Widget.ReloadState();
        }

        // ---------- 开机启动 ----------

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "QuotaBar";

        private static string ExecutablePath()
        {
            try
            {
                return Assembly.GetExecutingAssembly().Location;
            }
            catch (Exception)
            {
                return Process.GetCurrentProcess().MainModule.FileName;
            }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    return key.GetValue(RunValueName) != null;
                }
            }
            catch (Exception) { return false; }
        }

        private static void SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(RunValueName, "\"" + ExecutablePath() + "\"");
                    else key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception) { }
        }

        private static void OpenDataFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Store.DataDirectory);
                Process.Start("explorer.exe", "\"" + Store.DataDirectory + "\"");
            }
            catch (Exception) { }
        }

        // ---------- 单个供应商的编辑行 ----------

        private class ProviderRow
        {
            private readonly SettingsWindow _owner;
            private readonly ProviderConfig _config;

            private readonly ComboBox _kind = new ComboBox();
            private readonly TextBox _name = new TextBox();
            private readonly PasswordBox _credential = new PasswordBox();
            private readonly CheckBox _enabled = new CheckBox();
            private readonly Button _remove = new Button();

            private readonly TextBox _endpoint = new TextBox();
            private readonly TextBox _headers = new TextBox();
            private readonly TextBox _remainingPath = new TextBox();
            private readonly TextBox _usedPath = new TextBox();
            private readonly TextBox _limitPath = new TextBox();
            private readonly TextBox _currency = new TextBox();
            private readonly TextBox _label = new TextBox();
            private readonly ComboBox _unit = new ComboBox();

            private readonly StackPanel _credentialBlock = new StackPanel();
            private readonly StackPanel _customBlock = new StackPanel();
            private readonly TextBox _baseUrl = new TextBox();
            private readonly StackPanel _baseUrlBlock = new StackPanel();

            public ProviderRow(SettingsWindow owner, ProviderConfig config)
            {
                _owner = owner;
                _config = config;
            }

            public FrameworkElement Build()
            {
                Border card = new Border();
                card.Background = Brushes.White;
                card.CornerRadius = new CornerRadius(10);
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE8));
                card.BorderThickness = new Thickness(1);
                card.Padding = new Thickness(12);
                card.Margin = new Thickness(0, 0, 0, 10);

                StackPanel body = new StackPanel();

                // 第一行：类型 + 名称 + 启用 + 删除
                Grid first = new Grid();
                first.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                first.ColumnDefinitions.Add(new ColumnDefinition());
                first.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                first.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                for (int i = 0; i < ProviderCatalog.All.Length; i++)
                {
                    _kind.Items.Add(ProviderCatalog.DisplayName(ProviderCatalog.All[i]));
                    if (ProviderCatalog.All[i] == _config.Kind) _kind.SelectedIndex = i;
                }
                if (_kind.SelectedIndex < 0) _kind.SelectedIndex = 0;
                Grid.SetColumn(_kind, 0);
                first.Children.Add(_kind);

                _name.Text = _config.Name;
                _name.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(_name, 1);
                first.Children.Add(_name);

                _enabled.Content = "启用";
                _enabled.IsChecked = _config.Enabled;
                _enabled.FontSize = 12;
                _enabled.VerticalAlignment = VerticalAlignment.Center;
                _enabled.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(_enabled, 2);
                first.Children.Add(_enabled);

                _remove.Content = "删除";
                _remove.Margin = new Thickness(10, 0, 0, 0);
                _remove.Padding = new Thickness(10, 3, 10, 3);
                _remove.Click += delegate
                {
                    _owner.CommitAll();
                    _owner._app.State.Providers.Remove(_config);
                    _owner.Rebuild();
                };
                Grid.SetColumn(_remove, 3);
                first.Children.Add(_remove);

                body.Children.Add(first);

                // 凭证
                _credential.Password = _config.Credential;
                _credentialBlock.Children.Add(LabeledRow(
                    ProviderCatalog.CredentialLabel(_config.Kind), _credential, 10));

                TextBlock hint = new TextBlock();
                hint.Text = ProviderCatalog.Blurb(_config.Kind);
                hint.FontSize = 11;
                hint.Margin = new Thickness(96, 4, 0, 0);
                hint.TextWrapping = TextWrapping.Wrap;
                hint.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90));
                _credentialBlock.Children.Add(hint);

                string helpUrl = ProviderCatalog.HelpUrl(_config.Kind);
                if (helpUrl.Length > 0)
                {
                    StackPanel helpLine = new StackPanel();
                    helpLine.Orientation = Orientation.Horizontal;
                    helpLine.Margin = new Thickness(96, 4, 0, 0);

                    Button help = new Button();
                    help.Content = "在哪里找到这个 Key？";
                    help.FontSize = 11;
                    help.Padding = new Thickness(8, 2, 8, 2);
                    string captured = helpUrl;
                    help.Click += delegate { OpenUrl(captured); };
                    helpLine.Children.Add(help);
                    _credentialBlock.Children.Add(helpLine);
                }
                body.Children.Add(_credentialBlock);

                // 只需要站点根地址的供应商（如 SoloAPI）
                _baseUrl.Text = _config.Option("baseURL");
                _baseUrlBlock.Children.Add(LabeledRow("接口地址", _baseUrl, 10));
                TextBlock baseHint = new TextBlock();
                baseHint.Text = "留空就用默认地址 https://api.soloapi.cc（域名换了再改这里，"
                              + "填了 /v1 或结尾斜杠会自动纠正）。";
                baseHint.FontSize = 11;
                baseHint.Margin = new Thickness(96, 4, 0, 0);
                baseHint.TextWrapping = TextWrapping.Wrap;
                baseHint.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90));
                _baseUrlBlock.Children.Add(baseHint);
                body.Children.Add(_baseUrlBlock);

                // 自定义接口字段
                _endpoint.Text = _config.Option("endpoint");
                _headers.Text = _config.Option("headersJSON");
                _remainingPath.Text = _config.Option("remainingPath");
                _usedPath.Text = _config.Option("usedPath");
                _limitPath.Text = _config.Option("limitPath");
                _currency.Text = _config.Option("currency");
                _label.Text = _config.Option("label");

                _customBlock.Children.Add(LabeledRow("接口地址", _endpoint, 10));
                _customBlock.Children.Add(LabeledRow("请求头 JSON", _headers, 8));
                _customBlock.Children.Add(LabeledRow("剩余字段路径", _remainingPath, 8));
                _customBlock.Children.Add(LabeledRow("已用字段路径", _usedPath, 8));
                _customBlock.Children.Add(LabeledRow("上限字段路径", _limitPath, 8));

                QuotaUnit[] units = new QuotaUnit[]
                {
                    QuotaUnit.Currency, QuotaUnit.Percent, QuotaUnit.Tokens,
                    QuotaUnit.Requests, QuotaUnit.Credits
                };
                string[] unitNames = new string[] { "金额", "百分比", "Token", "次数", "积分" };
                string currentUnit = _config.Option("unit");
                for (int i = 0; i < units.Length; i++)
                {
                    _unit.Items.Add(unitNames[i]);
                    if (string.Equals(units[i].ToString(), currentUnit, StringComparison.OrdinalIgnoreCase))
                        _unit.SelectedIndex = i;
                }
                if (_unit.SelectedIndex < 0) _unit.SelectedIndex = 0;
                _customBlock.Children.Add(LabeledRow("单位", _unit, 8));
                _customBlock.Children.Add(LabeledRow("货币代码", _currency, 8));
                _customBlock.Children.Add(LabeledRow("指标名称", _label, 8));

                TextBlock customHint = new TextBlock();
                customHint.Text = "字段路径用点号分隔，例如 data.balance 或 balance_infos.0.total_balance。" +
                                  "填了「剩余字段路径」就直接显示它；只填已用和上限时会自动相减。";
                customHint.FontSize = 11;
                customHint.Margin = new Thickness(96, 6, 0, 0);
                customHint.TextWrapping = TextWrapping.Wrap;
                customHint.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90));
                _customBlock.Children.Add(customHint);
                body.Children.Add(_customBlock);

                card.Child = body;
                ApplyKindVisibility();

                _kind.SelectionChanged += delegate
                {
                    if (_owner.Suppressed) return;
                    ProviderKind previous = _config.Kind;
                    string previousName = (_config.Name ?? "").Trim();
                    _owner.CommitAll();
                    ProviderKind next = ProviderCatalog.All[_kind.SelectedIndex < 0 ? 0 : _kind.SelectedIndex];
                    _config.Kind = next;
                    if (previousName.Length == 0 || previousName == ProviderCatalog.DisplayName(previous))
                        _config.Name = ProviderCatalog.DisplayName(next);
                    _owner.Rebuild();
                };

                return card;
            }

            private void ApplyKindVisibility()
            {
                EndpointField field = ProviderCatalog.Endpoint(_config.Kind);
                _customBlock.Visibility =
                    field == EndpointField.FullURI ? Visibility.Visible : Visibility.Collapsed;
                _baseUrlBlock.Visibility =
                    field == EndpointField.BaseURL ? Visibility.Visible : Visibility.Collapsed;
                _credentialBlock.Visibility =
                    _config.Kind == ProviderKind.ClaudeCode ? Visibility.Collapsed : Visibility.Visible;
            }

            public void Commit()
            {
                if (_kind.SelectedIndex >= 0)
                    _config.Kind = ProviderCatalog.All[_kind.SelectedIndex];
                _config.Name = _name.Text.Trim();
                if (_config.Name.Length == 0) _config.Name = ProviderCatalog.DisplayName(_config.Kind);
                _config.Enabled = _enabled.IsChecked == true;
                _config.Credential = _credential.Password;

                SetOption("endpoint", _endpoint.Text);
                SetOption("baseURL", _baseUrl.Text);
                SetOption("headersJSON", _headers.Text);
                SetOption("remainingPath", _remainingPath.Text);
                SetOption("usedPath", _usedPath.Text);
                SetOption("limitPath", _limitPath.Text);
                SetOption("currency", _currency.Text);
                SetOption("label", _label.Text);

                QuotaUnit[] units = new QuotaUnit[]
                {
                    QuotaUnit.Currency, QuotaUnit.Percent, QuotaUnit.Tokens,
                    QuotaUnit.Requests, QuotaUnit.Credits
                };
                int index = _unit.SelectedIndex;
                if (index < 0 || index >= units.Length) index = 0;
                _config.Options["unit"] = units[index].ToString();
            }

            private void SetOption(string key, string value)
            {
                value = (value ?? "").Trim();
                if (value.Length == 0) _config.Options.Remove(key);
                else _config.Options[key] = value;
            }

            private static void OpenUrl(string url)
            {
                try { Process.Start(url); }
                catch (Exception) { }
            }
        }
    }
}
