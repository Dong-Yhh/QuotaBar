import Foundation

// MARK: - 单位

enum QuotaUnit: String, Codable, CaseIterable, Sendable {
    case currency
    case percent
    case tokens
    case requests
    case credits

    var label: String {
        switch self {
        case .currency: return "金额"
        case .percent: return "百分比"
        case .tokens: return "Token"
        case .requests: return "次数"
        case .credits: return "积分"
        }
    }

    var symbol: String {
        switch self {
        case .currency: return "banknote"
        case .percent: return "percent"
        case .tokens: return "textformat.123"
        case .requests: return "arrow.left.arrow.right"
        case .credits: return "circle.hexagongrid"
        }
    }
}

// MARK: - 指标

/// 一条具体的额度指标，例如「5 小时窗口剩余」「账户余额」「本月已用」。
struct QuotaMetric: Codable, Hashable, Identifiable, Sendable {
    /// 稳定的标识，用于记住用户选中了哪一条（如 "five_hour"）。
    var id: String
    var title: String
    var unit: QuotaUnit
    var currencyCode: String?
    var used: Double?
    var limit: Double?
    var remaining: Double?
    var resetsAt: Date?
    /// 补充说明，例如 "已用 $3.20 / $10.00"
    var footnote: String?
    /// 用剩余量作为主指标（余额类）还是已用量作为主指标（配额类）。
    var isRemainingPrimary: Bool = true

    init(
        id: String,
        title: String,
        unit: QuotaUnit,
        currencyCode: String? = nil,
        used: Double? = nil,
        limit: Double? = nil,
        remaining: Double? = nil,
        resetsAt: Date? = nil,
        footnote: String? = nil,
        isRemainingPrimary: Bool = true
    ) {
        self.id = id
        self.title = title
        self.unit = unit
        self.currencyCode = currencyCode
        self.used = used
        self.limit = limit
        self.remaining = remaining
        self.resetsAt = resetsAt
        self.footnote = footnote
        self.isRemainingPrimary = isRemainingPrimary
    }

    var resolvedRemaining: Double? {
        if let remaining { return remaining }
        if let limit, let used { return max(limit - used, 0) }
        return nil
    }

    var resolvedUsed: Double? {
        if let used { return used }
        if let limit, let remaining { return max(limit - remaining, 0) }
        return nil
    }

    /// 剩余比例 0...1；无法判断时返回 nil（例如无限额度）。
    var remainingFraction: Double? {
        guard let limit, limit > 0 else { return nil }
        if let value = resolvedRemaining { return Self.clamp(value / limit) }
        if let value = resolvedUsed { return Self.clamp(1 - value / limit) }
        return nil
    }

    private static func clamp(_ value: Double) -> Double {
        min(max(value, 0), 1)
    }
}

// MARK: - 快照

struct QuotaSnapshot: Codable, Hashable, Identifiable, Sendable {
    var providerID: UUID
    var metrics: [QuotaMetric]
    var updatedAt: Date
    var errorMessage: String?

    var id: UUID { providerID }
    var isFailure: Bool { errorMessage != nil }

    static func failure(providerID: UUID, message: String) -> QuotaSnapshot {
        QuotaSnapshot(providerID: providerID, metrics: [], updatedAt: Date(), errorMessage: message)
    }

    func metric(id: String?) -> QuotaMetric? {
        guard let id else { return metrics.first }
        return metrics.first { $0.id == id } ?? metrics.first
    }
}

// MARK: - 供应商类型

