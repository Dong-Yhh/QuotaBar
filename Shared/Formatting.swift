import Foundation
import SwiftUI

// MARK: - 数字格式化

enum QuotaFormatter {
    static func locale(for currencyCode: String?) -> Locale {
        switch currencyCode?.uppercased() {
        case "CNY", "RMB": return Locale(identifier: "zh_CN")
        case "USD": return Locale(identifier: "en_US")
        case "EUR": return Locale(identifier: "de_DE")
        default: return Locale(identifier: "zh_CN")
        }
    }

    static func money(_ value: Double, code: String?) -> String {
        let code = (code ?? "CNY").uppercased()
        let formatter = NumberFormatter()
        formatter.numberStyle = .currency
        formatter.currencyCode = code
        formatter.locale = locale(for: code)
        formatter.minimumFractionDigits = 2
        formatter.maximumFractionDigits = 2
        if let text = formatter.string(from: NSNumber(value: value)) {
            return text
        }
        return "\(code) \(String(format: "%.2f", value))"
    }

    static func plain(_ value: Double, compact: Bool = false) -> String {
        if compact && abs(value) >= 1000 {
            let formatter = NumberFormatter()
            formatter.numberStyle = .decimal
            formatter.maximumFractionDigits = 1
            formatter.minimumFractionDigits = 0
            return formatter.string(from: NSNumber(value: value)) ?? String(format: "%.0f", value)
        }
        if value == value.rounded() && abs(value) < 1_000_000 {
            return String(format: "%.0f", value)
        }
        return String(format: "%.2f", value)
    }

    /// 主数值：例如 "¥49.58" / "72%" / "1.2M"
    static func primary(_ metric: QuotaMetric) -> String {
        let value = metric.isRemainingPrimary ? (metric.resolvedRemaining ?? metric.resolvedUsed)
                                              : (metric.resolvedUsed ?? metric.resolvedRemaining)
        guard let value else { return "—" }
        switch metric.unit {
        case .currency:
            return money(value, code: metric.currencyCode)
        case .percent:
            return "\(plain(value))%"
        case .tokens:
            return plain(value, compact: true)
        case .requests, .credits:
            return plain(value, compact: true)
        }
    }

    static func value(_ value: Double, unit: QuotaUnit, currencyCode: String?) -> String {
        switch unit {
        case .currency: return money(value, code: currencyCode)
        case .percent: return "\(plain(value))%"
        default: return plain(value, compact: true)
        }
    }

    static func relative(_ date: Date) -> String {
        let formatter = RelativeDateTimeFormatter()
        formatter.locale = Locale(identifier: "zh_CN")
        formatter.unitsStyle = .short
        return formatter.localizedString(for: date, relativeTo: Date())
    }

    /// "3 小时 12 分后重置"
    static func resetDescription(_ date: Date) -> String {
        let interval = date.timeIntervalSinceNow
        if interval <= 0 { return "即将重置" }
        let totalMinutes = Int(interval / 60)
        let days = totalMinutes / (60 * 24)
        let hours = (totalMinutes % (60 * 24)) / 60
        let minutes = totalMinutes % 60
        if days > 0 { return "\(days) 天 \(hours) 小时后重置" }
        if hours > 0 { return "\(hours) 小时 \(minutes) 分后重置" }
        return "\(minutes) 分钟后重置"
    }
}

// MARK: - 健康度

enum QuotaLevel {
    case healthy
    case warning
    case critical
    case unknown

    static func of(_ metric: QuotaMetric, threshold: Double) -> QuotaLevel {
        guard let fraction = metric.remainingFraction else { return .unknown }
        if fraction <= threshold * 0.4 { return .critical }
        if fraction <= threshold { return .warning }
        return .healthy
    }

    var color: Color {
        switch self {
        case .healthy: return Color(red: 0.16, green: 0.72, blue: 0.45)
        case .warning: return Color(red: 0.98, green: 0.65, blue: 0.14)
        case .critical: return Color(red: 0.94, green: 0.31, blue: 0.31)
        case .unknown: return Color.secondary
        }
    }
}

// MARK: - 颜色

extension Color {
    init(hex: String) {
        let cleaned = hex.trimmingCharacters(in: CharacterSet.alphanumerics.inverted)
        var value: UInt64 = 0
        Scanner(string: cleaned).scanHexInt64(&value)
        let r, g, b, a: Double
        switch cleaned.count {
        case 8:
            r = Double((value >> 24) & 0xFF) / 255
            g = Double((value >> 16) & 0xFF) / 255
            b = Double((value >> 8) & 0xFF) / 255
            a = Double(value & 0xFF) / 255
        default:
            r = Double((value >> 16) & 0xFF) / 255
            g = Double((value >> 8) & 0xFF) / 255
            b = Double(value & 0xFF) / 255
            a = 1
        }
        self.init(.sRGB, red: r, green: g, blue: b, opacity: a)
    }
}

extension ProviderKind {
    var accent: Color { Color(hex: accentHex) }
}

// MARK: - JSON 取值

enum JSONPath {
    /// 支持 "data.balance" / "balance_infos.0.total_balance" 这类点号路径。
    static func value(in object: Any, path: String) -> Any? {
        var current: Any = object
        for component in path.split(separator: ".").map(String.init) {
            let key = component.trimmingCharacters(in: .whitespaces)
            if key.isEmpty { continue }
            if let dictionary = current as? [String: Any] {
                guard let next = dictionary[key] else { return nil }
                current = next
            } else if let array = current as? [Any] {
                guard let index = Int(key), index >= 0, index < array.count else { return nil }
                current = array[index]
            } else {
                return nil
            }
        }
        return current
    }

    static func double(in object: Any, path: String) -> Double? {
        guard let raw = value(in: object, path: path) else { return nil }
        if let number = raw as? NSNumber { return number.doubleValue }
        if let text = raw as? String { return Double(text.trimmingCharacters(in: .whitespaces)) }
        return nil
    }

    static func string(in object: Any, path: String) -> String? {
        guard let raw = value(in: object, path: path) else { return nil }
        if let text = raw as? String { return text }
        if let number = raw as? NSNumber { return number.stringValue }
        return nil
    }

    static func date(in object: Any, path: String) -> Date? {
        guard let raw = value(in: object, path: path) else { return nil }
        if let number = raw as? NSNumber {
            let seconds = number.doubleValue
            return Date(timeIntervalSince1970: seconds > 1e12 ? seconds / 1000 : seconds)
        }
        guard let text = raw as? String else { return nil }
        return ISO8601DateFormatter.flexible.date(from: text)
    }
}

extension ISO8601DateFormatter {
    static let flexible: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    static let plain: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }()

    static func parse(_ text: String) -> Date? {
        ISO8601DateFormatter.flexible.date(from: text) ?? ISO8601DateFormatter.plain.date(from: text)
    }
}
