import Foundation

/// 任意返回 JSON 的余额接口：自己填地址、请求头和取值路径。
struct CustomProvider: QuotaProvider {
    let kind: ProviderKind = .custom

    func fetch(config: ProviderConfig) async throws -> [QuotaMetric] {
        let endpoint = config.endpoint.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !endpoint.isEmpty else { throw QuotaError.missingCredential("接口地址") }

        var headers: [String: String] = ["Accept": "application/json"]
        if let raw = config.options[OptionKey.headersJSON],
           let data = raw.data(using: .utf8),
           let parsed = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] {
            for (key, value) in parsed {
                headers[key] = String(describing: value)
            }
        }
        let credential = config.credential.trimmingCharacters(in: .whitespacesAndNewlines)
        if !credential.isEmpty {
            headers["Authorization"] = "Bearer \(credential)"
        }

        let response = try await HTTPClient.json(endpoint, headers: headers)
        guard let object = response.object else { throw QuotaError.noValue }
        return [try Self.metric(from: object, config: config)]
    }

    /// 按用户配置的字段路径从任意 JSON 里取额度。
    static func metric(from object: Any, config: ProviderConfig) throws -> QuotaMetric {
        let unit = QuotaUnit(rawValue: config.options[OptionKey.unit] ?? "") ?? .currency
        let currency = config.options[OptionKey.currency] ?? "CNY"
        let title = config.options[OptionKey.label]?.trimmingCharacters(in: .whitespaces).nilIfEmpty
            ?? (unit == .currency ? "账户余额" : unit.label)

        let remainingPath = config.options[OptionKey.remainingPath]?.trimmingCharacters(in: .whitespaces).nilIfEmpty
        let limitPath = config.options[OptionKey.limitPath]?.trimmingCharacters(in: .whitespaces).nilIfEmpty
        let usedPath = config.options[OptionKey.usedPath]?.trimmingCharacters(in: .whitespaces).nilIfEmpty

        let remaining = remainingPath.flatMap { JSONPath.double(in: object, path: $0) }
        let limit = limitPath.flatMap { JSONPath.double(in: object, path: $0) }
        let used = usedPath.flatMap { JSONPath.double(in: object, path: $0) }

        guard remaining != nil || (limit != nil && used != nil) || used != nil else {
            throw QuotaError.noValue
        }

        var footnote: [String] = []
        if let used { footnote.append("已用 \(QuotaFormatter.value(used, unit: unit, currencyCode: currency))") }
        if let limit { footnote.append("上限 \(QuotaFormatter.value(limit, unit: unit, currencyCode: currency))") }

        return QuotaMetric(
            id: "custom",
            title: title,
            unit: unit,
            currencyCode: unit == .currency ? currency : nil,
            used: used,
            limit: limit,
            remaining: remaining,
            footnote: footnote.isEmpty ? nil : footnote.joined(separator: " · ")
        )
    }
}

extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}
