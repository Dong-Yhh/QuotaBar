import Foundation

/// 用 LaunchAgent 实现开机自动启动。
///
/// 没有用 `SMAppService.mainApp`：那个 API 要求 App 有正式签名并放在 /Applications，
/// 本工程是 ad-hoc 签名，注册会失败。写一个 LaunchAgent plist 是同等可靠、
/// 而且不需要开发者账号的做法。
enum LaunchAtLogin {
    static let label = "com.dongyu.quotabar"

    static var plistURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents", isDirectory: true)
            .appendingPathComponent("\(label).plist")
    }

    static var isEnabled: Bool {
        FileManager.default.fileExists(atPath: plistURL.path)
    }

    enum LaunchAtLoginError: LocalizedError {
        case notAnAppBundle(String)
        case writeFailed(String)
        case launchctlFailed(String)

        var errorDescription: String? {
            switch self {
            case .notAnAppBundle(let path):
                return "开机启动只能给 .app 包设置，当前路径是 \(path)。请先把 QuotaBar.app 放进「应用程序」再打开。"
            case .writeFailed(let message):
                return "写入启动项失败：\(message)"
            case .launchctlFailed(let message):
                return "注册启动项失败：\(message)"
            }
        }
    }

    static func setEnabled(_ enabled: Bool) throws {
        if enabled {
            try enable()
        } else {
            disable()
        }
    }

    /// LaunchAgent 的内容。抽出来是为了能单独验证 plist 的合法性。
    static var agentConfiguration: [String: Any] {
        // launchd 启动 GUI 程序最稳的方式是走 open。
        return [
            "Label": label,
            "ProgramArguments": ["/usr/bin/open", "-a", Bundle.main.bundlePath],
            "RunAtLoad": true,
            "LimitLoadToSessionType": "Aqua",
            "ProcessType": "Interactive"
        ]
    }

    static func plistData() throws -> Data {
        try PropertyListSerialization.data(
            fromPropertyList: agentConfiguration,
            format: .xml,
            options: 0
        )
    }

    private static func enable() throws {
        let bundlePath = Bundle.main.bundlePath
        guard bundlePath.hasSuffix(".app") else {
            throw LaunchAtLoginError.notAnAppBundle(bundlePath)
        }

        let directory = plistURL.deletingLastPathComponent()
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            try plistData().write(to: plistURL, options: .atomic)
        } catch {
            throw LaunchAtLoginError.writeFailed(error.localizedDescription)
        }

        // 先注销旧的（可能已经是已加载状态），再加载。
        _ = runLaunchctl(["bootout", "gui/\(getuid())/\(label)"])
        let result = runLaunchctl(["bootstrap", "gui/\(getuid())", plistURL.path])
        if !result.ok {
            // 老系统上 bootstrap 可能不可用，退回 legacy 写法。
            let legacy = runLaunchctl(["load", "-w", plistURL.path])
            if !legacy.ok {
                throw LaunchAtLoginError.launchctlFailed(result.message)
            }
        }
    }

    private static func disable() {
        _ = runLaunchctl(["bootout", "gui/\(getuid())/\(label)"])
        _ = runLaunchctl(["unload", "-w", plistURL.path])
        try? FileManager.default.removeItem(at: plistURL)
    }

    private struct CommandResult {
        var ok: Bool
        var message: String
    }

    private static func runLaunchctl(_ arguments: [String]) -> CommandResult {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/launchctl")
        process.arguments = arguments

        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe

        do {
            try process.run()
        } catch {
            return CommandResult(ok: false, message: error.localizedDescription)
        }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        let text = String(data: data, encoding: .utf8)?
            .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return CommandResult(ok: process.terminationStatus == 0, message: text)
    }
}
