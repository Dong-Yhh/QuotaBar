import SwiftUI

struct ProviderEditorView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var draft: ProviderConfig

    private let isNew: Bool
    private let onSave: (ProviderConfig) -> Void

    init(provider: ProviderConfig, isNew: Bool, onSave: @escaping (ProviderConfig) -> Void) {
        _draft = State(initialValue: provider)
        self.isNew = isNew
        self.onSave = onSave
    }

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            Form {
                generalSection
                if draft.kind == .custom {
                    customSection
                } else {
                    credentialSection
                    if draft.kind.endpointField == .baseURL {
                        baseURLSection
                    }
                }
                if draft.kind == .claudeCode {
                    claudeSection
                }
                thresholdSection
            }
            .formStyle(.grouped)
            Divider()
            footer
        }
        .frame(width: 520, height: editorHeight)
    }

    private var editorHeight: CGFloat {
        if draft.kind == .custom { return 660 }
        if draft.kind.endpointField == .baseURL { return 540 }
        return 470
    }

    private var header: some View {
        HStack(spacing: 12) {
            ProviderBadge(kind: draft.kind, size: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text(isNew ? "添加供应商" : "编辑供应商")
                    .font(.system(size: 15, weight: .semibold))
                Text(draft.kind.blurb)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
        }
        .padding(18)
    }

    @ViewBuilder
    private var generalSection: some View {
        Section {
            Picker("供应商", selection: $draft.kind) {
                ForEach(ProviderKind.allCases) { kind in
                    Text(kind.displayName).tag(kind)
                }
            }
            .onChange(of: draft.kind) { _, newValue in
                if draft.name == draft.kind.displayName || draft.name.isEmpty {
                    draft.name = newValue.displayName
                }
            }

            TextField("显示名称", text: $draft.name)

            Toggle("启用", isOn: $draft.isEnabled)
        }
    }

    @ViewBuilder
    private var credentialSection: some View {
        Section("凭证") {
            if draft.kind == .claudeCode {
                LabeledContent("说明", value: "无需填写，直接读取本机 Claude Code 登录态")
            } else {
                SecureField(draft.kind.credentialLabel, text: $draft.credential, prompt: Text(draft.kind.credentialHint))
                if let urlString = draft.kind.helpURLString, let url = URL(string: urlString) {
                    Link("在哪里找到这个 Key？", destination: url)
                        .font(.system(size: 11))
                }
            }
        }
    }

    @ViewBuilder
    private var baseURLSection: some View {
        Section("接口") {
            TextField(
                "接口地址",
                text: optionBinding(OptionKey.baseURL),
                prompt: Text(SoloAPIProvider.defaultBaseURL)
            )
            Text("留空就用默认地址 \(SoloAPIProvider.defaultBaseURL)。域名换了再改这里，填了 /v1 或结尾斜杠会自动纠正。")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    @ViewBuilder
    private var claudeSection: some View {
        Section("工作原理") {
            Text("QuotaBar 会读取本机 ~/.claude/.credentials.json（或钥匙串里的 Claude Code-credentials），调用 Anthropic 的 usage 接口，显示 5 小时 / 7 天窗口的剩余百分比。首次读取钥匙串时系统会弹窗询问，选择「始终允许」即可。")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    @ViewBuilder
    private var customSection: some View {
        Section("接口") {
            TextField("接口地址", text: optionBinding(OptionKey.endpoint), prompt: Text("https://example.com/api/balance"))
            SecureField("API Key（可选，会以 Bearer 发送）", text: $draft.credential)
            TextField(
                "额外请求头（JSON）",
                text: optionBinding(OptionKey.headersJSON),
                prompt: Text(#"{"X-Api-Version": "1"}"#)
            )
        }

        Section("取值") {
            TextField("剩余额度字段路径", text: optionBinding(OptionKey.remainingPath), prompt: Text("data.balance"))
            TextField("已用字段路径（可选）", text: optionBinding(OptionKey.usedPath), prompt: Text("data.usage"))
            TextField("上限字段路径（可选）", text: optionBinding(OptionKey.limitPath), prompt: Text("data.limit"))

            Picker("单位", selection: unitBinding) {
                ForEach(QuotaUnit.allCases, id: \.self) { unit in
                    Text(unit.label).tag(unit)
                }
            }

            if unitBinding.wrappedValue == .currency {
                TextField("货币代码", text: optionBinding(OptionKey.currency), prompt: Text("CNY / USD"))
            }

            TextField("指标名称", text: optionBinding(OptionKey.label), prompt: Text("账户余额"))
        }

        Section {
            Text("字段路径用点号分隔，例如 data.balance 或 balance_infos.0.total_balance。填了「剩余额度字段路径」就直接显示它；只填已用和上限时自动相减。")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    @ViewBuilder
    private var thresholdSection: some View {
        Section("提醒") {
            HStack {
                Text("剩余低于 \(Int(draft.warnThreshold * 100))% 时变色")
                Slider(value: thresholdBinding, in: 0.05...0.5, step: 0.05)
            }
        }
    }

    private var footer: some View {
        HStack {
            if let urlString = draft.kind.helpURLString, let url = URL(string: urlString) {
                Link("官方页面", destination: url)
                    .font(.system(size: 11))
            }
            Spacer()
            Button("取消") { dismiss() }
                .keyboardShortcut(.cancelAction)
            Button("保存") {
                let trimmedName = draft.name.trimmingCharacters(in: .whitespacesAndNewlines)
                draft.name = trimmedName.isEmpty ? draft.kind.displayName : trimmedName
                onSave(draft)
                dismiss()
            }
            .keyboardShortcut(.defaultAction)
            .buttonStyle(.borderedProminent)
        }
        .padding(16)
    }

    // MARK: - 绑定

    private func optionBinding(_ key: String) -> Binding<String> {
        Binding(
            get: { draft.options[key] ?? "" },
            set: { newValue in
                let trimmed = newValue.isEmpty ? nil : newValue
                draft.options[key] = trimmed
            }
        )
    }

    private var unitBinding: Binding<QuotaUnit> {
        Binding(
            get: { QuotaUnit(rawValue: draft.options[OptionKey.unit] ?? "") ?? .currency },
            set: { draft.options[OptionKey.unit] = $0.rawValue }
        )
    }

    private var thresholdBinding: Binding<Double> {
        Binding(
            get: { draft.warnThreshold },
            set: { draft.options[OptionKey.warnThreshold] = String($0) }
        )
    }
}
