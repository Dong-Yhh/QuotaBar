import SwiftUI

struct SettingsView: View {
    @EnvironmentObject private var model: AppModel

    private let intervals: [(label: String, seconds: Double)] = [
        ("1 分钟", 60),
        ("2 分钟", 120),
        ("5 分钟", 300),
        ("10 分钟", 600),
        ("30 分钟", 1800),
        ("1 小时", 3600)
    ]

    var body: some View {
        Form {
            refreshSection
            backgroundSection
            widgetSection
            storageSection
            sourcesSection
        }
        .formStyle(.grouped)
        .padding(.top, 8)
    }

    // MARK: - 刷新

    private var refreshSection: some View {
        Section("刷新") {
            Picker("自动刷新间隔", selection: intervalBinding) {
                ForEach(intervals, id: \.seconds) { item in
                    Text(item.label).tag(item.seconds)
                }
            }

            if model.state.refreshInterval <= 120 {
                Label(
                    "1–2 分钟适合盯着 Claude Code 的 5 小时窗口。余额类接口有频率限制，长期高频可能被限流，日常建议 5–10 分钟。",
                    systemImage: "exclamationmark.triangle"
                )
                .font(.system(size: 11))
                .foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
            } else {
                Text("打开主界面时会立刻刷新一遍。")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
        }
    }

    // MARK: - 后台运行

    private var backgroundSection: some View {
        Section("后台运行") {
            Toggle("开机自动启动", isOn: launchAtLoginBinding)

            Label(
                "关掉主窗口后 App 不会退出，会留在菜单栏按上面的间隔继续在后台刷新；想彻底退出请点菜单栏图标 →「退出」。",
                systemImage: "menubar.arrow.up.rectangle"
            )
            .font(.system(size: 11))
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)

            if let error = model.settingsError {
                Label(error, systemImage: "exclamationmark.triangle.fill")
                    .font(.system(size: 11))
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    // MARK: - 小组件

    private var widgetSection: some View {
        Section("桌面小组件") {
            Text("把 QuotaBar.app 拖进「应用程序」并运行一次，然后右键桌面 →「编辑小组件」→ 搜索 QuotaBar，拖到桌面上。右键小组件 →「编辑小组件」可以单独选择显示哪个供应商。")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Text("小组件本身由系统调度刷新（通常 15 分钟起，系统不允许更频繁），但 App 每次刷新都会主动通知小组件更新，所以 App 在后台跑的时候，桌面上看到的数据会跟得更紧。")
                .font(.system(size: 11))
                .foregroundStyle(.tertiary)
                .fixedSize(horizontal: false, vertical: true)

            Text("在卡片右上角「⋯」里点「设为小组件默认项」，小组件会立刻切过去。小组件那边如果选了「固定显示」，则以小组件自己的设置为准。")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Button("立即重载小组件") {
                model.reloadWidgetNow()
            }
            .font(.system(size: 11))
        }
    }

    // MARK: - 存储

    private var storageSection: some View {
        Section("数据存放位置") {
            ForEach(Array(SharedStore.shared.diagnostics().enumerated()), id: \.offset) { _, location in
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        Image(systemName: location.exists ? "checkmark.circle.fill" : "circle.dashed")
                            .foregroundStyle(location.exists ? Color.green : Color.secondary)
                            .font(.system(size: 10))
                        Text(location.writable ? "可写" : "不可写")
                            .font(.system(size: 10, weight: .medium))
                            .foregroundStyle(.secondary)
                        if let updatedAt = location.updatedAt {
                            Text("· 更新于 \(QuotaFormatter.relative(updatedAt))")
                                .font(.system(size: 10))
                                .foregroundStyle(.tertiary)
                        }
                    }
                    Text(location.path)
                        .font(.system(size: 9, design: .monospaced))
                        .foregroundStyle(.tertiary)
                        .textSelection(.enabled)
                        .lineLimit(2)
                        .truncationMode(.middle)
                }
                .padding(.vertical, 2)
            }
        }
    }

    // MARK: - 供应商说明

    private var sourcesSection: some View {
        Section("数据来源说明") {
            ForEach(ProviderKind.allCases) { kind in
                HStack(alignment: .top, spacing: 8) {
                    ProviderBadge(kind: kind, size: 20)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(kind.displayName)
                            .font(.system(size: 11, weight: .semibold))
                        Text(kind.blurb)
                            .font(.system(size: 11))
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                .padding(.vertical, 1)
            }
        }
    }

    // MARK: - 绑定

    private var intervalBinding: Binding<Double> {
        Binding(
            get: { model.state.refreshInterval },
            set: { model.setRefreshInterval($0) }
        )
    }

    private var launchAtLoginBinding: Binding<Bool> {
        Binding(
            get: { model.launchAtLogin },
            set: { model.setLaunchAtLogin($0) }
        )
    }
}
