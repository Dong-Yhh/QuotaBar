import AppIntents
import SwiftUI
import WidgetKit

struct QuotaEntry: TimelineEntry {
    var date: Date
    var providerName: String
    var kind: ProviderKind
    var metrics: [QuotaMetric]
    var errorMessage: String?
    var warnThreshold: Double
    var hasProvider: Bool

    static let placeholder = QuotaEntry(
        date: Date(),
        providerName: "Claude Code",
        kind: .claudeCode,
        metrics: [.quota(id: "seven_day", title: "7 天窗口", used: 26, limit: 100, resetsAt: Date().addingTimeInterval(5400))],
        errorMessage: nil,
        warnThreshold: 0.2,
        hasProvider: true
    )
}

struct ProviderTimelineProvider: AppIntentTimelineProvider {
    func placeholder(in context: Context) -> QuotaEntry {
        .placeholder
    }

    func snapshot(for configuration: ProviderSelectionIntent, in context: Context) async -> QuotaEntry {
        await entry(for: configuration, allowNetwork: !context.isPreview)
    }

    func timeline(for configuration: ProviderSelectionIntent, in context: Context) async -> Timeline<QuotaEntry> {
        let entry = await entry(for: configuration, allowNetwork: true)
        // 小组件自己请求刷新的下限受系统限制（约 5 分钟）。
        // App 在后台按用户设定的间隔跑，每次刷新都会调用 reloadAllTimelines()，
        // 所以真正决定桌面上数据新鲜度的是 App 那边的间隔，这里只兜底。
        let configured = SharedStore.shared.load().refreshInterval
        let interval: TimeInterval = entry.errorMessage == nil ? max(configured, 300) : 300
        return Timeline(entries: [entry], policy: .after(Date().addingTimeInterval(interval)))
    }

    private func entry(for configuration: ProviderSelectionIntent, allowNetwork: Bool) async -> QuotaEntry {
        let store = SharedStore.shared
        let state = store.load()

        // 只有显式选了「固定显示」才用小组件自己的选择，否则一律跟随 App。
        let pinned = configuration.mode == .specific ? configuration.provider?.uuid : nil
        guard let provider = state.widgetProvider(pinnedID: pinned) else {
            return QuotaEntry(
                date: Date(),
                providerName: "未配置",
                kind: .custom,
                metrics: [],
                errorMessage: nil,
                warnThreshold: 0.2,
                hasProvider: false
            )
        }

        var snapshot = state.snapshot(id: provider.id)

        if allowNetwork || snapshot == nil {
            let fresh = await QuotaService.snapshot(for: provider)
            snapshot = fresh
            var updated = store.load()
            updated.upsert(fresh)
            store.save(updated)
        }

        return QuotaEntry(
            date: Date(),
            providerName: provider.name,
            kind: provider.kind,
            metrics: snapshot?.metrics ?? [],
            errorMessage: snapshot?.errorMessage,
            warnThreshold: provider.warnThreshold,
            hasProvider: true
        )
    }
}

struct QuotaWidget: Widget {
    private let kind = "QuotaBarWidget"

    var body: some WidgetConfiguration {
        AppIntentConfiguration(
            kind: kind,
            intent: ProviderSelectionIntent.self,
            provider: ProviderTimelineProvider()
        ) { entry in
            QuotaWidgetView(entry: entry)
                .containerBackground(for: .widget) {
                    LinearGradient(
                        colors: [entry.kind.accent.opacity(0.22), entry.kind.accent.opacity(0.04)],
                        startPoint: .topLeading,
                        endPoint: .bottomTrailing
                    )
                }
        }
        .configurationDisplayName("AI 额度")
        .description("显示 AI 供应商的剩余额度。右键小组件可以选择显示哪一个。")
        .supportedFamilies([.systemSmall, .systemMedium, .systemLarge])
    }
}

