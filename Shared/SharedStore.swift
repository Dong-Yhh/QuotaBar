import Foundation

/// 主 App 与桌面小组件之间的共享状态。
///
/// 为什么不用 App Group：ad-hoc 签名（没有开发者团队）时
/// `containerURL(forSecurityApplicationGroupIdentifier:)` 会返回 nil。
/// 所以这里按「多候选目录」处理：
///
/// 1. App Group 容器 —— 用户填了开发者团队后自动启用，最正规；
/// 2. 当前进程的 Application Support —— 主 App 落在真实家目录，
///    沙盒化的小组件落在自己的容器里；
/// 3. 小组件容器 —— 主 App 未沙盒，可以把同一份数据写进小组件的沙盒容器。
///
/// 写入时尽量写到所有可写位置，读取时挑最新的那份。这样无论小组件是否开启沙盒都能拿到数据。
final class SharedStore {
    static let shared = SharedStore()

    static let appGroupID = "group.com.dongyu.quotabar"
    static let widgetBundleID = "com.dongyu.quotabar.widget"

    private let fileName = "state.json"
    private let folderName = "QuotaBar"
    private let lock = NSLock()

    private init() {}

    // MARK: - 位置

    private var candidates: [URL] {
        var result: [URL] = []

        if let group = FileManager.default
            .containerURL(forSecurityApplicationGroupIdentifier: Self.appGroupID) {
            result.append(group.appendingPathComponent(folderName, isDirectory: true))
        }

        let processHome = URL(fileURLWithPath: NSHomeDirectory(), isDirectory: true)
        result.append(processHome
            .appendingPathComponent("Library/Application Support", isDirectory: true)
            .appendingPathComponent(folderName, isDirectory: true))

        // 只有未沙盒的进程才需要推导小组件容器：
        // 沙盒里 NSHomeDirectory() 已经是容器本身，homeDirectoryForCurrentUser 也返回容器，
        // 再拼一次会得到一个不存在的嵌套路径（之前的版本确实写出过这种垃圾文件）。
        if !NSHomeDirectory().contains("/Library/Containers/") {
            let realHome = FileManager.default.homeDirectoryForCurrentUser
            result.append(realHome
                .appendingPathComponent("Library/Containers", isDirectory: true)
                .appendingPathComponent(Self.widgetBundleID, isDirectory: true)
                .appendingPathComponent("Data/Library/Application Support", isDirectory: true)
                .appendingPathComponent(folderName, isDirectory: true))
        }

        var seen = Set<String>()
        return result.filter { url in
            let key = url.standardizedFileURL.path
            return seen.insert(key).inserted
        }
    }

    var primaryURL: URL {
        candidates[0].appendingPathComponent(fileName)
    }

    // MARK: - 读写

    private static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return encoder
    }()

    private static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()

    func load() -> PersistedState {
        lock.lock()
        defer { lock.unlock() }

        let existing = candidates
            .map { $0.appendingPathComponent(fileName) }
            .filter { FileManager.default.fileExists(atPath: $0.path) }
            .sorted { modificationDate(of: $0) > modificationDate(of: $1) }

        for url in existing {
            if let data = try? Data(contentsOf: url),
               let state = try? Self.decoder.decode(PersistedState.self, from: data) {
                return state
            }
        }
        return PersistedState()
    }

    func save(_ state: PersistedState) {
        lock.lock()
        defer { lock.unlock() }

        guard let data = try? Self.encoder.encode(state) else { return }

        // 所有候选位置都写一份：主 App 未沙盒，可以同时写进真实家目录和小组件的沙盒容器，
        // 这样无论用户先装小组件还是先配置 App，两边都能读到同一份数据。
        for directory in candidates {
            try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let url = directory.appendingPathComponent(fileName)
            try? data.write(to: url, options: .atomic)
        }
    }

    private func modificationDate(of url: URL) -> Date {
        (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
    }

    // MARK: - 诊断

    struct Location {
        let path: String
        let exists: Bool
        let writable: Bool
        let updatedAt: Date?
    }

    func diagnostics() -> [Location] {
        candidates.map { directory in
            let fileURL = directory.appendingPathComponent(fileName)
            let exists = FileManager.default.fileExists(atPath: fileURL.path)
            let writable = FileManager.default.isWritableFile(atPath: directory.path)
                || FileManager.default.isWritableFile(atPath: directory.deletingLastPathComponent().path)
            return Location(
                path: fileURL.path,
                exists: exists,
                writable: writable,
                updatedAt: exists ? modificationDate(of: fileURL) : nil
            )
        }
    }
}
