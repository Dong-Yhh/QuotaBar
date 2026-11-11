import Foundation

/// 负责把「供应商配置」变成「额度快照」。App 和小组件都用它。
enum QuotaService {
    static func snapshot(for provider: ProviderConfig) async -> QuotaSnapshot {
        do {
            let metrics = try await ProviderRegistry.provider(for: provider.kind).fetch(config: provider)
            guard !metrics.isEmpty else {
                return .failure(providerID: provider.id, message: QuotaError.noValue.localizedDescription)
            }
            return QuotaSnapshot(providerID: provider.id, metrics: metrics, updatedAt: Date(), errorMessage: nil)
        } catch {
            return .failure(providerID: provider.id, message: error.localizedDescription)
        }
    }

    static func snapshots(for providers: [ProviderConfig]) async -> [QuotaSnapshot] {
        await withTaskGroup(of: QuotaSnapshot.self) { group in
            for provider in providers {
                group.addTask { await snapshot(for: provider) }
            }
            var results: [QuotaSnapshot] = []
            for await result in group {
                results.append(result)
            }
            return results
        }
    }
}