enum ProviderKind: String, Codable, CaseIterable, Identifiable, Sendable {
    case openRouter
    case deepSeek
    case moonshot
    case siliconFlow
    case soloAPI
    case claudeCode
    case openAI
    case custom

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .openRouter: return "OpenRouter"
        case .deepSeek: return "DeepSeek"
        case .moonshot: return "Kimi (Moonshot)"
        case .siliconFlow: return "SiliconFlow 硅基流动"
        case .soloAPI: return "SoloAPI"
        case .claudeCode: return "Claude Code 订阅"
        case .openAI: return "OpenAI 组织用量"
        case .custom: return "自定义接口"
        }
    }

    var blurb: String {
        switch self {
        case .openRouter: return "读取账户余额与密钥额度，数据最准确。"
        case .deepSeek: return "读取 DeepSeek 开放平台账户余额。"
        case .moonshot: return "读取 Kimi 开放平台账户可用余额。"
        case .siliconFlow: return "读取硅基流动账户余额与充值余额。"
        case .soloAPI: return "读取 SoloAPI 中转站余额、今日消费与累计消费。"
        case .claudeCode: return "读取本机 Claude Code 登录态，显示 5 小时 / 7 天限额。"
        case .openAI: return "使用 Admin Key 读取组织本月花费（OpenAI 不提供剩余额度接口）。"
        case .custom: return "任意返回 JSON 的接口，自己指定取数字段。"
        }
    }

    var credentialLabel: String {
        switch self {
        case .claudeCode: return "无需填写（读取本机登录态）"
        case .openAI: return "Admin API Key"
        default: return "API Key"
        }
    }

    var credentialHint: String {
        switch self {
        case .openRouter: return "sk-or-v1-…"
        case .deepSeek: return "sk-…"
        case .moonshot: return "sk-…"
        case .siliconFlow: return "sk-…"
        case .soloAPI: return "sk-…"
        case .claudeCode: return "留空即可"
        case .openAI: return "sk-admin-…"
        case .custom: return "按需填写，会以 Bearer 发送"
        }
    }

    var helpURLString: String? {
        switch self {
        case .openRouter: return "https://openrouter.ai/settings/keys"
        case .deepSeek: return "https://platform.deepseek.com/api_keys"
        case .moonshot: return "https://platform.moonshot.cn/console/api-keys"
        case .siliconFlow: return "https://cloud.siliconflow.cn/account/ak"
        case .soloAPI: return "https://soloapi.cc"
        case .claudeCode: return "https://claude.ai/settings/usage"
        case .openAI: return "https://platform.openai.com/settings/organization/admin-keys"
        case .custom: return nil
        }
    }

    var symbolName: String {
        switch self {
        case .openRouter: return "arrow.triangle.branch"
        case .deepSeek: return "drop.fill"
        case .moonshot: return "moon.stars.fill"
        case .siliconFlow: return "square.stack.3d.up.fill"
        case .soloAPI: return "arrow.triangle.swap"
        case .claudeCode: return "sparkle"
        case .openAI: return "circle.hexagongrid.fill"
        case .custom: return "slider.horizontal.3"
        }
    }

    var accentHex: String {
        switch self {
        case .openRouter: return "6E7BFF"
        case .deepSeek: return "4D6BFE"
        case .moonshot: return "8B5CF6"
        case .siliconFlow: return "16A34A"
        case .soloAPI: return "14B8A6"
        case .claudeCode: return "D97757"
        case .openAI: return "10A37F"
        case .custom: return "F59E0B"
        }
    }

    /// 编辑器里要显示哪种「接口地址」输入框。
    var endpointField: EndpointField {
        switch self {
        case .custom: return .fullURI
        case .soloAPI: return .baseURL
        default: return .none
        }
    }
}

enum EndpointField {
    /// 不显示接口地址
    case none
    /// 只填站点根地址，路径由代码决定
    case baseURL
    /// 完整地址 + 字段路径
    case fullURI
}

// MARK: - 供应商配置

enum OptionKey {
    static let endpoint = "endpoint"
    static let baseURL = "baseURL"
    static let headersJSON = "headersJSON"
    static let remainingPath = "remainingPath"
    static let usedPath = "usedPath"
    static let limitPath = "limitPath"
    static let unit = "unit"
    static let currency = "currency"
    static let label = "label"
    static let warnThreshold = "warnThreshold"
}

struct ProviderConfig: Codable, Identifiable, Hashable, Sendable {
    var id: UUID = UUID()
    var kind: ProviderKind
    var name: String
    var isEnabled: Bool = true
    var credential: String = ""
    var options: [String: String] = [:]

    init(kind: ProviderKind, name: String? = nil) {
        self.kind = kind
        self.name = name ?? kind.displayName
    }

    var endpoint: String { options[OptionKey.endpoint] ?? "" }

    /// 低于该百分比时进入「告警」配色（0...1），默认 20%。
    var warnThreshold: Double {
        guard let raw = options[OptionKey.warnThreshold], let value = Double(raw) else { return 0.2 }
        return min(max(value, 0.01), 0.99)
    }
}

