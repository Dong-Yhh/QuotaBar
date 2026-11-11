using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace QuotaBar
{
    /// <summary>
    /// 用固定的响应样本跑一遍所有解析逻辑，不联网。
    /// 运行：QuotaBar.exe --selftest
    /// 结果同时写到 %APPDATA%\QuotaBar\selftest.txt
    /// </summary>
    public static class SelfTest
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> _lines = new List<string>();

        private static void Check(string name, bool ok, string detail)
        {
            if (ok)
            {
                _passed++;
                _lines.Add("  ok   " + name);
            }
            else
            {
                _failed++;
                _lines.Add("  FAIL " + name + (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
            }
        }

        private static void Section(string title)
        {
            _lines.Add("");
            _lines.Add("== " + title + " ==");
        }

        private static bool Near(double? value, double expected)
        {
            if (!value.HasValue) return false;
            return Math.Abs(value.Value - expected) < 0.001;
        }

        private static object Parse(string text)
        {
            try { return Json.Parse(text); }
            catch (Exception) { return null; }
        }

        public static void Run()
        {
            _passed = 0;
            _failed = 0;
            _lines.Clear();
            _lines.Add("QuotaBar 自检 —— " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            // 自检会写配置文件，先重定向到临时目录，别动用户的真实配置。
            string reportPath = Path.Combine(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuotaBar"),
                "selftest.txt");
            string temporary = Path.Combine(Path.GetTempPath(), "QuotaBarSelfTest");
            try
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                Directory.CreateDirectory(temporary);
                Store.OverrideDirectory = temporary;
            }
            catch (Exception) { }

            Section("JSON 解析器");
            {
                object root = Parse("{\"a\":{\"b\":[{\"c\":1},{\"c\":2}]},\"flag\":true,\"text\":\"3.5\",\"nil\":null}");
                Check("解析嵌套对象/数组", root != null);
                Check("数组下标取值", Near(Json.Num(root, "a.b.1.c"), 2));
                Check("字符串转数字", Near(Json.Num(root, "text"), 3.5));
                Check("布尔读取", Json.Flag(root, "flag") == true);
                Check("null 字段", Json.Get(root, "nil") == null);
                Check("缺失路径返回 null", Json.Num(root, "a.x.y") == null);
                Check("下标越界返回 null", Json.Num(root, "a.b.9.c") == null);

                Check("转义字符", "a\"b\nc\\d".Equals(Json.Str(Parse("{\"v\":\"a\\\"b\\nc\\\\d\"}"), "v")));
                Check("\\u 转义", "中".Equals(Json.Str(Parse("{\"v\":\"\\u4e2d\"}"), "v")));
                Check("负数与指数", Near(Json.Num(Parse("{\"v\":-1.5e2}"), "v"), -150));

                Dictionary<string, object> obj = new Dictionary<string, object>();
                obj["名字"] = "额度";
                obj["值"] = 12.5;
                obj["开关"] = true;
                List<object> list = new List<object>();
                list.Add(1.0);
                list.Add("x");
                obj["列表"] = list;
                string dumped = Json.Dump(obj);
                Check("Dump→Parse 往返一致", Near(Json.Num(Parse(dumped), "值"), 12.5));
                Check("Dump 保留中文", dumped.IndexOf("额度", StringComparison.Ordinal) >= 0);
                Check("Dump 保留数组", Json.Num(Parse(dumped), "列表.0") != null);
            }

            Section("OpenRouter");
            {
                List<QuotaMetric> credits = ProviderEngine.ParseOpenRouterCredits(
                    Parse("{\"data\":{\"total_credits\":10.0,\"total_usage\":3.2}}"));
                Check("credits 解析出 1 条", credits.Count == 1, "实际 " + credits.Count);
                Check("账户余额 = 6.8", credits.Count > 0 && Near(credits[0].ResolvedRemaining, 6.8));
                Check("剩余比例 = 0.68", credits.Count > 0 && Near(credits[0].RemainingFraction, 0.68));

                List<QuotaMetric> key = ProviderEngine.ParseOpenRouterKey(
                    Parse("{\"data\":{\"label\":\"k\",\"usage\":1.5,\"limit\":10,\"limit_remaining\":8.5,\"is_free_tier\":false}}"));
                Check("key 解析出 1 条", key.Count == 1);
                Check("密钥剩余 = 8.5", key.Count > 0 && Near(key[0].ResolvedRemaining, 8.5));
                Check("密钥已用 = 1.5", key.Count > 0 && Near(key[0].ResolvedUsed, 1.5));

                List<QuotaMetric> unlimited = ProviderEngine.ParseOpenRouterKey(
                    Parse("{\"data\":{\"usage\":4.25,\"limit\":null,\"limit_remaining\":null}}"));
                Check("无上限时降级为用量",
                      unlimited.Count == 1 && !unlimited[0].RemainingPrimary);
                Check("无上限用量 = 4.25", unlimited.Count > 0 && Near(unlimited[0].ResolvedUsed, 4.25));
            }

            Section("DeepSeek");
            {
                List<QuotaMetric> metrics = ProviderEngine.ParseDeepSeek(Parse(
                    "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\"," +
                    "\"total_balance\":\"110.00\",\"granted_balance\":\"10.00\",\"topped_up_balance\":\"100.00\"}]}"));
                Check("解析出 1 条", metrics.Count == 1, "实际 " + metrics.Count);
                Check("余额 = 110", metrics.Count > 0 && Near(metrics[0].ResolvedRemaining, 110));
                Check("货币 = CNY", metrics.Count > 0 && metrics[0].CurrencyCode == "CNY");
                Check("备注含充值", metrics.Count > 0 && metrics[0].Footnote != null &&
                                      metrics[0].Footnote.IndexOf("充值", StringComparison.Ordinal) >= 0);

                List<QuotaMetric> broken = ProviderEngine.ParseDeepSeek(Parse(
                    "{\"is_available\":false,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"0.00\"}]}"));
                Check("余额不足时给出状态提示",
                      broken.Count == 2 && broken[0].Id == "unavailable");
            }

            Section("Kimi (Moonshot)");
            {
                List<QuotaMetric> metrics = ProviderEngine.ParseMoonshot(Parse(
                    "{\"code\":0,\"data\":{\"available_balance\":49.58,\"voucher_balance\":0,\"cash_balance\":49.58},\"status\":true}"));
                Check("解析出 1 条", metrics.Count == 1);
                Check("可用余额 = 49.58", metrics.Count > 0 && Near(metrics[0].ResolvedRemaining, 49.58));
            }

            Section("SiliconFlow 硅基流动");
            {
                List<QuotaMetric> metrics = ProviderEngine.ParseSiliconFlow(Parse(
                    "{\"code\":20000,\"message\":\"OK\",\"status\":true,\"data\":{\"id\":\"1\",\"balance\":\"0.88\"," +
                    "\"chargeBalance\":\"88.00\",\"totalBalance\":\"88.00\"}}"));
                Check("解析出 1 条", metrics.Count == 1);
                Check("可用余额 = 0.88", metrics.Count > 0 && Near(metrics[0].ResolvedRemaining, 0.88));
            }

            Section("Claude Code 订阅");
            {
                object payload = Parse(
                    "{\"five_hour\":{\"utilization\":79.0,\"resets_at\":\"2099-11-14T02:00:00.000Z\"}," +
                    "\"seven_day\":{\"utilization\":26.0,\"resets_at\":\"2099-11-18T07:00:00Z\"}," +
                    "\"seven_day_opus\":null," +
                    "\"seven_day_sonnet\":{\"utilization\":12.5,\"resets_at\":\"2099-11-18T07:00:00Z\"}," +
                    "\"extra_usage\":{\"is_enabled\":true,\"monthly_limit\":1000,\"used_credits\":250}}");
                List<QuotaMetric> metrics = ProviderEngine.ParseClaudeUsage(payload);
                Check("解析出 4 条（5h/7d/sonnet/extra）", metrics.Count == 4, "实际 " + metrics.Count);

                QuotaMetric fiveHour = Find(metrics, "five_hour");
                Check("5h 剩余 21%", fiveHour != null && Near(fiveHour.ResolvedRemaining, 21));
                Check("5h 剩余比例 0.21", fiveHour != null && Near(fiveHour.RemainingFraction, 0.21));
                Check("5h 重置时间已解析", fiveHour != null && fiveHour.ResetsAt.HasValue);
                Check("null 窗口被跳过", Find(metrics, "seven_day_opus") == null);

                QuotaMetric extra = Find(metrics, "extra_usage");
                Check("额外额度 1000 美分 = $10", extra != null && Near(extra.Limit, 10));
                Check("额外额度已用 250 美分 = $2.50", extra != null && Near(extra.ResolvedUsed, 2.5));
                Check("额外额度剩余 $7.50", extra != null && Near(extra.ResolvedRemaining, 7.5));
            }

            Section("OpenAI 组织用量");
            {
                double? cost = ProviderEngine.ParseOpenAIMonthCost(Parse(
                    "{\"object\":\"page\",\"data\":[" +
                    "{\"object\":\"bucket\",\"results\":[{\"amount\":{\"value\":0.06,\"currency\":\"usd\"}}," +
                    "{\"amount\":{\"value\":1.44,\"currency\":\"usd\"}}]}," +
                    "{\"object\":\"bucket\",\"results\":[{\"amount\":{\"value\":2.10,\"currency\":\"usd\"}}]}]," +
                    "\"has_more\":false}"));
                Check("本月花费 = 3.60", Near(cost, 3.6), "实际 " + cost);

                double tokens = ProviderEngine.ParseOpenAITotalTokens(Parse(
                    "{\"data\":[{\"results\":[{\"input_tokens\":1000,\"output_tokens\":500,\"num_model_requests\":3}]}]}"));
                Check("Token 合计 = 1500", Near(tokens, 1500), "实际 " + tokens);
            }

            Section("自定义接口");
            {
                ProviderConfig config = new ProviderConfig();
                config.Kind = ProviderKind.Custom;
                config.Options["remainingPath"] = "data.balance";
                config.Options["usedPath"] = "data.used";
                config.Options["limitPath"] = "data.quota";
                config.Options["currency"] = "CNY";
                config.Options["label"] = "自建余额";

                QuotaMetric metric = ProviderEngine.ParseCustom(
                    Parse("{\"code\":0,\"data\":{\"balance\":12.5,\"used\":7.5,\"quota\":20}}"), config);
                Check("剩余 = 12.5", Near(metric.ResolvedRemaining, 12.5));
                Check("名称来自配置", metric.Title == "自建余额");

                ProviderConfig derived = new ProviderConfig();
                derived.Kind = ProviderKind.Custom;
                derived.Options["usedPath"] = "used";
                derived.Options["limitPath"] = "limit";
                QuotaMetric derivedMetric = ProviderEngine.ParseCustom(Parse("{\"used\":30,\"limit\":100}"), derived);
                Check("只给已用+上限时自动相减 = 70", Near(derivedMetric.ResolvedRemaining, 70));
                Check("比例 = 0.7", Near(derivedMetric.RemainingFraction, 0.7));

                ProviderConfig broken = new ProviderConfig();
                broken.Kind = ProviderKind.Custom;
                broken.Options["remainingPath"] = "nope.missing";
                bool threw = false;
                try { ProviderEngine.ParseCustom(Parse("{\"a\":1}"), broken); }
                catch (QuotaException) { threw = true; }
                Check("路径取不到时抛错", threw);
            }

            Section("SoloAPI（用实测抓到的真实响应）");
            {
                // GET https://api.soloapi.cc/v1/usage 的真实返回：余额 $10，从未使用过
                object fresh = Parse(
                    "{\"balance\":10,\"daily_usage\":[],\"isValid\":true,\"mode\":\"unrestricted\"," +
                    "\"planName\":\"钱包余额\",\"remaining\":10,\"unit\":\"USD\"," +
                    "\"usage\":{\"rpm\":0,\"tpm\":0," +
                    "\"today\":{\"cost\":0,\"requests\":0,\"total_tokens\":0}," +
                    "\"total\":{\"cost\":0,\"requests\":0}}}");
                List<QuotaMetric> freshMetrics = ProviderEngine.ParseSoloAPIUsage(fresh);
                Check("未用过的账号只解析出余额 1 条", freshMetrics.Count == 1, "实际 " + freshMetrics.Count);
                Check("余额 = $10", freshMetrics.Count > 0 && Near(freshMetrics[0].ResolvedRemaining, 10));
                Check("货币 = USD", freshMetrics.Count > 0 && freshMetrics[0].CurrencyCode == "USD");
                Check("套餐名进备注", freshMetrics.Count > 0 && freshMetrics[0].Footnote == "钱包余额");
                Check("余额是主指标", freshMetrics.Count > 0 && freshMetrics[0].RemainingPrimary);
                Check("未知充值总额时没有进度条",
                      freshMetrics.Count > 0 && !freshMetrics[0].RemainingFraction.HasValue);

                object used = Parse(
                    "{\"balance\":7.25,\"remaining\":7.25,\"unit\":\"USD\",\"isValid\":true," +
                    "\"planName\":\"钱包余额\",\"usage\":{\"today\":{\"cost\":0.42,\"requests\":12," +
                    "\"total_tokens\":3456},\"total\":{\"cost\":2.75,\"requests\":88}}}");
                List<QuotaMetric> usedMetrics = ProviderEngine.ParseSoloAPIUsage(used);
                Check("有消费时解析出 3 条", usedMetrics.Count == 3, "实际 " + usedMetrics.Count);
                Check("余额 = $7.25", usedMetrics.Count > 0 && Near(usedMetrics[0].ResolvedRemaining, 7.25));

                QuotaMetric today = Find(usedMetrics, "today");
                Check("今日消费 = $0.42", today != null && Near(today.ResolvedUsed, 0.42));
                Check("今日消费按「已用」口径", today != null && !today.RemainingPrimary);
                Check("今日备注含请求数",
                      today != null && today.Footnote != null &&
                      today.Footnote.IndexOf("12 次请求", StringComparison.Ordinal) >= 0,
                      today == null ? "null" : today.Footnote);

                QuotaMetric total = Find(usedMetrics, "total");
                Check("累计消费 = $2.75", total != null && Near(total.ResolvedUsed, 2.75));

                List<QuotaMetric> invalidMetrics = ProviderEngine.ParseSoloAPIUsage(
                    Parse("{\"balance\":0,\"remaining\":0,\"unit\":\"USD\",\"isValid\":false}"));
                Check("失效时先给状态提示", invalidMetrics.Count > 0 && invalidMetrics[0].Id == "invalid");
                Check("失效时仍显示余额", invalidMetrics.Count == 2);

                List<QuotaMetric> fallback = ProviderEngine.ParseSoloAPIBalance(
                    Parse("{\"is_active\":true,\"balance\":10,\"unit\":\"USD\"}"));
                Check("兜底接口能解析", fallback.Count > 0 && Near(fallback[0].ResolvedRemaining, 10));

                ProviderConfig soloConfig = new ProviderConfig();
                soloConfig.Kind = ProviderKind.SoloAPI;
                Check("默认地址", ProviderEngine.SoloAPIBaseUrl(soloConfig) == "https://api.soloapi.cc",
                      ProviderEngine.SoloAPIBaseUrl(soloConfig));
                soloConfig.Options["baseURL"] = "https://api.soloapi.cc/v1/";
                Check("带 /v1 和结尾斜杠会自动纠正",
                      ProviderEngine.SoloAPIBaseUrl(soloConfig) == "https://api.soloapi.cc",
                      ProviderEngine.SoloAPIBaseUrl(soloConfig));
            }

            Section("格式化");
            {
                Check("USD 金额", QuotaFormat.Money(8.5, "USD") == "$8.50",
                      QuotaFormat.Money(8.5, "USD"));
                Check("CNY 金额", QuotaFormat.Money(49.58, "CNY") == "¥49.58",
                      QuotaFormat.Money(49.58, "CNY"));

                QuotaMetric quota = ProviderEngine.MakeQuota("x", "7 天窗口", 26, 100, null, null);
                Check("百分比主值 = 74%", quota.PrimaryText == "74%", quota.PrimaryText);

                QuotaMetric balance = ProviderEngine.MakeBalance("y", "余额", 12.5, "CNY", null, null, null);
                Check("金额主值", balance.PrimaryText == "¥12.50", balance.PrimaryText);

                QuotaMetric low = ProviderEngine.MakeQuota("z", "低", 95, 100, null, null);
                Check("剩余 5% 比例正确", Near(low.RemainingFraction, 0.05));
            }

            Section("配置读写");
            {
                AppState state = new AppState();
                ProviderConfig provider = new ProviderConfig();
                provider.Kind = ProviderKind.DeepSeek;
                provider.Name = "我的 DeepSeek";
                provider.Credential = "sk-test";
                provider.Options["warnThreshold"] = "0.15";
                state.Providers.Add(provider);
                state.PreferredProviderId = provider.Id;

                Store.Save(state);
                AppState reloaded = Store.Load();
                Check("配置文件已写出", File.Exists(Store.ConfigPath));
                Check("供应商往返一致",
                      reloaded.Providers.Count == 1 && reloaded.Providers[0].Name == "我的 DeepSeek");
                Check("凭证往返一致",
                      reloaded.Providers.Count == 1 && reloaded.Providers[0].Credential == "sk-test");
                Check("枚举往返一致",
                      reloaded.Providers.Count == 1 && reloaded.Providers[0].Kind == ProviderKind.DeepSeek);
                Check("阈值往返一致",
                      reloaded.Providers.Count == 1 && Near(reloaded.Providers[0].WarnThreshold, 0.15));
                Check("首选供应商保留", reloaded.PreferredProviderId == provider.Id);
            }

            Section("错误处理");
            {
                ProviderConfig empty = new ProviderConfig();
                empty.Kind = ProviderKind.DeepSeek;

                bool threw = false;
                string message = "";
                try { ProviderEngine.Fetch(empty); }
                catch (QuotaException error) { threw = true; message = error.Message; }
                Check("缺凭证时抛错而不是崩溃", threw);
                Check("缺凭证提示可读",
                      message.IndexOf("还没有填写凭证", StringComparison.Ordinal) >= 0, message);
            }

            _lines.Add("");
            _lines.Add("---------------------------------------");
            _lines.Add("通过 " + _passed + " 项，失败 " + _failed + " 项");

            string report = string.Join(Environment.NewLine, _lines.ToArray());

            string path = "";
            try
            {
                Store.OverrideDirectory = null;
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                path = reportPath;
                File.WriteAllText(path, report, new UTF8Encoding(false));
            }
            catch (Exception) { }

            try { Console.WriteLine(report); }
            catch (Exception) { }

            string summary = "通过 " + _passed + " 项，失败 " + _failed + " 项。\n\n" +
                             (path.Length > 0 ? "完整报告：" + path : "报告写入失败。");
            MessageBox.Show(summary, _failed == 0 ? "QuotaBar 自检 · 全部通过" : "QuotaBar 自检 · 有失败项",
                            MessageBoxButton.OK,
                            _failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private static QuotaMetric Find(List<QuotaMetric> metrics, string id)
        {
            foreach (QuotaMetric metric in metrics)
            {
                if (metric.Id == id) return metric;
            }
            return null;
        }
    }
}
