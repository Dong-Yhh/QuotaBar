import Foundation

struct MoonshotProvider: QuotaProvider {
    let kind: ProviderKind = .moonshot

    func fetch(config: ProviderConfig) async throws -> [QuotaMetric] {
        let key = config.credential.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { throw QuotaError.missingCredential("Moonshot API Key") }

        let response = try await HTTPClient.json(
            "https://api.moonshot.cn/v1/users/me/balance",
            headers: ["Authorization": "Bearer \(key)", "Accept": "application/json"]
        )
        guard let object = response.object else { throw QuotaError.noValue }

        let metrics = Self.metrics(from: object)
        guard !metrics.isEmpty else { throw QuotaError.noValue }
        return metrics
    }

    /// GET /v1/users/me/balance
    /// {"code": 0, "data": {"available_balance": 49.58, "voucher_balance": 0, "cash_balance": 49.58}}
    static func metrics(from object: Any) -> [QuotaMetric] {
        var metrics: [QuotaMetric] = []
        if let available = JSONPath.double(in: object, path: "data.available_balance") {
            let cash = JSONPath.double(in: object, path: "data.cash_balance")
            let voucher = JSONPath.double(in: object, path: "data.voucher_balance")
            var footnote: [String] = []
            if let cash { footnote.append("现金 \(QuotaFormatter.money(cash, code: "CNY"))") }
            if let voucher, voucher > 0 { footnote.append("代金券 \(QuotaFormatter.money(voucher, code: "CNY"))") }
            metrics.append(.balance(
                id: "available",
                title: "可用余额",
                remaining: available,
                currency: "CNY",
                footnote: footnote.isEmpty ? nil : footnote.joined(separator: " · ")
            ))
        }
        return metrics
    }
}
