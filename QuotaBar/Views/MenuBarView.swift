import SwiftUI

struct MenuBarView: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            Divider()
            if model.state.providers.isEmpty {
                Text("还没有添加供应商，打开主界面添加一个吧。")
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .padding(16)
            } else {
                ScrollView {
                    VStack(spacing: 0) {
                        ForEach(model.state.providers) { provider in
                            row(provider)
                            if provider.id != model.state.providers.last?.id {
                                Divider().padding(.leading, 44)
                            }
                        }
                    }
                }
                .frame(maxHeight: 360)
            }
            Divider()
            footer
        }
        .frame(width: 340)
    }

    private var header: some View {
        HStack {
            Text("AI 额度")
                .font(.system(size: 13, weight: .semibold))
            Spacer()
            if !model.refreshing.isEmpty {
                ProgressView().controlSize(.small)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    private func row(_ provider: ProviderConfig) -> some View {
        let snapshot = model.snapshot(for: provider)
        let metric = snapshot?.metrics.first
        let isFocused = model.state.preferredProviderID == provider.id

        return Button {
            model.setPreferred(providerID: provider.id)
        } label: {
            HStack(spacing: 10) {
                ProviderBadge(kind: provider.kind, size: 26)

                VStack(alignment: .leading, spacing: 2) {
                    Text(provider.name)
                        .font(.system(size: 12, weight: .medium))
                        .lineLimit(1)
                    if let snapshot, snapshot.isFailure {
                        Text(snapshot.errorMessage ?? "出错了")
                            .font(.system(size: 10))
                            .foregroundStyle(.orange)
                            .lineLimit(1)
                    } else if let metric {
                        Text(metric.title)
                            .font(.system(size: 10))
                            .foregroundStyle(.secondary)
                    } else {
                        Text(provider.kind.displayName)
                            .font(.system(size: 10))
                            .foregroundStyle(.secondary)
                    }
                }

                Spacer(minLength: 6)

                if let metric, metric.resolvedRemaining != nil || metric.resolvedUsed != nil {
                    VStack(alignment: .trailing, spacing: 4) {
                        Text(QuotaFormatter.primary(metric))
                            .font(.system(size: 13, weight: .semibold, design: .rounded))
                            .monospacedDigit()
                        QuotaProgressBar(
                            fraction: metric.remainingFraction,
                            level: .of(metric, threshold: provider.warnThreshold),
                            height: 4
                        )
                        .frame(width: 52)
                    }
                } else {
                    Text("—")
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                }

                Image(systemName: isFocused ? "checkmark.circle.fill" : "circle")
                    .font(.system(size: 11))
                    .foregroundStyle(isFocused ? Color.accentColor : Color.secondary.opacity(0.5))
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private var footer: some View {
        HStack(spacing: 10) {
            Button {
                openWindow(id: "dashboard")
                NSApp.activate(ignoringOtherApps: true)
            } label: {
                Label("主界面", systemImage: "macwindow")
            }
            .buttonStyle(.link)

            Button {
                Task { await model.refreshAll() }
            } label: {
                Label("刷新", systemImage: "arrow.clockwise")
            }
            .buttonStyle(.link)
            .disabled(model.refreshing.count > 0)

            Spacer()

            SettingsLink {
                Label("设置", systemImage: "gearshape")
            }
            .buttonStyle(.link)

            Button {
                NSApplication.shared.terminate(nil)
            } label: {
                Label("退出", systemImage: "power")
            }
            .buttonStyle(.link)
        }
        .font(.system(size: 11))
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}