struct QuotaWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let entry: QuotaEntry

    var body: some View {
        Group {
            if !entry.hasProvider {
                emptyState
            } else if let error = entry.errorMessage {
                errorState(error)
            } else if entry.metrics.isEmpty {
                errorState("暂无数据，打开 App 刷新一次")
            } else {
                switch family {
                case .systemSmall: small
                case .systemLarge: large
                default: medium
                }
            }
        }
        .widgetURL(URL(string: "quotabar://open"))
    }

    // MARK: - 布局

    private var small: some View {
        VStack(alignment: .leading, spacing: 8) {
            widgetHeader
            Spacer(minLength: 0)
            if let metric = entry.metrics.first {
                Text(QuotaFormatter.primary(metric))
                    .font(.system(size: 26, weight: .bold, design: .rounded))
                    .foregroundStyle(level(metric).color)
                    .monospacedDigit()
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                Text(metric.title)
                    .font(.system(size: 10))
                    .foregroundStyle(.secondary)
                QuotaProgressBar(fraction: metric.remainingFraction, level: level(metric), height: 6)
                if let resetsAt = metric.resetsAt {
                    Text(QuotaFormatter.resetDescription(resetsAt))
                        .font(.system(size: 9))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
    }

    private var medium: some View {
        VStack(alignment: .leading, spacing: 8) {
            widgetHeader
            Spacer(minLength: 0)
            ForEach(Array(entry.metrics.prefix(2))) { metric in
                VStack(alignment: .leading, spacing: 4) {
                    HStack(alignment: .firstTextBaseline) {
                        Text(metric.title)
                            .font(.system(size: 10, weight: .medium))
                            .foregroundStyle(.secondary)
                        Spacer(minLength: 6)
                        Text(QuotaFormatter.primary(metric))
                            .font(.system(size: 17, weight: .semibold, design: .rounded))
                            .foregroundStyle(level(metric).color)
                            .monospacedDigit()
                    }
                    QuotaProgressBar(fraction: metric.remainingFraction, level: level(metric), height: 5)
                    if let resetsAt = metric.resetsAt {
                        Text(QuotaFormatter.resetDescription(resetsAt))
                            .font(.system(size: 9))
                            .foregroundStyle(.tertiary)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var large: some View {
        VStack(alignment: .leading, spacing: 10) {
            widgetHeader
            ForEach(Array(entry.metrics.prefix(5))) { metric in
                VStack(alignment: .leading, spacing: 4) {
                    HStack(alignment: .firstTextBaseline) {
                        Text(metric.title)
                            .font(.system(size: 11, weight: .medium))
                            .foregroundStyle(.secondary)
                        Spacer(minLength: 6)
                        Text(QuotaFormatter.primary(metric))
                            .font(.system(size: 20, weight: .semibold, design: .rounded))
                            .foregroundStyle(level(metric).color)
                            .monospacedDigit()
                    }
                    QuotaProgressBar(fraction: metric.remainingFraction, level: level(metric), height: 6)
                    if let detail = detailText(metric) {
                        Text(detail)
                            .font(.system(size: 10))
                            .foregroundStyle(.tertiary)
                            .lineLimit(1)
                    }
                }
            }
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var emptyState: some View {
        VStack(alignment: .leading, spacing: 6) {
            Image(systemName: "gauge.with.dots.needle.33percent")
                .font(.system(size: 22, weight: .light))
                .foregroundStyle(.secondary)
            Text("打开 QuotaBar 添加供应商")
                .font(.system(size: 12, weight: .semibold))
            Text("然后右键这个小组件选择要显示哪一个。")
                .font(.system(size: 10))
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
    }

    private func errorState(_ message: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            widgetHeader
            Spacer(minLength: 0)
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
                .font(.system(size: 16))
            Text(message)
                .font(.system(size: 10))
                .foregroundStyle(.secondary)
                .lineLimit(4)
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
    }

    private var widgetHeader: some View {
        HStack(spacing: 6) {
            Image(systemName: entry.kind.symbolName)
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(entry.kind.accent)
            Text(entry.providerName)
                .font(.system(size: 11, weight: .semibold))
                .lineLimit(1)
            Spacer(minLength: 0)
        }
    }

    private func level(_ metric: QuotaMetric) -> QuotaLevel {
        .of(metric, threshold: entry.warnThreshold)
    }

    private func detailText(_ metric: QuotaMetric) -> String? {
        var parts: [String] = []
        if let footnote = metric.footnote { parts.append(footnote) }
        if let resetsAt = metric.resetsAt { parts.append(QuotaFormatter.resetDescription(resetsAt)) }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }
}
