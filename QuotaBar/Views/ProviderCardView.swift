import SwiftUI

struct ProviderCardView: View {
    let provider: ProviderConfig
    let snapshot: QuotaSnapshot?
    let isRefreshing: Bool
    let isFocused: Bool
    let preferredMetricID: String?

    var onRefresh: () -> Void
    var onEdit: () -> Void
    var onDelete: () -> Void
    var onFocus: () -> Void
    var onFocusMetric: (String) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            if let snapshot {
                if snapshot.isFailure {
                    errorView(snapshot.errorMessage ?? "未知错误")
                } else {
                    metricList(snapshot)
                }
            } else {
                placeholder
            }
        }
        .padding(16)
        .background(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .fill(Color(nsColor: .controlBackgroundColor))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .strokeBorder(isFocused ? Color.accentColor.opacity(0.65) : Color.primary.opacity(0.08),
                              lineWidth: isFocused ? 1.5 : 1)
        )
    }

    // MARK: - 头部

    private var header: some View {
        HStack(spacing: 10) {
            ProviderBadge(kind: provider.kind)

            VStack(alignment: .leading, spacing: 2) {
                Text(provider.name)
                    .font(.system(size: 13, weight: .semibold))
                    .lineLimit(1)
                HStack(spacing: 6) {
                    Text(provider.kind.displayName)
                        .font(.system(size: 10))
                        .foregroundStyle(.secondary)
                    if !provider.isEnabled {
                        StatusPill(text: "已停用", color: .secondary, symbol: "pause.fill")
                    }
                }
            }

            Spacer(minLength: 6)

            if isRefreshing {
                ProgressView().controlSize(.small)
            } else if let snapshot, !snapshot.isFailure {
                Text(QuotaFormatter.relative(snapshot.updatedAt))
                    .font(.system(size: 10))
                    .foregroundStyle(.tertiary)
            }

            Menu {
                Button(isFocused ? "已是小组件默认项" : "设为小组件默认项", action: onFocus)
                    .disabled(isFocused)
                Button("立即刷新", action: onRefresh)
                Divider()
                Button("编辑…", action: onEdit)
                Button("删除", role: .destructive, action: onDelete)
            } label: {
                Image(systemName: "ellipsis")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(.secondary)
                    .frame(width: 22, height: 22)
                    .contentShape(Rectangle())
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.hidden)
            .frame(width: 22)
        }
    }

    // MARK: - 主体

    private func metricList(_ snapshot: QuotaSnapshot) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            ForEach(snapshot.metrics) { metric in
                MetricRow(
                    metric: metric,
                    threshold: provider.warnThreshold,
                    isFocused: isFocused && (preferredMetricID == metric.id || preferredMetricID == nil)
                ) {
                    onFocusMetric(metric.id)
                }
            }
        }
    }

    private func errorView(_ message: String) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
                .font(.system(size: 12))
            VStack(alignment: .leading, spacing: 4) {
                Text(message)
                    .font(.system(size: 11))
                    .foregroundStyle(.primary)
                    .fixedSize(horizontal: false, vertical: true)
                Button("重试", action: onRefresh)
                    .buttonStyle(.link)
                    .font(.system(size: 11))
            }
        }
    }

    private var placeholder: some View {
        HStack(spacing: 8) {
            Image(systemName: "clock.arrow.circlepath")
                .foregroundStyle(.secondary)
            Text("还没有数据，点右上角刷新一下")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
            Spacer()
            Button("刷新", action: onRefresh).buttonStyle(.link).font(.system(size: 11))
        }
    }
}
