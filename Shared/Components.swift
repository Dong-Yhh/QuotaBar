import SwiftUI

struct ProviderBadge: View {
    let kind: ProviderKind
    var size: CGFloat = 30

    var body: some View {
        RoundedRectangle(cornerRadius: size * 0.3, style: .continuous)
            .fill(kind.accent.gradient)
            .frame(width: size, height: size)
            .overlay(
                Image(systemName: kind.symbolName)
                    .font(.system(size: size * 0.5, weight: .semibold))
                    .foregroundStyle(.white)
            )
    }
}

struct QuotaProgressBar: View {
    var fraction: Double?
    var level: QuotaLevel
    var height: CGFloat = 8

    var body: some View {
        GeometryReader { proxy in
            ZStack(alignment: .leading) {
                Capsule(style: .continuous)
                    .fill(Color.primary.opacity(0.12))
                if let fraction {
                    Capsule(style: .continuous)
                        .fill(level.color.gradient)
                        .frame(width: max(proxy.size.width * fraction, fraction > 0.005 ? height : 0))
                }
            }
        }
        .frame(height: height)
    }
}

struct StatusPill: View {
    var text: String
    var color: Color
    var symbol: String?

    var body: some View {
        HStack(spacing: 4) {
            if let symbol {
                Image(systemName: symbol).font(.system(size: 9, weight: .semibold))
            }
            Text(text).font(.system(size: 10, weight: .medium))
        }
        .padding(.horizontal, 7)
        .padding(.vertical, 3)
        .background(color.opacity(0.14), in: Capsule())
        .foregroundStyle(color)
    }
}
