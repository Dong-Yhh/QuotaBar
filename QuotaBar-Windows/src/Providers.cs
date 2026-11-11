using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace QuotaBar
{
    public class QuotaException : Exception
    {
        public QuotaException(string message) : base(message) { }
    }

    public class HttpResult
    {
        public int Status;
        public string Text = "";
        public object Body;
    }

    public enum EndpointField
    {
        /// <summary>不显示接口地址</summary>
        None,
        /// <summary>只填站点根地址，路径由代码决定</summary>
        BaseURL,
        /// <summary>完整地址 + 字段路径</summary>
        FullURI
    }

    /// <summary>各供应商的展示信息。</summary>
    public static class ProviderCatalog
    {
        public static readonly ProviderKind[] All = new ProviderKind[]
        {
            ProviderKind.OpenRouter,
            ProviderKind.DeepSeek,
            ProviderKind.Moonshot,
            ProviderKind.SiliconFlow,
            ProviderKind.SoloAPI,
            ProviderKind.ClaudeCode,
            ProviderKind.OpenAI,
            ProviderKind.Custom
        };

        public static string DisplayName(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenRouter: return "OpenRouter";
                case ProviderKind.DeepSeek: return "DeepSeek";
                case ProviderKind.Moonshot: return "Kimi (Moonshot)";
                case ProviderKind.SiliconFlow: return "SiliconFlow 硅基流动";
                case ProviderKind.SoloAPI: return "SoloAPI";
                case ProviderKind.ClaudeCode: return "Claude Code 订阅";
                case ProviderKind.OpenAI: return "OpenAI 组织用量";
                default: return "自定义接口";
            }
        }

        public static string Blurb(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenRouter: return "读取账户余额与密钥额度，数据最准确。";
                case ProviderKind.DeepSeek: return "读取 DeepSeek 开放平台账户余额。";
                case ProviderKind.Moonshot: return "读取 Kimi 开放平台账户可用余额。";
                case ProviderKind.SiliconFlow: return "读取硅基流动账户余额与充值余额。";
                case ProviderKind.SoloAPI: return "读取 SoloAPI 中转站余额、今日消费与累计消费。";
                case ProviderKind.ClaudeCode: return "读取本机 Claude Code 登录态，显示 5 小时 / 7 天限额。";
                case ProviderKind.OpenAI: return "用 Admin Key 读组织本月花费（OpenAI 不提供剩余额度接口）。";
                default: return "任意返回 JSON 的接口，自己指定取数字段。";
            }
        }

        public static string CredentialLabel(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.ClaudeCode: return "无需填写";
                case ProviderKind.OpenAI: return "Admin API Key";
                default: return "API Key";
            }
        }

        public static string CredentialHint(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenRouter: return "sk-or-v1-...";
                case ProviderKind.DeepSeek: return "sk-...";
                case ProviderKind.Moonshot: return "sk-...";
                case ProviderKind.SiliconFlow: return "sk-...";
                case ProviderKind.SoloAPI: return "sk-...";
                case ProviderKind.ClaudeCode: return "留空即可";
                case ProviderKind.OpenAI: return "sk-admin-...";
                default: return "按需填写，会以 Bearer 发送";
            }
        }

        public static string HelpUrl(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenRouter: return "https://openrouter.ai/settings/keys";
                case ProviderKind.DeepSeek: return "https://platform.deepseek.com/api_keys";
                case ProviderKind.Moonshot: return "https://platform.moonshot.cn/console/api-keys";
                case ProviderKind.SiliconFlow: return "https://cloud.siliconflow.cn/account/ak";
                case ProviderKind.SoloAPI: return "https://soloapi.cc";
                case ProviderKind.ClaudeCode: return "https://claude.ai/settings/usage";
                case ProviderKind.OpenAI: return "https://platform.openai.com/settings/organization/admin-keys";
                default: return "";
            }
        }

        /// <summary>用于窗口配色的主色。</summary>
        public static string AccentHex(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenRouter: return "#6E7BFF";
                case ProviderKind.DeepSeek: return "#4D6BFE";
                case ProviderKind.Moonshot: return "#8B5CF6";
                case ProviderKind.SiliconFlow: return "#16A34A";
                case ProviderKind.SoloAPI: return "#14B8A6";
                case ProviderKind.ClaudeCode: return "#D97757";
                case ProviderKind.OpenAI: return "#10A37F";
                default: return "#F59E0B";
            }
        }

        /// <summary>编辑器里要显示哪种「接口地址」输入框。</summary>
        public static EndpointField Endpoint(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.Custom: return EndpointField.FullURI;
                case ProviderKind.SoloAPI: return EndpointField.BaseURL;
                default: return EndpointField.None;
            }
        }
    }

    public static class ProviderEngine
    {
        // ---------- HTTP ----------

        public static HttpResult Request(string url, string method, Dictionary<string, string> headers, string body)
        {
            // 老版本 .NET Framework 默认只开 TLS 1.0，这里显式打开 1.1 / 1.2。
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch (Exception) { }

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                throw new QuotaException("接口地址不合法：" + url);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = string.IsNullOrEmpty(method) ? "GET" : method;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.UserAgent = "QuotaBar/1.0 (Windows)";
            request.Accept = "application/json";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            if (headers != null)
            {
                foreach (KeyValuePair<string, string> pair in headers)
                {
                    if (pair.Key == null) continue;
                    if (string.Equals(pair.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        request.ContentType = pair.Value;
                    else if (string.Equals(pair.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                        request.Accept = pair.Value;
                    else
                        request.Headers[pair.Key] = pair.Value;
                }
            }

            if (!string.IsNullOrEmpty(body))
            {
                byte[] payload = Encoding.UTF8.GetBytes(body);
                request.ContentLength = payload.Length;
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(payload, 0, payload.Length);
                }
            }

            HttpResult result = new HttpResult();
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    result.Status = (int)response.StatusCode;
                    result.Text = ReadAll(response);
                }
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response != null)
                {
                    result.Status = (int)response.StatusCode;
                    try { result.Text = ReadAll(response); }
                    catch (Exception) { }
                    response.Close();
                }
                else
                {
                    throw new QuotaException("网络请求失败：" + error.Message);
                }
            }

            if (result.Status < 200 || result.Status >= 300)
            {
                if (result.Status == 401 || result.Status == 403)
                    throw new QuotaException("凭证无效或权限不足（HTTP " + result.Status + "）");
                if (result.Status == 429)
                    throw new QuotaException("请求过于频繁（HTTP 429）");
                throw new QuotaException("HTTP " + result.Status + " · " + Shorten(result.Text));
            }

            try
            {
                result.Body = Json.Parse(result.Text);
            }
            catch (Exception)
            {
                throw new QuotaException("返回内容不是合法 JSON：" + Shorten(result.Text));
            }
            return result;
        }

        private static string ReadAll(HttpWebResponse response)
        {
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null) return "";
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim();
            if (text.Length <= 180) return text;
            return text.Substring(0, 180) + "…";
        }

        private static string Bearer(ProviderConfig config, string what)
        {
            string key = (config.Credential ?? "").Trim();
            if (key.Length == 0) throw new QuotaException("还没有填写凭证：" + what);
            return key;
        }

        private static Dictionary<string, string> Auth(string token)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + token;
            headers["Accept"] = "application/json";
            return headers;
        }

        // ---------- 指标构造 ----------

        public static QuotaMetric MakeBalance(string id, string title, double remaining, string currency,
                                              double? used, double? limit, string footnote)
        {
            QuotaMetric metric = new QuotaMetric();
            metric.Id = id;
            metric.Title = title;
            metric.Unit = QuotaUnit.Currency;
            metric.CurrencyCode = currency;
            metric.Remaining = remaining;
            metric.Used = used;
            metric.Limit = limit;
            metric.Footnote = footnote;
            metric.RemainingPrimary = true;
            return metric;
        }

        public static QuotaMetric MakeQuota(string id, string title, double used, double limit,
                                            DateTime? resetsAt, string footnote)
        {
            QuotaMetric metric = new QuotaMetric();
            metric.Id = id;
            metric.Title = title;
            metric.Unit = QuotaUnit.Percent;
            metric.Used = used;
            metric.Limit = limit;
            metric.Remaining = Math.Max(limit - used, 0);
            metric.ResetsAt = resetsAt;
            metric.Footnote = footnote;
            metric.RemainingPrimary = true;
            return metric;
        }

        public static QuotaMetric MakeUsage(string id, string title, double used, string currency, string footnote)
        {
            QuotaMetric metric = new QuotaMetric();
            metric.Id = id;
            metric.Title = title;
            metric.Unit = QuotaUnit.Currency;
            metric.CurrencyCode = currency;
            metric.Used = used;
            metric.Footnote = footnote;
            metric.RemainingPrimary = false;
            return metric;
        }

        // ---------- 入口 ----------

        public static List<QuotaMetric> Fetch(ProviderConfig config)
        {
            switch (config.Kind)
            {
                case ProviderKind.OpenRouter: return FetchOpenRouter(config);
                case ProviderKind.DeepSeek: return FetchDeepSeek(config);
                case ProviderKind.Moonshot: return FetchMoonshot(config);
                case ProviderKind.SiliconFlow: return FetchSiliconFlow(config);
                case ProviderKind.SoloAPI: return FetchSoloAPI(config);
                case ProviderKind.ClaudeCode: return FetchClaudeCode(config);
                case ProviderKind.OpenAI: return FetchOpenAI(config);
                default: return FetchCustom(config);
            }
        }

        // ---------- OpenRouter ----------

        public static List<QuotaMetric> FetchOpenRouter(ProviderConfig config)
        {
            string token = Bearer(config, "OpenRouter API Key");
            Dictionary<string, string> headers = Auth(token);
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            string firstError = null;

            try
            {
                HttpResult result = Request("https://openrouter.ai/api/v1/credits", "GET", headers, null);
                metrics.AddRange(ParseOpenRouterCredits(result.Body));
            }
            catch (Exception error) { if (firstError == null) firstError = error.Message; }

            try
            {
                HttpResult result = Request("https://openrouter.ai/api/v1/key", "GET", headers, null);
                metrics.AddRange(ParseOpenRouterKey(result.Body));
            }
            catch (Exception error) { if (firstError == null) firstError = error.Message; }

            if (metrics.Count == 0)
                throw new QuotaException(firstError == null ? "接口没有返回可识别的额度字段" : firstError);
            return metrics;
        }

        /// <summary>GET /api/v1/credits → {"data":{"total_credits":10,"total_usage":3.2}}</summary>
        public static List<QuotaMetric> ParseOpenRouterCredits(object body)
        {
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            double? total = Json.Num(body, "data.total_credits");
            double? usage = Json.Num(body, "data.total_usage");
            if (!total.HasValue || !usage.HasValue) return metrics;

            metrics.Add(MakeBalance("account", "账户余额", Math.Max(total.Value - usage.Value, 0), "USD",
                                    usage, total,
                                    "已用 " + QuotaFormat.Money(usage.Value, "USD") +
                                    " / " + QuotaFormat.Money(total.Value, "USD")));
            return metrics;
        }

        /// <summary>GET /api/v1/key → {"data":{"usage":1.5,"limit":10,"limit_remaining":8.5}}</summary>
        public static List<QuotaMetric> ParseOpenRouterKey(object body)
        {
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            double? remaining = Json.Num(body, "data.limit_remaining");
            double? limit = Json.Num(body, "data.limit");
            double? usage = Json.Num(body, "data.usage");

            if (remaining.HasValue && limit.HasValue)
            {
                double used = usage.HasValue ? usage.Value : Math.Max(limit.Value - remaining.Value, 0);
                metrics.Add(MakeBalance("key", "本密钥剩余额度", remaining.Value, "USD", used, limit,
                                        "已用 " + QuotaFormat.Money(used, "USD") +
                                        " / " + QuotaFormat.Money(limit.Value, "USD")));
            }
            else if (usage.HasValue)
            {
                metrics.Add(MakeUsage("key", "本密钥累计用量", usage.Value, "USD", "该密钥未设置额度上限"));
            }
            return metrics;
        }

        // ---------- DeepSeek ----------

        public static List<QuotaMetric> FetchDeepSeek(ProviderConfig config)
        {
            string token = Bearer(config, "DeepSeek API Key");
            HttpResult result = Request("https://api.deepseek.com/user/balance", "GET", Auth(token), null);
            List<QuotaMetric> metrics = ParseDeepSeek(result.Body);
            if (metrics.Count == 0) throw new QuotaException("接口没有返回可识别的余额字段");
            return metrics;
        }

        /// <summary>{"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"110.00",...}]}</summary>
        public static List<QuotaMetric> ParseDeepSeek(object body)
        {
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            List<object> infos = Json.AsArray(Json.Get(body, "balance_infos"));
            if (infos == null) return metrics;

            foreach (object info in infos)
            {
                string currency = Json.Str(info, "currency");
                if (string.IsNullOrEmpty(currency)) currency = "CNY";
                double? total = Json.Num(info, "total_balance");
                if (!total.HasValue) continue;

                double? granted = Json.Num(info, "granted_balance");
                double? topped = Json.Num(info, "topped_up_balance");
                List<string> notes = new List<string>();
                if (topped.HasValue) notes.Add("充值 " + QuotaFormat.Money(topped.Value, currency));
                if (granted.HasValue) notes.Add("赠送 " + QuotaFormat.Money(granted.Value, currency));

                metrics.Add(MakeBalance("balance_" + currency.ToLowerInvariant(),
                                        string.Equals(currency, "CNY", StringComparison.OrdinalIgnoreCase)
                                            ? "账户余额" : "账户余额（" + currency + "）",
                                        total.Value, currency, null, null,
                                        notes.Count == 0 ? null : string.Join(" · ", notes.ToArray())));
            }

            bool? available = Json.Flag(body, "is_available");
            if (available.HasValue && !available.Value)
            {
                metrics.Insert(0, MakeBalance("unavailable", "账户状态", 0, "CNY", null, null,
                                              "账户不可用，可能是余额不足"));
            }
            return metrics;
        }

        // ---------- Moonshot / Kimi ----------

        public static List<QuotaMetric> FetchMoonshot(ProviderConfig config)
        {
            string token = Bearer(config, "Moonshot API Key");
            HttpResult result = Request("https://api.moonshot.cn/v1/users/me/balance", "GET", Auth(token), null);
            List<QuotaMetric> metrics = ParseMoonshot(result.Body);
            if (metrics.Count == 0) throw new QuotaException("接口没有返回可识别的余额字段");
            return metrics;
        }

        /// <summary>{"code":0,"data":{"available_balance":49.58,"voucher_balance":0,"cash_balance":49.58}}</summary>
        public static List<QuotaMetric> ParseMoonshot(object body)
        {
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            double? available = Json.Num(body, "data.available_balance");
            if (!available.HasValue) return metrics;

            double? cash = Json.Num(body, "data.cash_balance");
            double? voucher = Json.Num(body, "data.voucher_balance");
            List<string> notes = new List<string>();
            if (cash.HasValue) notes.Add("现金 " + QuotaFormat.Money(cash.Value, "CNY"));
            if (voucher.HasValue && voucher.Value > 0) notes.Add("代金券 " + QuotaFormat.Money(voucher.Value, "CNY"));

            metrics.Add(MakeBalance("available", "可用余额", available.Value, "CNY", null, null,
                                    notes.Count == 0 ? null : string.Join(" · ", notes.ToArray())));
            return metrics;
        }

        // ---------- SiliconFlow ----------

        public static List<QuotaMetric> FetchSiliconFlow(ProviderConfig config)
        {
            string token = Bearer(config, "SiliconFlow API Key");
            HttpResult result = Request("https://api.siliconflow.cn/v1/user/info", "GET", Auth(token), null);
            List<QuotaMetric> metrics = ParseSiliconFlow(result.Body);
            if (metrics.Count == 0) throw new QuotaException("接口没有返回可识别的余额字段");
            return metrics;
        }

        /// <summary>{"data":{"balance":"0.88","chargeBalance":"88.00","totalBalance":"88.00"}}</summary>
        public static List<QuotaMetric> ParseSiliconFlow(object body)
        {
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            double? balance = Json.Num(body, "data.balance");
            if (!balance.HasValue) return metrics;

            double? charge = Json.Num(body, "data.chargeBalance");
            double? total = Json.Num(body, "data.totalBalance");
            List<string> notes = new List<string>();
            if (charge.HasValue && charge.Value > 0) notes.Add("充值余额 " + QuotaFormat.Money(charge.Value, "CNY"));
            if (total.HasValue) notes.Add("总额 " + QuotaFormat.Money(total.Value, "CNY"));

            metrics.Add(MakeBalance("balance", "可用余额", balance.Value, "CNY", null, null,
                                    notes.Count == 0 ? null : string.Join(" · ", notes.ToArray())));
            return metrics;
        }

        // ---------- SoloAPI 中转站 ----------

        /// <summary>
        /// SoloAPI（api.soloapi.cc）。接口是实测确认的，不是 one-api / new-api 那套约定：
        ///
        ///     GET {base}/v1/usage
        ///     Authorization: Bearer sk-...
        ///     → {"balance":10,"remaining":10,"unit":"USD","isValid":true,"planName":"钱包余额",
        ///        "usage":{"today":{"cost":0,"requests":0,"total_tokens":0},"total":{"cost":0}}}
        ///
        /// 鉴权失败返回 401 + {"code":"INVALID_API_KEY","message":"API Key 无效…"}，
        /// Request() 会把这个 message 透出来。
        /// 另有只返回余额的 {base}/v1/user/balance 作为兜底。
        /// </summary>
        public static List<QuotaMetric> FetchSoloAPI(ProviderConfig config)
        {
            string token = Bearer(config, "SoloAPI API Key");
            string baseUrl = SoloAPIBaseUrl(config);
            Dictionary<string, string> headers = Auth(token);

            List<QuotaMetric> metrics = new List<QuotaMetric>();
            string firstError = null;

            try
            {
                HttpResult result = Request(baseUrl + "/v1/usage", "GET", headers, null);
                metrics.AddRange(ParseSoloAPIUsage(result.Body));
            }
            catch (Exception error) { if (firstError == null) firstError = error.Message; }

            if (metrics.Count == 0)
            {
                try
                {
                    HttpResult result = Request(baseUrl + "/v1/user/balance", "GET", headers, null);
                    metrics.AddRange(ParseSoloAPIBalance(result.Body));
                }
                catch (Exception error) { if (firstError == null) firstError = error.Message; }
            }

            if (metrics.Count == 0)
                throw new QuotaException(firstError == null ? "接口没有返回可识别的余额字段" : firstError);
            return metrics;
        }

        /// <summary>允许填镜像域名；填了 /v1 或结尾斜杠也能自动纠正。</summary>
        public static string SoloAPIBaseUrl(ProviderConfig config)
        {
            string value = config.Option("baseURL").Trim();
            if (value.Length == 0) value = "https://api.soloapi.cc";
            while (value.EndsWith("/")) value = value.Substring(0, value.Length - 1);
            if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 3);
            return value;
        }

        public static List<QuotaMetric> ParseSoloAPIUsage(object body)
        {
            List<QuotaMetric> metrics = new List<QuotaMetric>();

            double? balance = Json.Num(body, "balance");
            if (!balance.HasValue) balance = Json.Num(body, "remaining");
            if (!balance.HasValue) return metrics;

            string unit = Json.Str(body, "unit");
            if (string.IsNullOrEmpty(unit)) unit = "USD";
            unit = unit.ToUpperInvariant();

            bool? valid = Json.Flag(body, "isValid");
            if (valid.HasValue && !valid.Value)
            {
                metrics.Add(MakeBalance("invalid", "账户状态", 0, unit, null, null,
                                        "API Key 已失效或账户不可用"));
            }

            string plan = Json.Str(body, "planName");
            if (plan != null) plan = plan.Trim();

            metrics.Add(MakeBalance("balance", "账户余额", balance.Value, unit, null, null,
                                    string.IsNullOrEmpty(plan) ? null : plan));

            // 今日消费只在真有请求时显示，免得没用过的账号挂一排 0
            double? todayRequests = Json.Num(body, "usage.today.requests");
            double? todayCost = Json.Num(body, "usage.today.cost");
            if (todayCost.HasValue && todayRequests.HasValue && todayRequests.Value > 0)
            {
                List<string> notes = new List<string>();
                notes.Add(QuotaFormat.Plain(todayRequests.Value) + " 次请求");
                double? tokens = Json.Num(body, "usage.today.total_tokens");
                if (tokens.HasValue && tokens.Value > 0)
                    notes.Add(QuotaFormat.Plain(tokens.Value) + " tokens");
                metrics.Add(MakeUsage("today", "今日消费", todayCost.Value, unit,
                                      string.Join(" · ", notes.ToArray())));
            }

            double? totalCost = Json.Num(body, "usage.total.cost");
            if (totalCost.HasValue && totalCost.Value > 0)
            {
                double? totalRequests = Json.Num(body, "usage.total.requests");
                string note = (totalRequests.HasValue && totalRequests.Value > 0)
                    ? QuotaFormat.Plain(totalRequests.Value) + " 次请求"
                    : null;
                metrics.Add(MakeUsage("total", "累计消费", totalCost.Value, unit, note));
            }

            return metrics;
        }

        /// <summary>兜底接口 GET {base}/v1/user/balance → {"is_active":true,"balance":10,"unit":"USD"}</summary>
        public static List<QuotaMetric> ParseSoloAPIBalance(object body)
        {
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            double? balance = Json.Num(body, "balance");
            if (!balance.HasValue) return metrics;

            string unit = Json.Str(body, "unit");
            if (string.IsNullOrEmpty(unit)) unit = "USD";
            unit = unit.ToUpperInvariant();

            bool? active = Json.Flag(body, "is_active");
            if (active.HasValue && !active.Value)
            {
                metrics.Add(MakeBalance("invalid", "账户状态", 0, unit, null, null,
                                        "API Key 已失效或账户不可用"));
            }
            metrics.Add(MakeBalance("balance", "账户余额", balance.Value, unit, null, null, null));
            return metrics;
        }

        // ---------- Claude Code 订阅 ----------

        private const string ClaudeClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        private const string ClaudeUsageUrl = "https://api.anthropic.com/api/oauth/usage";
        private const string ClaudeTokenUrl = "https://console.anthropic.com/v1/oauth/token";

        public static string ClaudeCredentialsPath
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(Path.Combine(home, ".claude"), ".credentials.json");
            }
        }

        public static List<QuotaMetric> FetchClaudeCode(ProviderConfig config)
        {
            string path = ClaudeCredentialsPath;
            if (!File.Exists(path))
            {
                throw new QuotaException(
                    "找不到 Claude Code 登录态：" + path +
                    "。请先在终端运行一次 claude 并完成登录。");
            }

            object root = Json.Parse(File.ReadAllText(path, Encoding.UTF8));
            string token = Json.Str(root, "claudeAiOauth.accessToken");
            if (string.IsNullOrEmpty(token))
                throw new QuotaException("登录态文件里没有 accessToken，请重新登录 Claude Code。");

            DateTime? expiresAt = Json.Time(root, "claudeAiOauth.expiresAt");
            if (expiresAt.HasValue && expiresAt.Value <= DateTime.Now.AddSeconds(120))
            {
                token = RefreshClaudeToken(root, token, path);
            }

            List<QuotaMetric> metrics;
            try
            {
                metrics = ParseClaudeUsage(FetchClaudeUsage(token));
            }
            catch (QuotaException error)
            {
                if (error.Message.IndexOf("401", StringComparison.Ordinal) < 0 &&
                    error.Message.IndexOf("403", StringComparison.Ordinal) < 0)
                    throw;
                token = RefreshClaudeToken(root, token, path);
                metrics = ParseClaudeUsage(FetchClaudeUsage(token));
            }

            if (metrics.Count == 0) throw new QuotaException("usage 接口没有返回可识别的限额字段");
            return metrics;
        }

        private static object FetchClaudeUsage(string token)
        {
            Dictionary<string, string> headers = Auth(token);
            headers["anthropic-beta"] = "oauth-2025-04-20";
            HttpResult result = Request(ClaudeUsageUrl, "GET", headers, null);
            return result.Body;
        }

        private static string RefreshClaudeToken(object root, string currentToken, string path)
        {
            string refresh = Json.Str(root, "claudeAiOauth.refreshToken");
            if (string.IsNullOrEmpty(refresh))
                throw new QuotaException("登录态已过期，且没有 refresh token，请重新登录 Claude Code。");

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["grant_type"] = "refresh_token";
            payload["refresh_token"] = refresh;
            payload["client_id"] = ClaudeClientId;

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["Content-Type"] = "application/json";
            headers["Accept"] = "application/json";

            HttpResult result = Request(ClaudeTokenUrl, "POST", headers, Json.Dump(payload));
            string access = Json.Str(result.Body, "access_token");
            if (string.IsNullOrEmpty(access))
                throw new QuotaException("刷新 Claude 登录态失败，请打开 Claude Code 重新登录。");

            // Anthropic 的 refresh token 是轮换的：必须把新的写回去，
            // 否则 Claude Code 自己的登录态会被搞坏。
            try
            {
                Dictionary<string, object> oauth = Json.AsObject(Json.Get(root, "claudeAiOauth"));
                if (oauth == null) oauth = new Dictionary<string, object>();
                oauth["accessToken"] = access;
                string newRefresh = Json.Str(result.Body, "refresh_token");
                if (!string.IsNullOrEmpty(newRefresh)) oauth["refreshToken"] = newRefresh;
                double? expiresIn = Json.Num(result.Body, "expires_in");
                if (expiresIn.HasValue)
                    oauth["expiresAt"] = (DateTime.UtcNow.AddSeconds(expiresIn.Value - 60)
                                          - Json.Epoch).TotalMilliseconds;

                Dictionary<string, object> updated = Json.AsObject(root);
                if (updated == null) updated = new Dictionary<string, object>();
                updated["claudeAiOauth"] = oauth;
                File.WriteAllText(path, Json.Dump(updated), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 回写失败不影响本次读取。
            }
            return access;
        }

        /// <summary>
        /// GET /api/oauth/usage → {"five_hour":{"utilization":79,"resets_at":"..."},
        /// "seven_day":{...},"extra_usage":{"is_enabled":true,"monthly_limit":1000,"used_credits":250}}
        /// utilization / monthly_limit / used_credits 都是「已用」口径，金额单位是美分。
        /// </summary>
        public static List<QuotaMetric> ParseClaudeUsage(object body)
        {
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            string[] ids = new string[] { "five_hour", "seven_day", "seven_day_opus", "seven_day_sonnet" };
            string[] titles = new string[] { "5 小时窗口", "7 天窗口", "Opus 周限额", "Sonnet 周限额" };

            for (int i = 0; i < ids.Length; i++)
            {
                double? utilization = Json.Num(body, ids[i] + ".utilization");
                if (!utilization.HasValue) continue;
                DateTime? resetsAt = Json.Time(body, ids[i] + ".resets_at");
                metrics.Add(MakeQuota(ids[i], titles[i], utilization.Value, 100, resetsAt,
                                      "已用 " + QuotaFormat.Plain(utilization.Value) + "%"));
            }

            bool? extraEnabled = Json.Flag(body, "extra_usage.is_enabled");
            double? usedCredits = Json.Num(body, "extra_usage.used_credits");
            if (extraEnabled.HasValue && extraEnabled.Value && usedCredits.HasValue)
            {
                double used = usedCredits.Value / 100.0;
                double? limitCents = Json.Num(body, "extra_usage.monthly_limit");
                if (limitCents.HasValue && limitCents.Value > 0)
                {
                    double limit = limitCents.Value / 100.0;
                    metrics.Add(MakeBalance("extra_usage", "额外用量额度",
                                            Math.Max(limit - used, 0), "USD", used, limit,
                                            "已用 " + QuotaFormat.Money(used, "USD") +
                                            " / " + QuotaFormat.Money(limit, "USD")));
                }
                else
                {
                    metrics.Add(MakeUsage("extra_usage", "额外用量已用", used, "USD", null));
                }
            }
            return metrics;
        }

        // ---------- OpenAI ----------

        public static List<QuotaMetric> FetchOpenAI(ProviderConfig config)
        {
            string token = Bearer(config, "OpenAI Admin API Key");
            Dictionary<string, string> headers = Auth(token);

            DateTime monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            long startUnix = (long)(monthStart - Json.Epoch).TotalSeconds;

            List<QuotaMetric> metrics = new List<QuotaMetric>();
            string firstError = null;

            string costUrl = "https://api.openai.com/v1/organization/costs?start_time=" + startUnix +
                             "&bucket_width=1d&limit=31";
            try
            {
                HttpResult result = Request(costUrl, "GET", headers, null);
                double? cost = ParseOpenAIMonthCost(result.Body);
                if (cost.HasValue)
                {
                    metrics.Add(MakeUsage("month_cost", "本月已用", cost.Value, "USD",
                                          "统计自 " + monthStart.ToLocalTime().ToString("M 月 d 日") +
                                          "（OpenAI 未提供剩余额度接口）"));
                }
            }
            catch (Exception error) { if (firstError == null) firstError = error.Message; }

            string usageUrl = "https://api.openai.com/v1/organization/usage/completions?start_time=" + startUnix +
                              "&bucket_width=1d&limit=31";
            try
            {
                HttpResult result = Request(usageUrl, "GET", headers, null);
                double tokens = ParseOpenAITotalTokens(result.Body);
                if (tokens > 0)
                {
                    QuotaMetric metric = new QuotaMetric();
                    metric.Id = "month_tokens";
                    metric.Title = "本月 Token 用量";
                    metric.Unit = QuotaUnit.Tokens;
                    metric.Used = tokens;
                    metric.Footnote = "输入 + 输出合计";
                    metric.RemainingPrimary = false;
                    metrics.Add(metric);
                }
            }
            catch (Exception error) { if (firstError == null) firstError = error.Message; }

            if (metrics.Count == 0)
                throw new QuotaException(firstError == null ? "接口没有返回可识别的用量字段" : firstError);
            return metrics;
        }

        /// <summary>成本接口是嵌套分桶：data[].results[].amount.value</summary>
        public static double? ParseOpenAIMonthCost(object body)
        {
            List<object> buckets = Json.AsArray(Json.Get(body, "data"));
            if (buckets == null) return null;
            double total = 0;
            bool found = false;
            foreach (object bucket in buckets)
            {
                List<object> results = Json.AsArray(Json.Get(bucket, "results"));
                if (results == null) continue;
                foreach (object item in results)
                {
                    double? value = Json.Num(item, "amount.value");
                    if (value.HasValue) { total += value.Value; found = true; }
                }
            }
            return found ? (double?)total : null;
        }

        public static double ParseOpenAITotalTokens(object body)
        {
            return SumField(body, "input_tokens") + SumField(body, "output_tokens");
        }

        private static double SumField(object node, string key)
        {
            double total = 0;
            Dictionary<string, object> map = node as Dictionary<string, object>;
            if (map != null)
            {
                object value;
                if (map.TryGetValue(key, out value))
                {
                    if (value is double) total += (double)value;
                }
                foreach (KeyValuePair<string, object> pair in map) total += SumField(pair.Value, key);
                return total;
            }
            List<object> list = node as List<object>;
            if (list != null)
            {
                foreach (object item in list) total += SumField(item, key);
            }
            return total;
        }

        // ---------- 自定义接口 ----------

        public static List<QuotaMetric> FetchCustom(ProviderConfig config)
        {
            string endpoint = config.Option("endpoint").Trim();
            if (endpoint.Length == 0) throw new QuotaException("还没有填写凭证：接口地址");

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["Accept"] = "application/json";

            string rawHeaders = config.Option("headersJSON");
            if (rawHeaders.Trim().Length > 0)
            {
                try
                {
                    Dictionary<string, object> parsed = Json.AsObject(Json.Parse(rawHeaders));
                    if (parsed != null)
                    {
                        foreach (KeyValuePair<string, object> pair in parsed)
                        {
                            headers[pair.Key] = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                        }
                    }
                }
                catch (Exception)
                {
                    throw new QuotaException("额外请求头不是合法 JSON。");
                }
            }

            string credential = (config.Credential ?? "").Trim();
            if (credential.Length > 0) headers["Authorization"] = "Bearer " + credential;

            HttpResult result = Request(endpoint, "GET", headers, null);
            List<QuotaMetric> metrics = new List<QuotaMetric>();
            metrics.Add(ParseCustom(result.Body, config));
            return metrics;
        }

        public static QuotaMetric ParseCustom(object body, ProviderConfig config)
        {
            QuotaUnit unit = QuotaUnit.Currency;
            string unitText = config.Option("unit");
            if (!string.IsNullOrEmpty(unitText))
            {
                try { unit = (QuotaUnit)Enum.Parse(typeof(QuotaUnit), unitText, true); }
                catch (Exception) { unit = QuotaUnit.Currency; }
            }

            string currency = config.Option("currency");
            if (string.IsNullOrEmpty(currency)) currency = "CNY";

            string title = config.Option("label").Trim();
            if (title.Length == 0) title = unit == QuotaUnit.Currency ? "账户余额" : unit.ToString();

            string remainingPath = config.Option("remainingPath").Trim();
            string usedPath = config.Option("usedPath").Trim();
            string limitPath = config.Option("limitPath").Trim();

            double? remaining = remainingPath.Length > 0 ? Json.Num(body, remainingPath) : null;
            double? used = usedPath.Length > 0 ? Json.Num(body, usedPath) : null;
            double? limit = limitPath.Length > 0 ? Json.Num(body, limitPath) : null;

            if (!remaining.HasValue && !(used.HasValue && limit.HasValue) && !used.HasValue)
                throw new QuotaException("接口返回里没有找到需要的数据，请检查字段路径");

            List<string> notes = new List<string>();
            if (used.HasValue) notes.Add("已用 " + QuotaFormat.Value(used.Value, unit, currency));
            if (limit.HasValue) notes.Add("上限 " + QuotaFormat.Value(limit.Value, unit, currency));

            QuotaMetric metric = new QuotaMetric();
            metric.Id = "custom";
            metric.Title = title;
            metric.Unit = unit;
            metric.CurrencyCode = unit == QuotaUnit.Currency ? currency : null;
            metric.Used = used;
            metric.Limit = limit;
            metric.Remaining = remaining;
            metric.Footnote = notes.Count == 0 ? null : string.Join(" · ", notes.ToArray());
            metric.RemainingPrimary = true;
            return metric;
        }
    }
}
