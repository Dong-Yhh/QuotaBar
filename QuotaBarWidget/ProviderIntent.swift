import AppIntents
import Foundation
import WidgetKit

/// 小组件显示哪一家由这个开关决定。
///
/// 之前只有「指定供应商」一个参数，而且我们提供了 `defaultResult()`，
/// WidgetKit 会拿默认值把这个参数**预填**掉 —— 结果是小组件一旦添加到桌面
/// 就被钉死在当时的第一家供应商上，之后在 App 里怎么切它都不动。
/// 现在默认「跟随 App」，需要固定显示某一家的人再手动选。
enum ProviderSelectionMode: String, AppEnum {
    case followApp
    case specific

    static let typeDisplayRepresentation: TypeDisplayRepresentation = "显示方式"

    static let caseDisplayRepresentations: [ProviderSelectionMode: DisplayRepresentation] = [
        .followApp: DisplayRepresentation(
            title: "跟随 App",
            subtitle: "和 App 里「设为小组件默认项」保持一致"
        ),
        .specific: DisplayRepresentation(
            title: "固定显示",
            subtitle: "始终显示下面选中的那一家"
        )
    ]
}

struct ProviderEntity: AppEntity, Hashable {
    static let typeDisplayRepresentation: TypeDisplayRepresentation = "AI 供应商"
    static let defaultQuery = ProviderEntityQuery()

    var id: String
    var name: String
    var kindRaw: String

    var displayRepresentation: DisplayRepresentation {
        DisplayRepresentation(title: "\(name)", subtitle: "\(kindRaw)")
    }

    var kind: ProviderKind { ProviderKind(rawValue: kindRaw) ?? .custom }
    var uuid: UUID? { UUID(uuidString: id) }

    static func make(from config: ProviderConfig) -> ProviderEntity {
        ProviderEntity(id: config.id.uuidString, name: config.name, kindRaw: config.kind.displayName)
    }
}

struct ProviderEntityQuery: EntityQuery {
    func entities(for identifiers: [String]) async throws -> [ProviderEntity] {
        all().filter { identifiers.contains($0.id) }
    }

    func suggestedEntities() async throws -> [ProviderEntity] {
        all()
    }

    func defaultResult() async -> ProviderEntity? {
        all().first
    }

    private func all() -> [ProviderEntity] {
        SharedStore.shared.load().providers.map(ProviderEntity.make(from:))
    }
}

struct ProviderSelectionIntent: WidgetConfigurationIntent {
    static let title: LocalizedStringResource = "选择供应商"
    static let description = IntentDescription(
        "默认跟随 App 里选中的供应商，在 App 里切换会立刻同步过来；也可以固定显示某一个。"
    )

    @Parameter(title: "显示方式", default: .followApp)
    var mode: ProviderSelectionMode

    @Parameter(title: "固定显示")
    var provider: ProviderEntity?

    init() {
        mode = .followApp
    }

    func perform() async throws -> some IntentResult {
        .result()
    }
}
