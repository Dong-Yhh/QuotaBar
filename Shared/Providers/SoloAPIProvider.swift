import Foundation

/// SoloAPI 中转站（api.soloapi.cc）。
///
/// 接口是实测确认的，不是照搬 one-api / new-api 的约定：
///
///     GET {base}/v1/usage
///     Authorization: Bearer sk-...
///     → {"balance":10,"remaining":10,"unit":"USD","isValid":true,"planName":"钱包余额",
///        "usage":{"today":{"cost":0,"requests":0,"total_tokens":0,...},
///                 "total":{"cost":0,...}}}
///
/// 鉴权失败时返回 401 + {"code":"INVALID_API_KEY","message":"API Key 无效…"}，
/// HTTPClient 会把这个 message 透出来。
///
/// 另有一个只返回余额的简单接口 `{base}/v1/user/balance`，作为兜底。
struct SoloAPIProvider: QuotaProvider {
    let kind: ProviderKind = .soloAPI

    static let defaultBaseURL = "https://api.soloapi.cc"

    func fetch(config: ProviderConfig) async throws -> [QuotaMetric] {
        let key = config.credential.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { throw QuotaError.missingCredential("SoloAPI API Key") }

        let base = Self.baseURL(from: config)
        let headers = ["Authorization": "Bearer \(key)", "Accept": "application/json"]
        var failures: [Error] = []

        // 主接口：余额 + 用量明细
        do {
            let response = try await HTTPClient.json("\(base)/v1/usage", headers: headers)
            if let object = response.object {
                let metrics = Self.metrics(fromUsage: object)
                if !metrics.isEmpty { return metrics }
            }
        } catch {
            failures.append(error)
        }

        // 兜底：只有余额
        do {
            let response = try await HTTPClient.json("\(base)/v1/user/balance", headers: headers)
            if let object = response.object {
                let metrics = Self.metrics(fromBalance: object)
                if !metrics.isEmpty { return metrics }
            }
        } catch {
            failures.append(error)
        }

        if let first = failures.first { throw first }
        throw QuotaError.noValue
    }

    /// 允许在这里填镜像域名；填了 `/v1` 或结尾斜杠也能自动纠正。
    static func baseURL(from config: ProviderConfig) -> String {
        var base = (config.options[OptionKey.baseURL] ?? "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if base.isEmpty { base = defaultBaseURL }
        while base.hasSuffix("/") { base.removeLast() }
        if base.lowercased().hasSuffix("/v1") { base.removeLast(3) }
        return base
    }

    // MARK: - GET /v1/usage

    static func metrics(fromUsage object: Any) -> [QuotaMetric] {
        guard let balance = JSONPath.double(in: object, path: "balance")
                ?? JSONPath.double(in: object, path: "remaining") else {
            return []
        }

        let unit = (JSONPath.string(in: object, path: "unit") ?? "USD").uppercased()
        let planName = JSONPath.string(in: object, path: "planName")?.trimmingCharacters(in: .whitespaces)
        let isValid = JSONPath.value(in: object, path: "isValid") as? Bool ?? true

        var metrics: [QuotaMetric] = []

        if !isValid {
            metrics.append(.balance(
                id: "invalid",
                title: "账户状态",
                remaining: 0,
                currency: unit,
                footnote: "API Key 已失效或账户不可用"
            ))
        }

        metrics.append(.balance(
            id: "balance",
            title: "账户余额",
            remaining: balance,
            currency: unit,
            footnote: (planName?.isEmpty == false) ? planName : nil
        ))

        // 今日消费只在真有请求时显示，免得没用过的账号挂一排 0
        let todayRequests = JSONPath.double(in: object, path: "usage.today.requests")
        if let todayCost = JSONPath.double(in: object, path: "usage.today.cost"),
           (todayRequests ?? 0) > 0 {
            var notes: [String] = []
            if let todayRequests {
                notes.append("\(QuotaFormatter.plain(todayRequests)) 次请求")
            }
            if let tokens = JSONPath.double(in: object, path: "usage.today.total_tokens"), tokens > 0 {
                notes.append("\(QuotaFormatter.plain(tokens, compact: true)) tokens")
            }
            metrics.append(.usageOnly(
                id: "today",
                title: "今日消费",
                used: todayCost,
                currency: unit,
                footnote: notes.isEmpty ? nil : notes.joined(separator: " · ")
            ))
        }

        if let totalCost = JSONPath.double(in: object, path: "usage.total.cost"), totalCost > 0 {
            if let requests = JSONPath.double(in: object, path: "usage.total.requests"), requests > 0 {
                metrics.append(.usageOnly(
                    id: "total",
                    title: "累计消费",
                    used: totalCost,
                    currency: unit,
                    footnote: "\(QuotaFormatter.plain(requests)) 次请求"
                ))
            } else {
                metrics.append(.usageOnly(id: "total", title: "累计消费", used: totalCost, currency: unit))
            }
        }

        return metrics
    }

    // MARK: - GET /v1/user/balance（兜底）

    static func metrics(fromBalance object: Any) -> [QuotaMetric] {
        guard let balance = JSONPath.double(in: object, path: "balance") else { return [] }
        let unit = (JSONPath.string(in: object, path: "unit") ?? "USD").uppercased()
        let isActive = JSONPath.value(in: object, path: "is_active") as? Bool ?? true

        var metrics: [QuotaMetric] = []
        if !isActive {
            metrics.append(.balance(
                id: "invalid",
                title: "账户状态",
                remaining: 0,
                currency: unit,
                footnote: "API Key 已失效或账户不可用"
            ))
        }
        metrics.append(.balance(id: "balance", title: "账户余额", remaining: balance, currency: unit))
        return metrics
    }
}
