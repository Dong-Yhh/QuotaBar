using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace QuotaBar
{
    public enum QuotaUnit
    {
        Currency,
        Percent,
        Tokens,
        Requests,
        Credits
    }

    public enum ProviderKind
    {
        OpenRouter,
        DeepSeek,
        Moonshot,
        SiliconFlow,
        SoloAPI,
        ClaudeCode,
        OpenAI,
        Custom
    }

    /// <summary>一条额度指标，例如「7 天窗口剩余」「账户余额」。</summary>
    public class QuotaMetric
    {
        public string Id;
        public string Title;
        public QuotaUnit Unit;
        public string CurrencyCode;
        public double? Used;
        public double? Limit;
        public double? Remaining;
        public DateTime? ResetsAt;
        public string Footnote;
        /// <summary>true 表示主数值展示「剩余」（余额类）；false 表示展示「已用」。</summary>
        public bool RemainingPrimary = true;

        public double? ResolvedRemaining
        {
            get
            {
                if (Remaining.HasValue) return Remaining.Value;
                if (Limit.HasValue && Used.HasValue) return Math.Max(Limit.Value - Used.Value, 0);
                return null;
            }
        }

        public double? ResolvedUsed
        {
            get
            {
                if (Used.HasValue) return Used.Value;
                if (Limit.HasValue && Remaining.HasValue) return Math.Max(Limit.Value - Remaining.Value, 0);
                return null;
            }
        }

        /// <summary>剩余比例 0~1，无法判断（例如无上限）时返回 null。</summary>
        public double? RemainingFraction
        {
            get
            {
                if (!Limit.HasValue || Limit.Value <= 0) return null;
                double limit = Limit.Value;
                if (ResolvedRemaining.HasValue) return Clamp(ResolvedRemaining.Value / limit);
                if (ResolvedUsed.HasValue) return Clamp(1 - ResolvedUsed.Value / limit);
                return null;
            }
        }

        public string PrimaryText
        {
            get
            {
                double? value = RemainingPrimary ? ResolvedRemaining : ResolvedUsed;
                if (!value.HasValue) value = ResolvedRemaining;
                if (!value.HasValue) value = ResolvedUsed;
                if (!value.HasValue) return "—";
                return QuotaFormat.Value(value.Value, Unit, CurrencyCode);
            }
        }

        public string DetailText
        {
            get
            {
                List<string> parts = new List<string>();
                if (!string.IsNullOrEmpty(Footnote)) parts.Add(Footnote);
                if (ResetsAt.HasValue && ResetsAt.Value > DateTime.Now)
                    parts.Add(QuotaFormat.Countdown(ResetsAt.Value - DateTime.Now));
                return string.Join(" · ", parts.ToArray());
            }
        }

        private static double Clamp(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }

    public static class QuotaFormat
    {
        public static string Value(double value, QuotaUnit unit, string currencyCode)
        {
            switch (unit)
            {
                case QuotaUnit.Currency: return Money(value, currencyCode);
                case QuotaUnit.Percent: return Plain(value) + "%";
                default: return Plain(value);
            }
        }

        public static string Money(double value, string currencyCode)
        {
            string code = string.IsNullOrEmpty(currencyCode) ? "CNY" : currencyCode.ToUpperInvariant();
            string symbol;
            switch (code)
            {
                case "USD": symbol = "$"; break;
                case "CNY":
                case "RMB": symbol = "¥"; break;
                case "EUR": symbol = "€"; break;
                case "JPY": symbol = "¥"; break;
                default: symbol = code + " "; break;
            }
            return symbol + value.ToString("N2", CultureInfo.InvariantCulture);
        }

        public static string Plain(double value)
        {
            if (Math.Abs(value - Math.Round(value)) < 0.005 && Math.Abs(value) < 1000000)
                return Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);
            return value.ToString("N2", CultureInfo.InvariantCulture);
        }

        public static string Countdown(TimeSpan span)
        {
            if (span.TotalSeconds <= 0) return "即将重置";
            int totalMinutes = (int)(span.TotalMinutes);
            int days = totalMinutes / (60 * 24);
            int hours = (totalMinutes % (60 * 24)) / 60;
            int minutes = totalMinutes % 60;
            if (days > 0) return string.Format("{0} 天 {1} 小时后重置", days, hours);
            if (hours > 0) return string.Format("{0} 小时 {1} 分后重置", hours, minutes);
            return string.Format("{0} 分钟后重置", minutes);
        }

        public static string Ago(DateTime moment)
        {
            TimeSpan span = DateTime.Now - moment;
            if (span.TotalSeconds < 60) return "刚刚更新";
            if (span.TotalMinutes < 60) return string.Format("{0} 分钟前更新", (int)span.TotalMinutes);
            if (span.TotalHours < 24) return string.Format("{0} 小时前更新", (int)span.TotalHours);
            return string.Format("{0} 天前更新", (int)span.TotalDays);
        }
    }

    public class ProviderConfig
    {
        public string Id = Guid.NewGuid().ToString("N");
        public ProviderKind Kind = ProviderKind.OpenRouter;
        public string Name = "";
        public bool Enabled = true;
        public string Credential = "";
        public Dictionary<string, string> Options = new Dictionary<string, string>();

        /// <summary>低于该比例时进入告警配色，默认 20%。</summary>
        public double WarnThreshold
        {
            get
            {
                string raw;
                double value;
                if (Options.TryGetValue("warnThreshold", out raw) &&
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    if (value < 0.01) return 0.01;
                    if (value > 0.99) return 0.99;
                    return value;
                }
                return 0.2;
            }
        }

        public string Option(string key)
        {
            string value;
            return Options.TryGetValue(key, out value) ? value : "";
        }
    }

    public class AppState
    {
        public List<ProviderConfig> Providers = new List<ProviderConfig>();
        public string PreferredProviderId = "";
        public int RefreshMinutes = 10;
        public double WidgetLeft = double.NaN;
        public double WidgetTop = double.NaN;
        public bool Topmost = true;
        public bool PinnedToDesktop = false;
    }

    /// <summary>配置读写：%APPDATA%\QuotaBar\config.json</summary>
    public static class Store
    {
        /// <summary>自检时把读写重定向到临时目录，避免覆盖用户真实配置。</summary>
        public static string OverrideDirectory = null;

        public static string DataDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(OverrideDirectory)) return OverrideDirectory;
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "QuotaBar");
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(DataDirectory, "config.json"); }
        }

        public static AppState Load()
        {
            AppState state = new AppState();
            try
            {
                if (!File.Exists(ConfigPath)) return state;
                object root = Json.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8));

                string preferred = Json.Str(root, "preferredProviderId");
                if (preferred != null) state.PreferredProviderId = preferred;

                double? minutes = Json.Num(root, "refreshMinutes");
                if (minutes.HasValue && minutes.Value >= 1) state.RefreshMinutes = (int)minutes.Value;

                double? left = Json.Num(root, "widgetLeft");
                if (left.HasValue) state.WidgetLeft = left.Value;
                double? top = Json.Num(root, "widgetTop");
                if (top.HasValue) state.WidgetTop = top.Value;

                bool? topmost = Json.Flag(root, "topmost");
                if (topmost.HasValue) state.Topmost = topmost.Value;
                bool? pinned = Json.Flag(root, "pinnedToDesktop");
                if (pinned.HasValue) state.PinnedToDesktop = pinned.Value;

                List<object> providers = Json.AsArray(Json.Get(root, "providers"));
                if (providers != null)
                {
                    foreach (object item in providers)
                    {
                        ProviderConfig config = ParseProvider(item);
                        if (config != null) state.Providers.Add(config);
                    }
                }
            }
            catch (Exception)
            {
                // 配置损坏时退回默认值，避免程序起不来。
            }
            return state;
        }

        private static ProviderConfig ParseProvider(object item)
        {
            string kindText = Json.Str(item, "kind");
            if (string.IsNullOrEmpty(kindText)) return null;

            ProviderConfig config = new ProviderConfig();
            try
            {
                config.Kind = (ProviderKind)Enum.Parse(typeof(ProviderKind), kindText, true);
            }
            catch (Exception)
            {
                return null;
            }

            string id = Json.Str(item, "id");
            if (!string.IsNullOrEmpty(id)) config.Id = id;
            string name = Json.Str(item, "name");
            if (name != null) config.Name = name;
            string credential = Json.Str(item, "credential");
            if (credential != null) config.Credential = credential;
            bool? enabled = Json.Flag(item, "enabled");
            if (enabled.HasValue) config.Enabled = enabled.Value;

            Dictionary<string, object> options = Json.AsObject(Json.Get(item, "options"));
            if (options != null)
            {
                foreach (KeyValuePair<string, object> pair in options)
                {
                    string value = pair.Value as string;
                    if (value == null && pair.Value != null)
                        value = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                    if (value != null) config.Options[pair.Key] = value;
                }
            }
            return config;
        }

        public static void Save(AppState state)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);

                List<object> providers = new List<object>();
                foreach (ProviderConfig config in state.Providers)
                {
                    Dictionary<string, object> map = new Dictionary<string, object>();
                    map["id"] = config.Id;
                    map["kind"] = config.Kind.ToString();
                    map["name"] = config.Name;
                    map["enabled"] = config.Enabled;
                    map["credential"] = config.Credential;
                    // 直接给 Dictionary<string,string>，Json.Dump 走 IDictionary 分支能正确处理。
                    map["options"] = config.Options;
                    providers.Add(map);
                }

                Dictionary<string, object> root = new Dictionary<string, object>();
                root["providers"] = providers;
                root["preferredProviderId"] = state.PreferredProviderId;
                root["refreshMinutes"] = (double)state.RefreshMinutes;
                root["topmost"] = state.Topmost;
                root["pinnedToDesktop"] = state.PinnedToDesktop;
                if (!double.IsNaN(state.WidgetLeft)) root["widgetLeft"] = state.WidgetLeft;
                if (!double.IsNaN(state.WidgetTop)) root["widgetTop"] = state.WidgetTop;

                File.WriteAllText(ConfigPath, Json.Dump(root), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 写失败不致命，下次刷新再试。
            }
        }

        /// <summary>
        /// 早期版本里 SoloAPI 只能配成「自定义接口」，而且几乎配不对
        /// （指向对话用的 /v1、没有余额字段路径、key 塞在自定义请求头里）。
        /// 这里把旧配置自动升级成一等供应商。返回是否有改动。
        /// </summary>
        public static bool MigrateLegacySoloAPI(AppState state)
        {
            bool changed = false;

            foreach (ProviderConfig provider in state.Providers)
            {
                if (provider.Kind != ProviderKind.Custom) continue;
                if (provider.Option("endpoint").ToLowerInvariant().IndexOf("soloapi", StringComparison.Ordinal) < 0)
                    continue;

                string key = (provider.Credential ?? "").Trim();
                if (key.Length == 0)
                {
                    string raw = provider.Option("headersJSON");
                    if (raw.Trim().Length > 0)
                    {
                        try
                        {
                            Dictionary<string, object> parsed = Json.AsObject(Json.Parse(raw));
                            if (parsed != null)
                            {
                                object value;
                                if (parsed.TryGetValue("OPENAI_API_KEY", out value) && value != null)
                                    key = Convert.ToString(value, CultureInfo.InvariantCulture);
                                else if (parsed.TryGetValue("Authorization", out value) && value != null)
                                    key = Convert.ToString(value, CultureInfo.InvariantCulture);
                            }
                        }
                        catch (Exception) { }
                        if (key != null) key = key.Replace("Bearer ", "").Trim();
                    }
                }
                if (string.IsNullOrEmpty(key)) continue;

                provider.Kind = ProviderKind.SoloAPI;
                provider.Credential = key;

                Uri uri;
                if (Uri.TryCreate(provider.Option("endpoint"), UriKind.Absolute, out uri))
                    provider.Options["baseURL"] = uri.Scheme + "://" + uri.Host;

                provider.Options.Remove("endpoint");
                provider.Options.Remove("headersJSON");
                provider.Options.Remove("remainingPath");
                provider.Options.Remove("usedPath");
                provider.Options.Remove("limitPath");
                provider.Options.Remove("unit");
                provider.Options.Remove("currency");
                changed = true;
            }

            return changed;
        }
    }
}
