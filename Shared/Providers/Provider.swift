import Foundation

protocol QuotaProvider: Sendable {
    var kind: ProviderKind { get }
    func fetch(config: ProviderConfig) async throws -> [QuotaMetric]
}

enum ProviderRegistry {
    static func provider(for kind: ProviderKind) -> QuotaProvider {
        switch kind {
        case .openRouter: return OpenRouterProvider()
        case .deepSeek: return DeepSeekProvider()
        case .moonshot: return MoonshotProvider()
        case .siliconFlow: return SiliconFlowProvider()
        case .soloAPI: return SoloAPIProvider()
        case .claudeCode: return ClaudeCodeProvider()
        case .openAI: return OpenAIProvider()
        case .custom: return CustomProvider()
        }
    }
}

extension QuotaMetric {
    /// 余额型指标：知道「还剩多少」。
    static func balance(
        id: String,
        title: String,
        remaining: Double,
        currency: String,
        used: Double? = nil,
        limit: Double? = nil,
        footnote: String? = nil
    ) -> QuotaMetric {
        QuotaMetric(
            id: id,
            title: title,
            unit: .currency,
            currencyCode: currency,
            used: used,
            limit: limit,
            remaining: remaining,
            footnote: footnote,
            isRemainingPrimary: true
        )
    }

    /// 配额型指标：知道「用了多少 / 上限多少」，主指标显示剩余百分比。
    static func quota(
        id: String,
        title: String,
        used: Double,
        limit: Double,
        resetsAt: Date? = nil,
        footnote: String? = nil
    ) -> QuotaMetric {
        QuotaMetric(
            id: id,
            title: title,
            unit: .percent,
            used: used,
            limit: limit,
            remaining: max(limit - used, 0),
            resetsAt: resetsAt,
            footnote: footnote,
            isRemainingPrimary: true
        )
    }

    /// 只知用量、不知上限。
    static func usageOnly(
        id: String,
        title: String,
        used: Double,
        currency: String,
        footnote: String? = nil
    ) -> QuotaMetric {
        QuotaMetric(
            id: id,
            title: title,
            unit: .currency,
            currencyCode: currency,
            used: used,
            footnote: footnote,
            isRemainingPrimary: false
        )
    }
}
