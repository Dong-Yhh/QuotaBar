import Foundation
import Security

/// 读取本机 Claude Code 的登录态，通过 Anthropic 的 OAuth usage 接口拿到
/// 5 小时 / 7 天窗口的限额使用率。这是 Claude 订阅套餐唯一可编程获取的额度来源。
struct ClaudeCodeProvider: QuotaProvider {
    let kind: ProviderKind = .claudeCode

    private static let oauthClientID = "9d1c250a-e61b-44d9-88ed-5944d1962f5e"
    private static let usageURL = "https://api.anthropic.com/api/oauth/usage"
    private static let tokenURL = "https://console.anthropic.com/v1/oauth/token"
    private static let keychainService = "Claude Code-credentials"

    // MARK: - 凭证

    enum CredentialSource {
        case file(URL)
        case keychain
    }

    struct Credentials {
        var accessToken: String
        var refreshToken: String?
        var expiresAt: Date?
        var subscriptionType: String?
        var source: CredentialSource
        var raw: [String: Any]
    }

    private var credentialsURL: URL {
        URL(fileURLWithPath: NSHomeDirectory(), isDirectory: true)
            .appendingPathComponent(".claude", isDirectory: true)
            .appendingPathComponent(".credentials.json")
    }

    func fetch(config: ProviderConfig) async throws -> [QuotaMetric] {
        var credentials = try loadCredentials()

        if let expiresAt = credentials.expiresAt, expiresAt.timeIntervalSinceNow < 120 {
            if let refreshed = try? await refresh(credentials) {
                credentials = refreshed
                save(refreshed)
            }
        }

        do {
            return try await fetchUsage(token: credentials.accessToken)
        } catch QuotaError.http(let status, _) where status == 401 || status == 403 {
            guard let refreshed = try? await refresh(credentials) else {
                throw QuotaError.http(status: status, message: "登录态已过期，请打开 Claude Code 重新登录一次")
            }
            save(refreshed)
            return try await fetchUsage(token: refreshed.accessToken)
        }
    }

    // MARK: - 读取 / 回写本地登录态

    private func loadCredentials() throws -> Credentials {
        if let data = try? Data(contentsOf: credentialsURL),
           let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
           let oauth = root["claudeAiOauth"] as? [String: Any],
           let token = oauth["accessToken"] as? String,
           !token.isEmpty {
            return Credentials(
                accessToken: token,
                refreshToken: oauth["refreshToken"] as? String,
                expiresAt: Self.date(fromMilliseconds: oauth["expiresAt"]),
                subscriptionType: oauth["subscriptionType"] as? String,
                source: .file(credentialsURL),
                raw: root
            )
        }

        if let data = readKeychain(), let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] {
            // 钥匙串里存的可能是整份 credentials，也可能直接就是 oauth 字典。
            let oauth = (root["claudeAiOauth"] as? [String: Any]) ?? root
            if let token = oauth["accessToken"] as? String, !token.isEmpty {
                return Credentials(
                    accessToken: token,
                    refreshToken: oauth["refreshToken"] as? String,
                    expiresAt: Self.date(fromMilliseconds: oauth["expiresAt"]),
                    subscriptionType: oauth["subscriptionType"] as? String,
                    source: .keychain,
                    raw: root["claudeAiOauth"] != nil ? root : ["claudeAiOauth": root]
                )
            }
        }

