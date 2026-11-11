import Foundation

struct DeepSeekProvider: QuotaProvider {
    let kind: ProviderKind = .deepSeek

    func fetch(config: ProviderConfig) async throws -> [QuotaMetric] {
        let key = config.credential.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { throw QuotaError.missingCredential("DeepSeek API Key") }

        let response = try await HTTPClient.json(
            "https://api.deepseek.com/user/balance",
            headers: ["Authorization": "Bearer \(key)", "Accept": "application/json"]
        )
        guard let object = response.object else { throw QuotaError.noValue }

        let metrics = Self.metrics(from: object)
        guard !metrics.isEmpty else { throw QuotaError.noValue }

        let available = JSONPath.value(in: object, path: "is_available") as? Bool ?? true
        guard !available else { return metrics }
        return [.balance(id: "unavailable", title: "账户状态", remaining: 0, currency: "CNY", footnote: "账户不可用，可能是余额不足")] + metrics
    }

    /// GET /user/balance
    /// {"is_available": true, "balance_infos": [{"currency":"CNY","total_balance":"110.00",
    ///  "granted_balance":"10.00","topped_up_balance":"100.00"}]}
    static func metrics(from object: Any) -> [QuotaMetric] {
        guard let infos = JSONPath.value(in: object, path: "balance_infos") as? [Any] else {
            return []
        }

        var metrics: [QuotaMetric] = []
        for info in infos {
            let currency = JSONPath.string(in: info, path: "currency") ?? "CNY"
            guard let total = JSONPath.double(in: info, path: "total_balance") else { continue }
            let granted = JSONPath.double(in: info, path: "granted_balance")
            let topped = JSONPath.double(in: info, path: "topped_up_balance")
            var footnote: [String] = []
            if let topped { footnote.append("充值 \(QuotaFormatter.money(topped, code: currency))") }
            if let granted { footnote.append("赠送 \(QuotaFormatter.money(granted, code: currency))") }
            metrics.append(.balance(
                id: "balance_\(currency.lowercased())",
                title: currency.uppercased() == "CNY" ? "账户余额" : "账户余额（\(currency)）",
                remaining: total,
                currency: currency,
                used: nil,
                limit: nil,
                footnote: footnote.isEmpty ? nil : footnote.joined(separator: " · ")
            ))
        }
        return metrics
    }
}
