using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace QuotaBar
{
    public class WidgetWindow : Window
    {
        private readonly QuotaApp _app;

        private readonly Border _card = new Border();
        private readonly StackPanel _body = new StackPanel();
        private readonly StackPanel _metricPanel = new StackPanel();
        private readonly TextBlock _providerName = new TextBlock();
        private readonly TextBlock _updatedText = new TextBlock();

        private readonly Dictionary<string, List<QuotaMetric>> _metrics = new Dictionary<string, List<QuotaMetric>>();
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> _updated = new Dictionary<string, DateTime>();
        private readonly HashSet<string> _busy = new HashSet<string>();

        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private bool _refreshingAll;

        public WidgetWindow(QuotaApp app)
        {
            _app = app;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = _app.State.Topmost;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            BuildCard();
            Content = _card;

            MouseLeftButtonDown += OnMouseDown;
            MouseDoubleClick += delegate { _app.ShowSettings(); };
            // 每次右键都重建菜单，这样「切换供应商」列表永远是最新的。
            PreviewMouseRightButtonUp += OnRightClick;

            Loaded += OnLoaded;
            Closed += OnClosed;
            Closing += OnClosing;
        }

        // ---------- 外观 ----------

        private void BuildCard()
        {
            _card.Width = 276;
            _card.CornerRadius = new CornerRadius(16);
            _card.Padding = new Thickness(16, 14, 16, 16);
            _card.Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.42,
                Color = Colors.Black
            };

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _providerName.FontSize = 12;
            _providerName.FontWeight = FontWeights.SemiBold;
            _providerName.Foreground = Brushes.White;
            _providerName.VerticalAlignment = VerticalAlignment.Center;
            _providerName.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(_providerName, 0);
            header.Children.Add(_providerName);

            _updatedText.FontSize = 10;
            _updatedText.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            _updatedText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_updatedText, 1);
            header.Children.Add(_updatedText);

            _body.Children.Add(header);
            _body.Children.Add(_metricPanel);
            _card.Child = _body;
        }

        private static Color FromHex(string hex)
        {
            hex = (hex ?? "#666666").Replace("#", "");
            if (hex.Length < 6) hex = "666666";
            return Color.FromRgb(
                byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber));
        }

        private static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromRgb(
                (byte)(a.R * (1 - t) + b.R * t),
                (byte)(a.G * (1 - t) + b.G * t),
                (byte)(a.B * (1 - t) + b.B * t));
        }

        private static Color LevelColor(double? fraction, double threshold)
        {
            if (!fraction.HasValue) return Color.FromRgb(0x8A, 0x8A, 0x92);
            if (fraction.Value <= threshold * 0.4) return Color.FromRgb(0xF0, 0x4F, 0x4F);
            if (fraction.Value <= threshold) return Color.FromRgb(0xFA, 0xA6, 0x1E);
            return Color.FromRgb(0x2E, 0xC4, 0x7A);
        }

        private void ApplyTheme(ProviderKind kind)
        {
            Color accent = FromHex(ProviderCatalog.AccentHex(kind));
            Color dark = Color.FromRgb(0x1B, 0x1B, 0x20);

            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            brush.GradientStops.Add(new GradientStop(Blend(accent, dark, 0.58), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF5, dark.R, dark.G, dark.B), 1));
            _card.Background = brush;

            _card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            _card.BorderThickness = new Thickness(1);
        }

        // ---------- 生命周期 ----------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RestorePosition();
            if (_app.State.PinnedToDesktop) TryPinToDesktop();
            RefreshAll();
            RestartTimer();

            // 第一次运行、还没有任何供应商时，直接把设置窗口打开。
            if (_app.State.Providers.Count == 0)
            {
                Dispatcher.BeginInvoke(new Action(delegate { _app.ShowSettings(); }));
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _timer.Stop();
        }

        /// <summary>
        /// 关窗口 = 藏起来，程序继续在后台按设定间隔刷新。
        /// 只有从托盘菜单点「退出」才真的结束进程。
        /// </summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_app.Quitting) return;
            e.Cancel = true;
            Hide();
        }

        private void RestorePosition()
        {
            double left = _app.State.WidgetLeft;
            double top = _app.State.WidgetTop;

            if (double.IsNaN(left) || double.IsNaN(top))
            {
                Rect area = SystemParameters.WorkArea;
                left = area.Right - ActualWidth - 28;
                top = area.Bottom - ActualHeight - 28;
            }
            else
            {
                // 屏幕分辨率变了的话，把窗口拉回可见区域。
                Rect area = SystemParameters.WorkArea;
                if (left < area.Left - 40) left = area.Left + 40;
                if (top < area.Top - 10) top = area.Top + 10;
                if (left > area.Right - 60) left = area.Right - ActualWidth - 20;
                if (top > area.Bottom - 40) top = area.Bottom - ActualHeight - 20;
            }
            Left = left;
            Top = top;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); }
            catch (Exception) { }

            _app.State.WidgetLeft = Left;
            _app.State.WidgetTop = Top;
            _app.SaveStateOnly();
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            ContextMenu menu = BuildMenu();
            menu.PlacementTarget = this;
            ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void RestartTimer()
        {
            _timer.Stop();
            int minutes = _app.State.RefreshMinutes;
            if (minutes < 1) minutes = 1;
            _timer.Interval = TimeSpan.FromMinutes(minutes);
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            RefreshAll();
        }

        public void ReloadState()
        {
            Topmost = _app.State.Topmost;
            RestartTimer();
            if (_app.State.PinnedToDesktop) TryPinToDesktop();
            else UnpinFromDesktop();
            RefreshAll();
        }

        // ---------- 刷新 ----------

        public void RefreshAll()
        {
            if (_refreshingAll) return;
            _refreshingAll = true;

            List<ProviderConfig> targets = new List<ProviderConfig>();
            foreach (ProviderConfig provider in _app.State.Providers)
            {
                if (provider.Enabled) targets.Add(provider);
            }

            if (targets.Count == 0)
            {
                _refreshingAll = false;
                Render();
                return;
            }

            _busy.Clear();
            foreach (ProviderConfig provider in targets) _busy.Add(provider.Id);
            Render();

            foreach (ProviderConfig provider in targets)
            {
                ProviderConfig captured = provider;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    List<QuotaMetric> result = null;
                    string error = null;
                    try { result = ProviderEngine.Fetch(captured); }
                    catch (Exception failure) { error = failure.Message; }

                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (error == null)
                        {
                            _metrics[captured.Id] = result;
                            _errors.Remove(captured.Id);
                        }
                        else
                        {
                            _errors[captured.Id] = error;
                            _metrics.Remove(captured.Id);
                        }
                        _updated[captured.Id] = DateTime.Now;
                        _busy.Remove(captured.Id);
                        if (_busy.Count == 0) _refreshingAll = false;
                        Render();
                    }));
                });
            }
        }

        private void RefreshOne(string providerId)
        {
            ProviderConfig target = null;
            foreach (ProviderConfig provider in _app.State.Providers)
            {
                if (provider.Id == providerId) target = provider;
            }
            if (target == null) return;

            _busy.Add(providerId);
            Render();

            ProviderConfig captured = target;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<QuotaMetric> result = null;
                string error = null;
                try { result = ProviderEngine.Fetch(captured); }
                catch (Exception failure) { error = failure.Message; }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (error == null) { _metrics[captured.Id] = result; _errors.Remove(captured.Id); }
                    else { _errors[captured.Id] = error; _metrics.Remove(captured.Id); }
                    _updated[captured.Id] = DateTime.Now;
                    _busy.Remove(captured.Id);
                    Render();
                }));
            });
        }

        // ---------- 渲染 ----------

        private ProviderConfig CurrentProvider()
        {
            ProviderConfig focus = _app.FocusProvider;
            return focus;
        }

        private void Render()
        {
            ProviderConfig provider = CurrentProvider();
            _metricPanel.Children.Clear();

            if (provider == null)
            {
                _app.UpdateTrayText("QuotaBar · 未配置");
                _providerName.Text = "AI 额度";
                _updatedText.Text = "";
                _card.Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1B, 0x1B, 0x20));

                TextBlock hint = new TextBlock();
                hint.Text = "还没有添加供应商\n双击这里开始配置";
                hint.FontSize = 12;
                hint.Margin = new Thickness(0, 14, 0, 2);
                hint.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
                _metricPanel.Children.Add(hint);
                return;
            }

            ApplyTheme(provider.Kind);
            _providerName.Text = provider.Name;

            if (_busy.Contains(provider.Id))
            {
                _updatedText.Text = "刷新中…";
            }
            else
            {
                DateTime when;
                _updatedText.Text = _updated.TryGetValue(provider.Id, out when) ? QuotaFormat.Ago(when) : "";
            }

            string error;
            if (_errors.TryGetValue(provider.Id, out error))
            {
                _app.UpdateTrayText("QuotaBar · " + provider.Name + " · 出错了");
                TextBlock message = new TextBlock();
                message.Text = error;
                message.FontSize = 11;
                message.TextWrapping = TextWrapping.Wrap;
                message.Margin = new Thickness(0, 12, 0, 0);
                message.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x77));
                _metricPanel.Children.Add(message);

                TextBlock retry = new TextBlock();
                retry.Text = "右键 →「刷新」重试";
                retry.FontSize = 10;
                retry.Margin = new Thickness(0, 6, 0, 0);
                retry.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
                _metricPanel.Children.Add(retry);
                return;
            }

            List<QuotaMetric> metrics;
            if (!_metrics.TryGetValue(provider.Id, out metrics) || metrics == null || metrics.Count == 0)
            {
                _app.UpdateTrayText("QuotaBar · " + provider.Name + " · 等待数据");
                TextBlock waiting = new TextBlock();
                waiting.Text = "等待数据…";
                waiting.FontSize = 11;
                waiting.Margin = new Thickness(0, 12, 0, 0);
                waiting.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
                _metricPanel.Children.Add(waiting);
                return;
            }

            int limit = metrics.Count > 3 ? 3 : metrics.Count;
            for (int i = 0; i < limit; i++)
            {
                _metricPanel.Children.Add(BuildMetricBlock(metrics[i], provider, i == 0));
            }
            _app.UpdateTrayText("QuotaBar · " + provider.Name + " " + metrics[0].PrimaryText);
        }

        private FrameworkElement BuildMetricBlock(QuotaMetric metric, ProviderConfig provider, bool primary)
        {
            StackPanel block = new StackPanel();
            block.Margin = new Thickness(0, 12, 0, 0);

            TextBlock title = new TextBlock();
            title.Text = metric.Title;
            title.FontSize = 11;
            title.Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
            block.Children.Add(title);

            TextBlock value = new TextBlock();
            value.Text = metric.PrimaryText;
            value.FontSize = primary ? 28 : 18;
            value.FontWeight = FontWeights.Bold;
            value.Margin = new Thickness(0, 2, 0, 0);
            value.Foreground = new SolidColorBrush(LevelColor(metric.RemainingFraction, provider.WarnThreshold));
            block.Children.Add(value);

            block.Children.Add(BuildBar(metric, provider));

            string detail = metric.DetailText;
            if (!string.IsNullOrEmpty(detail))
            {
                TextBlock note = new TextBlock();
                note.Text = detail;
                note.FontSize = 10;
                note.Margin = new Thickness(0, 6, 0, 0);
                note.TextWrapping = TextWrapping.Wrap;
                note.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
                block.Children.Add(note);
            }
            return block;
        }

        private FrameworkElement BuildBar(QuotaMetric metric, ProviderConfig provider)
        {
            double? fraction = metric.RemainingFraction;

            Grid track = new Grid();
            track.Height = 7;
            track.Margin = new Thickness(0, 8, 0, 0);

            Border background = new Border();
            background.CornerRadius = new CornerRadius(4);
            background.Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
            track.Children.Add(background);

            if (fraction.HasValue)
            {
                Border fill = new Border();
                fill.CornerRadius = new CornerRadius(4);
                fill.HorizontalAlignment = HorizontalAlignment.Left;
                fill.Background = new SolidColorBrush(LevelColor(fraction, provider.WarnThreshold));
                track.SizeChanged += delegate
                {
                    fill.Width = Math.Max(track.ActualWidth * fraction.Value, fraction.Value > 0.005 ? 7 : 0);
                };
                track.Children.Add(fill);
            }
            return track;
        }

        // ---------- 右键菜单 ----------

        private ContextMenu BuildMenu()
        {
            ContextMenu menu = new ContextMenu();

            MenuItem refresh = new MenuItem();
            refresh.Header = "刷新全部";
            refresh.Click += delegate { RefreshAll(); };
            menu.Items.Add(refresh);

            MenuItem switchItem = new MenuItem();
            switchItem.Header = "切换供应商";
            switchItem.IsEnabled = _app.State.Providers.Count > 0;
            foreach (ProviderConfig provider in _app.State.Providers)
            {
                MenuItem item = new MenuItem();
                bool isCurrent = _app.FocusProvider != null && _app.FocusProvider.Id == provider.Id;
                item.Header = (isCurrent ? "● " : "    ") + provider.Name;
                string id = provider.Id;
                item.Click += delegate
                {
                    _app.State.PreferredProviderId = id;
                    _app.SaveStateOnly();
                    Render();
                };
                switchItem.Items.Add(item);
            }
            menu.Items.Add(switchItem);

            MenuItem refreshOne = new MenuItem();
            refreshOne.Header = "只刷新当前供应商";
            refreshOne.Click += delegate
            {
                ProviderConfig current = CurrentProvider();
                if (current != null) RefreshOne(current.Id);
            };
            menu.Items.Add(refreshOne);

            MenuItem hide = new MenuItem();
            hide.Header = "隐藏挂件（后台继续刷新）";
            hide.Click += delegate { Hide(); };
            menu.Items.Add(hide);

            menu.Items.Add(new Separator());

            MenuItem topmost = new MenuItem();
            topmost.Header = "总在最前";
            topmost.IsCheckable = true;
            topmost.IsChecked = _app.State.Topmost;
            topmost.Click += delegate
            {
                _app.State.Topmost = !_app.State.Topmost;
                Topmost = _app.State.Topmost;
                _app.SaveStateOnly();
            };
            menu.Items.Add(topmost);

            MenuItem pinned = new MenuItem();
            pinned.Header = "贴到桌面（实验）";
            pinned.IsCheckable = true;
            pinned.IsChecked = _app.State.PinnedToDesktop;
            pinned.Click += delegate
            {
                _app.State.PinnedToDesktop = !_app.State.PinnedToDesktop;
                if (_app.State.PinnedToDesktop)
                {
                    _app.State.Topmost = false;
                    Topmost = false;
                    PinToDesktop();
                }
                else
                {
                    UnpinFromDesktop();
                }
                _app.SaveStateOnly();
            };
            menu.Items.Add(pinned);

            menu.Items.Add(new Separator());

            MenuItem settings = new MenuItem();
            settings.Header = "设置…";
            settings.Click += delegate { _app.ShowSettings(); };
            menu.Items.Add(settings);

            MenuItem quit = new MenuItem();
            quit.Header = "退出";
            quit.Click += delegate { _app.Shutdown(); };
            menu.Items.Add(quit);

            return menu;
        }

        // ---------- 贴到桌面（Rainmeter 那套 WorkerW 手法） ----------

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string className, string windowName);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam,
                                                        uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr child);

        private IntPtr _originalParent = IntPtr.Zero;

        private void TryPinToDesktop()
        {
            try { PinToDesktop(); }
            catch (Exception) { }
        }

        private void PinToDesktop()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return;

            IntPtr unused;
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out unused);

            IntPtr workerw = IntPtr.Zero;
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
                workerw = FindWindowEx(IntPtr.Zero, defView, "WorkerW", null);
            if (workerw == IntPtr.Zero) workerw = progman;

            if (_originalParent == IntPtr.Zero) _originalParent = GetParent(handle);
            SetParent(handle, workerw);
        }

        private void UnpinFromDesktop()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero && _originalParent != IntPtr.Zero)
                    SetParent(handle, _originalParent);
            }
            catch (Exception) { }
        }
    }
}