        throw QuotaError.missingCredential(
            "找不到 Claude Code 登录态（~/.claude/.credentials.json 或钥匙串 Claude Code-credentials）。请先在终端运行一次 claude 并完成登录。"
        )
    }

    private func save(_ credentials: Credentials) {
        switch credentials.source {
        case .file(let url):
            guard let data = try? JSONSerialization.data(withJSONObject: credentials.raw, options: [.prettyPrinted]) else { return }
            try? data.write(to: url, options: .atomic)
        case .keychain:
            guard let data = try? JSONSerialization.data(withJSONObject: credentials.raw) else { return }
            let query: [String: Any] = [
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: Self.keychainService
            ]
            SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        }
    }

    private func readKeychain() -> Data? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: Self.keychainService,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess else { return nil }
        return item as? Data
    }

    // MARK: - 刷新 token

    private func refresh(_ credentials: Credentials) async throws -> Credentials {
        guard let refreshToken = credentials.refreshToken, !refreshToken.isEmpty else {
            throw QuotaError.missingCredential("refresh token")
        }
        let payload: [String: Any] = [
            "grant_type": "refresh_token",
            "refresh_token": refreshToken,
            "client_id": Self.oauthClientID
        ]
        let body = try JSONSerialization.data(withJSONObject: payload)
        let response = try await HTTPClient.json(
            Self.tokenURL,
            method: "POST",
            headers: ["Content-Type": "application/json", "Accept": "application/json"],
            body: body
        )
        guard let object = response.object,
              let accessToken = JSONPath.string(in: object, path: "access_token") else {
            throw QuotaError.noValue
        }

        var updated = credentials
        updated.accessToken = accessToken
        if let newRefresh = JSONPath.string(in: object, path: "refresh_token") {
            updated.refreshToken = newRefresh
        }
        if let expiresIn = JSONPath.double(in: object, path: "expires_in") {
            updated.expiresAt = Date().addingTimeInterval(expiresIn - 60)
        }

        var oauth = updated.raw["claudeAiOauth"] as? [String: Any] ?? [:]
        oauth["accessToken"] = updated.accessToken
        if let token = updated.refreshToken { oauth["refreshToken"] = token }
        if let expiresAt = updated.expiresAt { oauth["expiresAt"] = expiresAt.timeIntervalSince1970 * 1000 }
        updated.raw["claudeAiOauth"] = oauth
        return updated
    }

    // MARK: - 查询用量

    private func fetchUsage(token: String) async throws -> [QuotaMetric] {
        let response = try await HTTPClient.json(
            Self.usageURL,
            headers: [
                "Authorization": "Bearer \(token)",
                "anthropic-beta": "oauth-2025-04-20",
                "Accept": "application/json"
            ]
        )
        guard let object = response.object else { throw QuotaError.noValue }

        let metrics = Self.metrics(fromUsage: object)
        guard !metrics.isEmpty else { throw QuotaError.noValue }
        return metrics
    }

    /// GET /api/oauth/usage
    /// {"five_hour":{"utilization":79.0,"resets_at":"..."},"seven_day":{"utilization":26.0,...},
    ///  "extra_usage":{"is_enabled":true,"monthly_limit":1000,"used_credits":100}}
    /// utilization / monthly_limit / used_credits 均为「已用」口径，金额单位是美分。
    static func metrics(fromUsage object: Any) -> [QuotaMetric] {
        let windows: [(id: String, title: String)] = [
            ("five_hour", "5 小时窗口"),
            ("seven_day", "7 天窗口"),
            ("seven_day_opus", "Opus 周限额"),
            ("seven_day_sonnet", "Sonnet 周限额")
        ]

        var metrics: [QuotaMetric] = []
        for window in windows {
            guard let utilization = JSONPath.double(in: object, path: "\(window.id).utilization") else { continue }
            let resetsAt = JSONPath.date(in: object, path: "\(window.id).resets_at")
            metrics.append(.quota(
                id: window.id,
                title: window.title,
                used: utilization,
                limit: 100,
                resetsAt: resetsAt,
                footnote: "已用 \(QuotaFormatter.plain(utilization))%"
            ))
        }

        if (JSONPath.value(in: object, path: "extra_usage.is_enabled") as? Bool) == true,
           let usedCents = JSONPath.double(in: object, path: "extra_usage.used_credits") {
            let used = usedCents / 100
            if let limitCents = JSONPath.double(in: object, path: "extra_usage.monthly_limit"), limitCents > 0 {
                let limit = limitCents / 100
                metrics.append(.balance(
                    id: "extra_usage",
                    title: "额外用量额度",
                    remaining: max(limit - used, 0),
                    currency: "USD",
                    used: used,
                    limit: limit,
                    footnote: "已用 \(QuotaFormatter.money(used, code: "USD")) / \(QuotaFormatter.money(limit, code: "USD"))"
                ))
            } else {
                metrics.append(.usageOnly(id: "extra_usage", title: "额外用量已用", used: used, currency: "USD"))
            }
        }
        return metrics
    }

    private static func date(fromMilliseconds raw: Any?) -> Date? {
        guard let raw else { return nil }
        let value: Double?
        if let number = raw as? NSNumber { value = number.doubleValue }
        else if let text = raw as? String { value = Double(text) }
        else { value = nil }
        guard let value else { return nil }
        return Date(timeIntervalSince1970: value > 1e12 ? value / 1000 : value)
    }
}
