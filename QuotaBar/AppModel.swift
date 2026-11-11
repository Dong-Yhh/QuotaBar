import Foundation
import SwiftUI
import WidgetKit

@MainActor
final class AppModel: ObservableObject {
    @Published private(set) var state: PersistedState
    @Published private(set) var refreshing: Set<UUID> = []
    @Published private(set) var launchAtLogin: Bool = LaunchAtLogin.isEnabled
    @Published var lastError: String?
    @Published var settingsError: String?

    private var timer: Timer?
    private let store = SharedStore.shared
    private var activityToken: NSObjectProtocol?
    private var lastWidgetReload = Date.distantPast

    init() {
        var loaded = store.load()
        // 把旧版本里配成「自定义接口」的 SoloAPI 自动升级成一等供应商。
        if loaded.migrateLegacySoloAPI() {
            store.save(loaded)
        }
        state = loaded
        beginBackgroundActivity()
        startAutoRefresh()
        if !state.providers.isEmpty {
            Task { await refreshAll() }
        }
    }

    // MARK: - 后台运行

    /// 关掉主窗口后 App 仍然留在菜单栏并继续按间隔刷新。
    /// 但 macOS 的 App Nap 会把后台 App 的定时器合并成几分钟才跑一次，
    /// 1 分钟的设定会形同虚设，所以要显式声明一个活动。
    private func beginBackgroundActivity() {
        guard activityToken == nil else { return }
        activityToken = ProcessInfo.processInfo.beginActivity(
            options: [
                .userInitiatedAllowingIdleSystemSleep,
                .suddenTerminationDisabled,
                .automaticTerminationDisabled
            ],
            reason: "按设定间隔刷新 AI 额度"
        )
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            try LaunchAtLogin.setEnabled(enabled)
            settingsError = nil
        } catch {
            settingsError = error.localizedDescription
        }
        launchAtLogin = LaunchAtLogin.isEnabled
    }

    // MARK: - 派生数据

    var focusProvider: ProviderConfig? { state.focusProvider }

    func snapshot(for provider: ProviderConfig) -> QuotaSnapshot? {
        state.snapshot(id: provider.id)
    }

    func primaryMetric(for provider: ProviderConfig) -> QuotaMetric? {
        let snapshot = state.snapshot(id: provider.id)
        guard let snapshot, !snapshot.metrics.isEmpty else { return nil }
        if snapshot.providerID == state.preferredProviderID, let preferred = state.preferredMetricID,
           let match = snapshot.metrics.first(where: { $0.id == preferred }) {
            return match
        }
        return snapshot.metrics.first
    }

    // MARK: - 编辑

    func add(kind: ProviderKind) -> ProviderConfig {
        let provider = ProviderConfig(kind: kind)
        state.providers.append(provider)
        if state.preferredProviderID == nil { state.preferredProviderID = provider.id }
        persist(immediateWidgetUpdate: true)
        return provider
    }

    func update(_ provider: ProviderConfig) {
        guard let index = state.providers.firstIndex(where: { $0.id == provider.id }) else { return }
        state.providers[index] = provider
        persist(immediateWidgetUpdate: true)
        Task { await refresh(id: provider.id) }
    }

    func remove(id: UUID) {
        state.remove(providerID: id)
        persist(immediateWidgetUpdate: true)
    }

    func move(from source: IndexSet, to destination: Int) {
        state.providers.move(fromOffsets: source, toOffset: destination)
        persist(immediateWidgetUpdate: true)
    }

    func setPreferred(providerID: UUID?) {
        state.preferredProviderID = providerID
        state.preferredMetricID = nil
        persist(immediateWidgetUpdate: true)
    }

    func setPreferredMetric(_ metricID: String?, providerID: UUID) {
        state.preferredProviderID = providerID
        state.preferredMetricID = metricID
        persist(immediateWidgetUpdate: true)
    }

    func setRefreshInterval(_ interval: Double) {
        state.refreshInterval = interval
        persist(immediateWidgetUpdate: true)
        startAutoRefresh()
    }

    /// 手动重载小组件（设置页里的按钮），绕过节流。
    func reloadWidgetNow() {
        lastWidgetReload = Date()
        WidgetCenter.shared.reloadAllTimelines()
    }

    // MARK: - 刷新

    func refreshAll() async {
        let providers = state.enabledProviders
        guard !providers.isEmpty else { return }
        refreshing.formUnion(providers.map(\.id))
        let snapshots = await QuotaService.snapshots(for: providers)
        for snapshot in snapshots {
            state.upsert(snapshot)
            refreshing.remove(snapshot.providerID)
        }
        persist()
    }

    func refresh(id: UUID) async {
        guard let provider = state.providers.first(where: { $0.id == id }) else { return }
        refreshing.insert(id)
        let snapshot = await QuotaService.snapshot(for: provider)
        state.upsert(snapshot)
        refreshing.remove(id)
        persist()
    }

    private func startAutoRefresh() {
        timer?.invalidate()
        let interval = max(state.refreshInterval, 60)
        timer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in
            Task { @MainActor in
                await self?.refreshAll()
            }
        }
    }

    // MARK: - 落盘

    /// 落盘并通知小组件。
    ///
    /// `immediateWidgetUpdate` 只在「用户在 App 里换了供应商 / 改了配置」时为 true ——
    /// 这种必须立刻看到。例行刷新走节流：系统对小组件重载有预算（大约每天几十次），
    /// 1 分钟刷新一次却每次都调 reloadAllTimelines 会把预算烧光，
    /// 结果真正需要立刻生效的那一次反而被系统丢掉。
    private func persist(immediateWidgetUpdate: Bool = false) {
        store.save(state)
        let now = Date()
        if immediateWidgetUpdate || now.timeIntervalSince(lastWidgetReload) >= 600 {
            lastWidgetReload = now
            WidgetCenter.shared.reloadAllTimelines()
        }
    }
}
