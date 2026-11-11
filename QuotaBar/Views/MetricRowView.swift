import SwiftUI

struct MetricRow: View {
    var metric: QuotaMetric
    var threshold: Double
    var isFocused: Bool
    var onSelect: (() -> Void)?

    private var level: QuotaLevel { .of(metric, threshold: threshold) }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(metric.title)
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(.secondary)
                if isFocused {
                    Image(systemName: "dot.radiowaves.left.and.right")
                        .font(.system(size: 9))
                        .foregroundStyle(Color.accentColor)
                }
                Spacer(minLength: 8)
                Text(QuotaFormatter.primary(metric))
                    .font(.system(size: 19, weight: .semibold, design: .rounded))
                    .foregroundStyle(level == .unknown ? Color.primary : level.color)
                    .monospacedDigit()
            }

            QuotaProgressBar(fraction: metric.remainingFraction, level: level)

            if let detailText {
                Text(detailText)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { onSelect?() }
    }

    private var detailText: String? {
        var parts: [String] = []
        if let footnote = metric.footnote { parts.append(footnote) }
        if let resetsAt = metric.resetsAt { parts.append(QuotaFormatter.resetDescription(resetsAt)) }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }
}