// MARK: - 持久化状态

struct PersistedState: Codable, Sendable {
    var providers: [ProviderConfig] = []
    var snapshots: [QuotaSnapshot] = []
    /// 桌面小组件与菜单栏默认展示哪个供应商。
    var preferredProviderID: UUID?
    /// 默认展示哪个指标（如 Claude 的 five_hour / seven_day）。
    var preferredMetricID: String?
    var refreshInterval: Double = 600

    func provider(id: UUID?) -> ProviderConfig? {
        guard let id else { return nil }
        return providers.first { $0.id == id }
    }

    func snapshot(id: UUID) -> QuotaSnapshot? {
        snapshots.first { $0.providerID == id }
    }

    var enabledProviders: [ProviderConfig] {
        providers.filter(\.isEnabled)
    }

    /// 界面 / 小组件当前应聚焦的供应商。
    var focusProvider: ProviderConfig? {
        // 被停用的供应商不该继续占着界面 / 小组件。
        if let id = preferredProviderID,
           let match = providers.first(where: { $0.id == id && $0.isEnabled }) {
            return match
        }
        return enabledProviders.first ?? providers.first
    }

    /// 桌面小组件该显示哪一家。
    ///
    /// `pinnedID` 只在小组件里显式选了「固定显示」时才传进来；其余情况一律跟随
    /// App 里「设为小组件默认项」选中的那一家 —— 这样在 App 里换供应商，
    /// 小组件就会跟着换。
    func widgetProvider(pinnedID: UUID?) -> ProviderConfig? {
        if let pinnedID,
           let match = providers.first(where: { $0.id == pinnedID && $0.isEnabled }) {
            return match
        }
        return focusProvider
    }

    mutating func upsert(_ snapshot: QuotaSnapshot) {
        if let index = snapshots.firstIndex(where: { $0.providerID == snapshot.providerID }) {
            snapshots[index] = snapshot
        } else {
            snapshots.append(snapshot)
        }
    }

    mutating func remove(providerID: UUID) {
        providers.removeAll { $0.id == providerID }
        snapshots.removeAll { $0.providerID == providerID }
        if preferredProviderID == providerID { preferredProviderID = nil }
    }

    /// 早期版本里 SoloAPI 只能配成「自定义接口」，而且几乎配不对
    /// （指向对话用的 /v1、没有余额字段路径、key 塞在自定义请求头里）。
    /// 现在它有一等供应商了，这里把旧配置自动升级过来，用户不用重新填一遍。
    ///
    /// 返回是否发生了改动。
    mutating func migrateLegacySoloAPI() -> Bool {
        var changed = false

        for index in providers.indices {
            var provider = providers[index]
            guard provider.kind == .custom else { continue }
            guard provider.endpoint.lowercased().contains("soloapi") else { continue }

            // API Key 可能填在凭证栏，也可能塞在自定义请求头里。
            var key = provider.credential.trimmingCharacters(in: .whitespacesAndNewlines)
            if key.isEmpty,
               let raw = provider.options[OptionKey.headersJSON],
               let data = raw.data(using: .utf8),
               let parsed = try? JSONSerialization.jsonObject(with: data) as? [String: String] {
                let fromHeader = parsed["OPENAI_API_KEY"] ?? parsed["Authorization"]
                key = (fromHeader ?? "")
                    .replacingOccurrences(of: "Bearer ", with: "")
                    .trimmingCharacters(in: .whitespacesAndNewlines)
            }
            guard !key.isEmpty else { continue }

            provider.kind = .soloAPI
            provider.credential = key

            // 只保留站点根地址，路径交给代码。
            if let url = URL(string: provider.endpoint),
               let scheme = url.scheme,
               let host = url.host {
                provider.options[OptionKey.baseURL] = "\(scheme)://\(host)"
            }
            for optionKey in [
                OptionKey.endpoint,
                OptionKey.headersJSON,
                OptionKey.remainingPath,
                OptionKey.usedPath,
                OptionKey.limitPath,
                OptionKey.unit,
                OptionKey.currency
            ] {
                provider.options.removeValue(forKey: optionKey)
            }

            providers[index] = provider
            changed = true
        }

        return changed
    }
}
