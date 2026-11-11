import Foundation

struct OpenRouterProvider: QuotaProvider {
    let kind: ProviderKind = .openRouter

    func fetch(config: ProviderConfig) async throws -> [QuotaMetric] {
        let key = config.credential.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { throw QuotaError.missingCredential("OpenRouter API Key") }
        let headers = ["Authorization": "Bearer \(key)", "Accept": "application/json"]

        var metrics: [QuotaMetric] = []
        var failures: [Error] = []

        // 账户总额度 / 已用
        do {
            let response = try await HTTPClient.json("https://openrouter.ai/api/v1/credits", headers: headers)
            if let object = response.object { metrics += Self.metrics(fromCredits: object) }
        } catch {
            failures.append(error)
        }

        // 单个密钥的额度
        do {
            let response = try await HTTPClient.json("https://openrouter.ai/api/v1/key", headers: headers)
            if let object = response.object { metrics += Self.metrics(fromKey: object) }
        } catch {
            failures.append(error)
        }

        guard !metrics.isEmpty else {
            if let first = failures.first { throw first }
            throw QuotaError.noValue
        }
        return metrics
    }

    /// GET /api/v1/credits → {"data": {"total_credits": 10, "total_usage": 3.2}}
    static func metrics(fromCredits object: Any) -> [QuotaMetric] {
        guard let total = JSONPath.double(in: object, path: "data.total_credits"),
              let usage = JSONPath.double(in: object, path: "data.total_usage") else {
            return []
        }
        return [.balance(
            id: "account",
            title: "账户余额",
            remaining: max(total - usage, 0),
            currency: "USD",
            used: usage,
            limit: total,
            footnote: "已用 \(QuotaFormatter.money(usage, code: "USD")) / \(QuotaFormatter.money(total, code: "USD"))"
        )]
    }

    /// GET /api/v1/key → {"data": {"usage": 1.5, "limit": 10, "limit_remaining": 8.5}}
    static func metrics(fromKey object: Any) -> [QuotaMetric] {
        if let remaining = JSONPath.double(in: object, path: "data.limit_remaining"),
           let limit = JSONPath.double(in: object, path: "data.limit") {
            let used = JSONPath.double(in: object, path: "data.usage") ?? max(limit - remaining, 0)
            return [.balance(
                id: "key",
                title: "本密钥剩余额度",
                remaining: remaining,
                currency: "USD",
                used: used,
                limit: limit,
                footnote: "已用 \(QuotaFormatter.money(used, code: "USD")) / \(QuotaFormatter.money(limit, code: "USD"))"
            )]
        }
        if let used = JSONPath.double(in: object, path: "data.usage") {
            return [.usageOnly(
                id: "key",
                title: "本密钥累计用量",
                used: used,
                currency: "USD",
                footnote: "该密钥未设置额度上限"
            )]
        }
        return []
    }
}
