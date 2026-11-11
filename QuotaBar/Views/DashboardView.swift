import SwiftUI
import WidgetKit

struct DashboardView: View {
    @EnvironmentObject private var model: AppModel
    @State private var editing: EditorTarget?

    struct EditorTarget: Identifiable {
        var provider: ProviderConfig
        var isNew: Bool
        var id: UUID { provider.id }
    }

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            content
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .sheet(item: $editing) { target in
            ProviderEditorView(provider: target.provider, isNew: target.isNew) { updated in
                model.update(updated)
            }
        }
    }

    // MARK: - 头部

    private var header: some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text("AI 额度")
                    .font(.system(size: 16, weight: .semibold))
                Text(subtitle)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button {
                Task { await model.refreshAll() }
            } label: {
                if model.refreshing.isEmpty {
                    Label("刷新", systemImage: "arrow.clockwise")
                } else {
                    Label("刷新中…", systemImage: "arrow.triangle.2.circlepath")
                }
            }
            .disabled(model.refreshing.count > 0 || model.state.providers.isEmpty)

            Menu {
                ForEach(ProviderKind.allCases) { kind in
                    Button(kind.displayName) { addProvider(kind) }
                }
            } label: {
                Label("添加", systemImage: "plus")
            }
            .menuStyle(.borderlessButton)
            .fixedSize()
            .frame(width: 78)
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 14)
    }

    private var subtitle: String {
        if model.state.providers.isEmpty { return "还没有添加任何供应商" }
        let focus = model.focusProvider?.name ?? "—"
        return "共 \(model.state.providers.count) 个供应商 · 小组件当前显示「\(focus)」"
    }

    // MARK: - 内容

    private var content: some View {
        ScrollView {
            if model.state.providers.isEmpty {
                emptyState
            } else {
                LazyVStack(spacing: 12) {
                    ForEach(model.state.providers) { provider in
                        ProviderCardView(
                            provider: provider,
                            snapshot: model.snapshot(for: provider),
                            isRefreshing: model.refreshing.contains(provider.id),
                            isFocused: model.state.preferredProviderID == provider.id,
                            preferredMetricID: model.state.preferredMetricID,
                            onRefresh: { Task { await model.refresh(id: provider.id) } },
                            onEdit: {
                                editing = EditorTarget(provider: provider, isNew: false)
                            },
                            onDelete: { model.remove(id: provider.id) },
                            onFocus: { model.setPreferred(providerID: provider.id) },
                            onFocusMetric: { metricID in
                                model.setPreferredMetric(metricID, providerID: provider.id)
                            }
                        )
                    }
                    widgetHint
                }
                .padding(18)
            }
        }
    }

    private var emptyState: some View {
        VStack(spacing: 14) {
            Image(systemName: "gauge.with.dots.needle.33percent")
                .font(.system(size: 40, weight: .light))
                .foregroundStyle(.tertiary)
            Text("添加第一个 AI 供应商")
                .font(.system(size: 15, weight: .semibold))
            Text("支持 OpenRouter、DeepSeek、Kimi、硅基流动、Claude Code 订阅、OpenAI 组织用量，也可以自定义接口。")
                .font(.system(size: 12))
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 340)
            Menu {
                ForEach(ProviderKind.allCases) { kind in
                    Button(kind.displayName) { addProvider(kind) }
                }
            } label: {
                Label("添加供应商", systemImage: "plus")
            }
            .menuStyle(.borderlessButton)
            .fixedSize()
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 90)
    }

    private var widgetHint: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "square.grid.2x2")
                .foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 3) {
                Text("把额度放到桌面上")
                    .font(.system(size: 11, weight: .semibold))
                Text("把 QuotaBar 安装到「应用程序」并运行一次，然后右键桌面 →「编辑小组件」，搜索 QuotaBar 拖到桌面。小组件里可以单独选择要显示哪个供应商。")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            SettingsLink { Text("设置…") }
            .buttonStyle(.link)
            .font(.system(size: 11))
        }
        .padding(14)
        .background(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Color.primary.opacity(0.04))
        )
    }

    private func addProvider(_ kind: ProviderKind) {
        let provider = model.add(kind: kind)
        editing = EditorTarget(provider: provider, isNew: true)
    }
}
