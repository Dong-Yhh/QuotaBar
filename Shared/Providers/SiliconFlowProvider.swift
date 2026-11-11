import Foundation

struct SiliconFlowProvider: QuotaProvider {
    let kind: ProviderKind = .siliconFlow

    func fetch(config: ProviderConfig) async throws -> [QuotaMetric] {
        let key = config.credential.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { throw QuotaError.missingCredential("SiliconFlow API Key") }

        let response = try await HTTPClient.json(
            "https://api.siliconflow.cn/v1/user/info",
            headers: ["Authorization": "Bearer \(key)", "Accept": "application/json"]
        )
        guard let object = response.object else { throw QuotaError.noValue }

        let metrics = Self.metrics(from: object)
        guard !metrics.isEmpty else { throw QuotaError.noValue }
        return metrics
    }

    /// GET /v1/user/info
    /// {"code": 20000, "status": true,
    ///  "data": {"balance": "0.88", "chargeBalance": "88.00", "totalBalance": "88.00"}}
    static func metrics(from object: Any) -> [QuotaMetric] {
        guard let balance = JSONPath.double(in: object, path: "data.balance") else { return [] }
        let charge = JSONPath.double(in: object, path: "data.chargeBalance")
        let total = JSONPath.double(in: object, path: "data.totalBalance")

        var footnote: [String] = []
        if let charge, charge > 0 { footnote.append("充值余额 \(QuotaFormatter.money(charge, code: "CNY"))") }
        if let total { footnote.append("总额 \(QuotaFormatter.money(total, code: "CNY"))") }
        return [.balance(
            id: "balance",
            title: "可用余额",
            remaining: balance,
            currency: "CNY",
            footnote: footnote.isEmpty ? nil : footnote.joined(separator: " · ")
        )]
    }
}
