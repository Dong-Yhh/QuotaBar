import Foundation

/// OpenAI 没有「剩余额度」接口（ChatGPT 订阅额度也不对外开放），
/// 所以这里用 Admin Key 读取组织本月花费，是官方接口里最接近的一个。
struct OpenAIProvider: QuotaProvider {
    let kind: ProviderKind = .openAI

    func fetch(config: ProviderConfig) async throws -> [QuotaMetric] {
        let key = config.credential.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { throw QuotaError.missingCredential("OpenAI Admin API Key") }
        let headers = ["Authorization": "Bearer \(key)", "Accept": "application/json"]

        let startOfMonth = Self.startOfMonthUnix()
        let costURL = "https://api.openai.com/v1/organization/costs?start_time=\(startOfMonth)&bucket_width=1d&limit=31"

        var metrics: [QuotaMetric] = []
        var failures: [Error] = []

        do {
            let response = try await HTTPClient.json(costURL, headers: headers)
            if let object = response.object, let total = Self.monthCost(from: object) {
                metrics.append(.usageOnly(
                    id: "month_cost",
                    title: "本月已用",
                    used: total,
                    currency: "USD",
                    footnote: "统计自 \(Self.monthStartDescription())（OpenAI 未提供剩余额度接口）"
                ))
            }
        } catch {
            failures.append(error)
        }

        let usageURL = "https://api.openai.com/v1/organization/usage/completions?start_time=\(startOfMonth)&bucket_width=1d&limit=31"
        do {
            let response = try await HTTPClient.json(usageURL, headers: headers)
            if let object = response.object {
                let tokens = Self.totalTokens(in: object)
                if tokens > 0 {
                    metrics.append(QuotaMetric(
                        id: "month_tokens",
                        title: "本月 Token 用量",
                        unit: .tokens,
                        used: tokens,
                        footnote: "输入 + 输出合计"
                    ))
                }
            }
        } catch {
            failures.append(error)
        }

        guard !metrics.isEmpty else {
            if let first = failures.first { throw first }
            throw QuotaError.noValue
        }
        return metrics
    }

    private static func startOfMonthUnix() -> Int {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC") ?? .current
        let components = calendar.dateComponents([.year, .month], from: Date())
        let date = calendar.date(from: components) ?? Date()
        return Int(date.timeIntervalSince1970)
    }

    private static func monthStartDescription() -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "zh_CN")
        formatter.dateFormat = "M 月 d 日"
        return formatter.string(from: Date(timeIntervalSince1970: TimeInterval(startOfMonthUnix())))
    }

    /// 成本接口是嵌套分桶结构：data[].results[].amount.value
    static func monthCost(from object: Any) -> Double? {
        guard let buckets = JSONPath.value(in: object, path: "data") as? [Any] else { return nil }
        var total = 0.0
        var found = false
        for bucket in buckets {
            guard let results = JSONPath.value(in: bucket, path: "results") as? [Any] else { continue }
            for result in results {
                if let value = JSONPath.double(in: result, path: "amount.value") {
                    total += value
                    found = true
                }
            }
        }
        return found ? total : nil
    }

    static func totalTokens(in object: Any) -> Double {
        sum(object, key: "input_tokens") + sum(object, key: "output_tokens")
    }

    /// 用量接口里按字段名递归求和。
    static func sum(_ object: Any, key: String) -> Double {
        var total = 0.0
        func walk(_ node: Any) {
            if let dictionary = node as? [String: Any] {
                if let value = dictionary[key] as? NSNumber { total += value.doubleValue }
                for value in dictionary.values { walk(value) }
            } else if let array = node as? [Any] {
                for value in array { walk(value) }
            }
        }
        walk(object)
        return total
    }
}
