using System;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QuotaBar
{
    /// <summary>
    /// 托盘图标。有了它，关掉挂件窗口后程序依然在后台跑、按设定间隔刷新。
    /// 用命名空间别名，避免 System.Windows.Forms 和 System.Windows 的类型打架。
    /// </summary>
    public class TrayIcon : IDisposable
    {
        private readonly QuotaApp _app;
        private readonly Forms.NotifyIcon _icon;
        private readonly Forms.ContextMenuStrip _menu;
        private readonly Forms.ToolStripMenuItem _toggle;

        public TrayIcon(QuotaApp app)
        {
            _app = app;

            _menu = new Forms.ContextMenuStrip();

            _toggle = new Forms.ToolStripMenuItem("隐藏挂件");
            _toggle.Click += delegate { _app.ToggleWidget(); };
            _menu.Items.Add(_toggle);

            Forms.ToolStripMenuItem refresh = new Forms.ToolStripMenuItem("立即刷新");
            refresh.Click += delegate { _app.RefreshWidget(); };
            _menu.Items.Add(refresh);

            Forms.ToolStripMenuItem settings = new Forms.ToolStripMenuItem("设置…");
            settings.Click += delegate { _app.ShowSettings(); };
            _menu.Items.Add(settings);

            _menu.Items.Add(new Forms.ToolStripSeparator());

            Forms.ToolStripMenuItem quit = new Forms.ToolStripMenuItem("退出");
            quit.Click += delegate { _app.Shutdown(); };
            _menu.Items.Add(quit);

            // 每次打开菜单时同步一下文字，避免显示错。
            _menu.Opening += delegate
            {
                _toggle.Text = _app.WidgetVisible ? "隐藏挂件" : "显示挂件";
            };

            _icon = new Forms.NotifyIcon();
            _icon.Icon = BuildIcon();
            _icon.Text = "QuotaBar · AI 额度";
            _icon.ContextMenuStrip = _menu;
            _icon.Visible = true;
            _icon.DoubleClick += delegate { _app.ToggleWidget(); };
        }

        /// <summary>显示当前额度，方便鼠标悬停直接看。系统限制 63 个字符。</summary>
        public void SetTooltip(string text)
        {
            if (_icon == null) return;
            if (text == null) text = "";
            if (text.Length > 60) text = text.Substring(0, 60) + "…";
            try { _icon.Text = text; }
            catch (Exception) { }
        }

        public void Dispose()
        {
            try
            {
                if (_icon != null)
                {
                    _icon.Visible = false;
                    _icon.Dispose();
                }
                if (_menu != null) _menu.Dispose();
            }
            catch (Exception) { }
        }

        // ---------- 运行时画一个托盘图标，省得额外带 .ico 文件 ----------

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Drawing.Icon BuildIcon()
        {
            using (Drawing.Bitmap bitmap = new Drawing.Bitmap(32, 32))
            {
                using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    graphics.Clear(Drawing.Color.Transparent);

                    Drawing.RectangleF body = new Drawing.RectangleF(1.5f, 1.5f, 29, 29);
                    using (Drawing.Drawing2D.GraphicsPath path = RoundedRect(body, 8.5f))
                    using (Drawing.Drawing2D.LinearGradientBrush brush =
                           new Drawing.Drawing2D.LinearGradientBrush(
                               body,
                               Drawing.Color.FromArgb(0x5A, 0x6B, 0xFF),
                               Drawing.Color.FromArgb(0x9B, 0x4D, 0xFF),
                               45f))
                    {
                        graphics.FillPath(brush, path);
                    }

                    using (Drawing.SolidBrush white = new Drawing.SolidBrush(Drawing.Color.White))
                    {
                        FillBar(graphics, white, 8f, 9.5f, 16f, 3.6f);
                        FillBar(graphics, white, 8f, 14.5f, 11f, 3.6f);
                        FillBar(graphics, white, 8f, 19.5f, 6.5f, 3.6f);
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Drawing.Icon temporary = Drawing.Icon.FromHandle(handle))
                    {
                        return (Drawing.Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static Drawing.Drawing2D.GraphicsPath RoundedRect(Drawing.RectangleF rect, float radius)
        {
            Drawing.Drawing2D.GraphicsPath path = new Drawing.Drawing2D.GraphicsPath();
            float diameter = radius * 2;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void FillBar(Drawing.Graphics graphics, Drawing.Brush brush,
                                    float x, float y, float width, float height)
        {
            using (Drawing.Drawing2D.GraphicsPath path =
                   RoundedRect(new Drawing.RectangleF(x, y, width, height), height / 2))
            {
                graphics.FillPath(brush, path);
            }
        }
    }
}
