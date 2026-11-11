using System;
using System.Threading;
using System.Windows;

namespace QuotaBar
{
    /// <summary>把状态、挂件窗口、设置窗口串起来的控制器。</summary>
    public class QuotaApp
    {
        public AppState State;
        public WidgetWindow Widget;
        public TrayIcon Tray;
        /// <summary>true 表示用户真的要退出，用来区分「关窗口」和「退出程序」。</summary>
        public bool Quitting;

        private SettingsWindow _settings;

        public QuotaApp(AppState state)
        {
            State = state;
        }

        public ProviderConfig FocusProvider
        {
            get
            {
                if (State.Providers.Count == 0) return null;

                if (!string.IsNullOrEmpty(State.PreferredProviderId))
                {
                    foreach (ProviderConfig provider in State.Providers)
                    {
                        if (provider.Id == State.PreferredProviderId && provider.Enabled) return provider;
                    }
                }
                foreach (ProviderConfig provider in State.Providers)
                {
                    if (provider.Enabled) return provider;
                }
                return State.Providers[0];
            }
        }

        public void SaveStateOnly()
        {
            Store.Save(State);
        }

        public bool WidgetVisible
        {
            get { return Widget != null && Widget.IsVisible; }
        }

        /// <summary>隐藏 / 显示挂件。隐藏后程序继续在后台按设定间隔刷新。</summary>
        public void ToggleWidget()
        {
            if (Widget == null) return;
            if (Widget.IsVisible)
            {
                Widget.Hide();
            }
            else
            {
                Widget.Show();
                Widget.Activate();
            }
        }

        public void RefreshWidget()
        {
            if (Widget != null) Widget.RefreshAll();
        }

        public void UpdateTrayText(string text)
        {
            if (Tray != null) Tray.SetTooltip(text);
        }

        public void ShowSettings()
        {
            if (_settings != null)
            {
                _settings.Activate();
                return;
            }
            SettingsWindow window = new SettingsWindow(this);
            _settings = window;
            window.Closed += delegate { _settings = null; };
            window.Show();
            window.Activate();
        }

        public void Shutdown()
        {
            Quitting = true;
            SaveStateOnly();
            if (Tray != null) Tray.Dispose();
            Application.Current.Shutdown();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                SelfTest.Run();
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "QuotaBar_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("QuotaBar 已经在运行了（看桌面上的小窗口，或者右键它打开菜单）。",
                                    "QuotaBar", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Application application = new Application();
                // 关掉所有窗口也不退出 —— 程序要留在托盘里后台跑。
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                AppState state = Store.Load();
                // 把旧版本里配成「自定义接口」的 SoloAPI 自动升级成一等供应商。
                if (Store.MigrateLegacySoloAPI(state)) Store.Save(state);
                QuotaApp controller = new QuotaApp(state);
                WidgetWindow widget = new WidgetWindow(controller);
                controller.Widget = widget;
                controller.Tray = new TrayIcon(controller);

                application.Run(widget);

                controller.Tray.Dispose();
            }
        }
    }
}
